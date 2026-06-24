using System.Collections.Generic;
using Godot;

namespace Nova;

public class StateManager : ISingleton
{
    private GameState _gameState;
    private ObjectManager _objectManager;

    private readonly List<IStateObject> _states = [];

    public AnimationState Animation { get; private set; }
    /// <summary>
    /// A second, independent animation track bound to GDScript as "anim_hold" - mirrors Nova1's
    /// AnimationType.Holding (vs the PerDialogue Animation above). Used by the Colorless port's
    /// anim_hold_begin/anim_hold_end + wait_all: animations queued here run alongside the main
    /// per-dialogue animation but are deliberately not consulted by GameViewController.Step()'s
    /// IsRunning gate, so they don't block click-to-advance - that's the entire point of the split.
    /// </summary>
    public AnimationState HoldAnimation { get; private set; }
    public AutoVoiceController AutoVoice { get; private set; }

    public void OnEnter()
    {
        _gameState = GameState.Instance;
        _gameState.DialogueChangedEarly.Subscribe(_ => OnDialogueChangedEarly());

        _objectManager = ObjectManager.Instance;
        Animation = new AnimationState("anim");
        Animation.OnFinish.Subscribe(SyncImmediate);
        _states.Add(Animation);
        _objectManager.BindObject("anim", Animation.Root);

        HoldAnimation = new AnimationState("anim_hold");
        HoldAnimation.OnFinish.Subscribe(SyncImmediate);
        _states.Add(HoldAnimation);
        _objectManager.BindObject("anim_hold", HoldAnimation.Root);

        AutoVoice = new AutoVoiceController();
        _states.Add(AutoVoice);
        _objectManager.BindObject("auto_voice", AutoVoice);
    }

    public void OnExit() { }

    public void OnReady() { }

    public void SyncBackend()
    {
        foreach (var state in _states)
        {
            state.SyncBackend();
        }
    }

    public void SyncImmediate()
    {
        foreach (var state in _states)
        {
            state.SyncImmediate();
        }
    }

    public void Sync()
    {
        foreach (var state in _states)
        {
            state.Sync();
        }
    }

    private void OnDialogueChangedEarly()
    {
        if (_gameState.IsRestoring || _gameState.IsUpgrading)
        {
            // SyncBackend() is a no-op placeholder for a future snapshot-based restore; our restore
            // replays the script instead (see GameState.RestorePath), so the dirty PropertyState values
            // it produces need to actually reach the real nodes - just without animating, hence
            // SyncImmediate() (which also force-finalizes any in-flight animation hold) rather than Sync().
            SyncImmediate();
        }
        else
        {
            Sync();
        }
    }

    public void BindPropertyState(string name, PropertyState state)
    {
        _states.Add(state);
        _objectManager.BindObject(name, state);
    }

    /// <summary>
    /// Like BindPropertyState, but for a PropertyState with no object-manager binding name of its own
    /// (e.g. VfxManager's per-shader materials, which callers reach directly through VfxManager.GetState
    /// rather than by name lookup). Still needs tracking here so baseline-reset/Sync apply to it.
    /// </summary>
    public void TrackState(PropertyState state)
    {
        _states.Add(state);
    }

    /// <summary>
    /// Like BindPropertyState, but for an IStateObject whose state isn't a handful of named
    /// properties (e.g. dictionary-shaped, like AutoVoiceController/AvatarController) and so can't go
    /// through PropertyState's generic Get/Set-by-name forwarding. Unlike AutoVoice (constructed
    /// directly here since it has no scene presence), AvatarController is a real Node placed in a
    /// .tscn, so it self-registers from its own _EnterTree() the same way BindPropertyState callers do.
    /// </summary>
    public void RegisterState(string name, IStateObject state)
    {
        _states.Add(state);
        _objectManager.BindObject(name, (GodotObject)state);
    }

    /// <summary>
    /// Reset all bound state back to its pre-game baseline. Must run before a restore replay starts
    /// (see GameState.LoadGame): RestorePath only re-applies whatever sits on the replayed path, so
    /// without this, anything mutated only after the save point (in the now-abandoned future) would
    /// keep its stale live value instead of reverting.
    /// </summary>
    public void ResetToBaseline()
    {
        foreach (var state in _states)
        {
            state.ResetToBaseline();
        }
    }

    public static StateManager Instance => NovaController.Instance.GetObj<StateManager>();
}
