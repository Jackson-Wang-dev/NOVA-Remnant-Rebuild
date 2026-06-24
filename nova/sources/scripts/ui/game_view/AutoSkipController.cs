namespace Nova;

/// <summary>
/// Auto-advance / skip-already-read state machine, mirroring Nova1's DialogueState
/// Normal/Auto/FastForward modes. Driven every frame from GameViewController._Process (the first
/// frame-polling UI logic in this project - GameState.Step() itself is click-driven, so there's no
/// existing per-frame hook to piggyback on).
///
/// Auto waits for the current node's blocking animations (AnimationState.IsRunning) and voice
/// (AutoVoiceController.IsVoicePlaying) to finish before stepping - nova2's animation system has no
/// concept of an unbounded/looping animation at all (every PropertyAnimation has a finite Duration;
/// ambient effects like drifting clouds/rain are driven purely by each .gdshader's own TIME uniform
/// and never enter AnimationState), so IsRunning already only ever reflects animations a node should
/// be waited on for, with no extra categorization needed.
///
/// Skip force-completes text/animation/voice every step instead of waiting, and (by default) only
/// advances through dialogue already reached in some previous playthrough/branch
/// (DialogueChangedData.IsReachedAnyHistory, the same flag Nova1's CheckpointManager.
/// IsReachedAnyHistory feeds into its own fastForwardUnread gate) - stopping the moment it lands on
/// unread content so the player reads it normally instead of skipping past new story. AllowSkipUnread
/// lifts that restriction; it's a script-facing dev switch (see dialogue_box.gd's allow_skip_unread()),
/// not a player-facing settings toggle this round.
/// </summary>
public class AutoSkipController
{
    public enum Mode
    {
        Normal,
        Auto,
        Skip
    }

    private const double AutoPaceDelay = 1.0;
    private const double SkipStepDelay = 0.05;

    private readonly GameViewController _gameView;
    private readonly GameState _gameState;
    private readonly AnimationState _animation;
    private readonly AutoVoiceController _autoVoice;

    public Mode CurrentMode { get; private set; } = Mode.Normal;
    public bool AllowSkipUnread { get; set; }

    // Debounces against re-stepping before the previous Step()'s dialogue actions (BeforeCheckpoint/
    // Default/AfterDialogue, run via an async Task kicked off from GameState.UpdateDialogue and not
    // guaranteed to have settled within the Step() call itself) have actually landed - a hazard only
    // an automated, faster-than-human stepper like this one can hit.
    private bool _awaitingStep;
    private bool _lastIsReachedAnyHistory;
    private double _readyElapsed;
    private double _skipElapsed;

    // Set by ForceAdvance() (see animation.gd's auto_step()) - a script-armed, one-shot version of
    // Auto's own "wait for text/animation/voice to clear, then step" condition, but independent of
    // whether the player has Auto toggled on, with no AutoPaceDelay pause (the point is to continue
    // the instant whatever this dialogue step is blocking on - e.g. a video.gd video_play() - finishes
    // playing, not to add an extra human-reading-pace beat on top). Scoped to exactly one dialogue
    // step: cleared on the *next* entry's DialogueWillChange (before its own Default action runs) so
    // a step that didn't call auto_step() again never inherits a stale pending flag from an earlier
    // one - NOT on DialogueChanged, which fires for *this* entry, after its Default action (where
    // auto_step() itself is called) already ran; clearing there would wipe the flag the same step just
    // armed, before TickForceAdvance ever gets a chance to see it.
    private bool _forceAdvancePending;
    public bool IsForceAdvancePending => _forceAdvancePending;
    public void ForceAdvance() => _forceAdvancePending = true;

    public AutoSkipController(GameViewController gameView, AnimationState animation, AutoVoiceController autoVoice)
    {
        _gameView = gameView;
        _gameState = GameState.Instance;
        _animation = animation;
        _autoVoice = autoVoice;

        _gameState.DialogueWillChange.Subscribe(OnDialogueWillChange);
        _gameState.DialogueChanged.Subscribe(OnDialogueChanged);
        _gameState.ChoiceOccurs.Subscribe(OnInterrupt);
        _gameState.RouteEnded.Subscribe(OnInterrupt);
        _gameState.GameStarted.Subscribe(Cancel);
    }

    public void Dispose()
    {
        _gameState.DialogueWillChange.Unsubscribe(OnDialogueWillChange);
        _gameState.DialogueChanged.Unsubscribe(OnDialogueChanged);
        _gameState.ChoiceOccurs.Unsubscribe(OnInterrupt);
        _gameState.RouteEnded.Unsubscribe(OnInterrupt);
        _gameState.GameStarted.Unsubscribe(Cancel);
    }

    public void ToggleAuto() => SetMode(CurrentMode == Mode.Auto ? Mode.Normal : Mode.Auto);
    public void ToggleSkip() => SetMode(CurrentMode == Mode.Skip ? Mode.Normal : Mode.Skip);
    public void Cancel() => SetMode(Mode.Normal);

    private void SetMode(Mode mode)
    {
        CurrentMode = mode;
        _readyElapsed = 0;
        _skipElapsed = 0;
    }

    private void OnDialogueWillChange() => _forceAdvancePending = false;

    private void OnDialogueChanged(DialogueChangedData data)
    {
        _awaitingStep = false;
        _lastIsReachedAnyHistory = data.IsReachedAnyHistory;
    }

    private void OnInterrupt<T>(T _) => Cancel();

    public void Tick(double delta)
    {
        switch (CurrentMode)
        {
            case Mode.Auto:
                TickAuto(delta);
                return;
            case Mode.Skip:
                TickSkip(delta);
                return;
        }

        // Mode.Normal only: Auto/Skip above already step the instant this same "ready" condition
        // clears (Auto with its own pacing pause, Skip immediately after force-completing everything)
        // - a forced advance armed mid-Auto/Skip is naturally satisfied by their own tick already
        // firing Step(), and gets cleared like normal by the OnDialogueChanged that follows. Only plain
        // Normal mode has no existing per-frame stepper to piggyback on, so it needs its own.
        if (_forceAdvancePending)
        {
            TickForceAdvance();
        }
    }

    private void TickAuto(double delta)
    {
        if (_awaitingStep)
        {
            return;
        }
        if (!_gameState.CanStepForward)
        {
            Cancel();
            return;
        }

        var box = _gameView.CurrentDialogueBox;
        var ready = (box == null || !box.IsTextRevealing) && !_animation.IsRunning && !_autoVoice.IsVoicePlaying;
        if (!ready)
        {
            _readyElapsed = 0;
            return;
        }

        _readyElapsed += delta;
        if (_readyElapsed < AutoPaceDelay)
        {
            return;
        }

        _readyElapsed = 0;
        _awaitingStep = true;
        _gameState.Step();
    }

    private void TickForceAdvance()
    {
        if (_awaitingStep)
        {
            return;
        }
        if (!_gameState.CanStepForward)
        {
            _forceAdvancePending = false;
            return;
        }

        var box = _gameView.CurrentDialogueBox;
        var ready = (box == null || !box.IsTextRevealing) && !_animation.IsRunning && !_autoVoice.IsVoicePlaying;
        if (!ready)
        {
            return;
        }

        _forceAdvancePending = false;
        _awaitingStep = true;
        _gameState.Step();
    }

    private void TickSkip(double delta)
    {
        if (_awaitingStep)
        {
            return;
        }
        if (!_gameState.CanStepForward)
        {
            Cancel();
            return;
        }
        if (!_lastIsReachedAnyHistory && !AllowSkipUnread)
        {
            Cancel();
            ConfirmDialog.Instance.ShowNotice("dialogue.noreadtext");
            return;
        }

        _skipElapsed += delta;
        if (_skipElapsed < SkipStepDelay)
        {
            return;
        }
        _skipElapsed = 0;

        var box = _gameView.CurrentDialogueBox;
        if (box != null && box.IsTextRevealing)
        {
            box.CompleteTextReveal();
        }
        _animation.Stop();
        _autoVoice.StopVoice();

        _awaitingStep = true;
        _gameState.Step();
    }
}
