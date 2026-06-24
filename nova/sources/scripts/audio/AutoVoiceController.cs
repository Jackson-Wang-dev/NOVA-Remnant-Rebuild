using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Nova;

/// <summary>
/// Per-character auto-voice bookkeeping (Nova1's AutoVoice) plus the actual dispatch for both
/// auto-triggered and explicit (Say) voice lines, since nova2 has no per-character node yet to host
/// a Say method of its own - both route through the single "voice" AudioPlayerController channel.
///
/// Registered directly into StateManager._states (not wrapped in a generic PropertyState, since its
/// state is dictionary-shaped, not a handful of named properties) so ResetToBaseline() can clear it
/// before a restore replay - the replay then re-runs whatever auto_voice_on/off calls sit on the
/// replayed path, exactly like Nova1's restore replays AutoVoiceRestoreData implicitly through script.
/// </summary>
public partial class AutoVoiceController : RefCounted, IStateObject
{
    private const int PadWidth = 6;

    private readonly Dictionary<string, bool> _enabled = [];
    private readonly Dictionary<string, int> _index = [];
    private readonly Dictionary<string, string> _prefix = [];
    private double _delay;

    // Set by an explicit Say() call; suppresses the next auto-voice trigger for this dialogue entry,
    // mirroring Nova1's auto_voice_skip()/auto_voice_overridden flag in auto_voice.lua.
    private bool _overridden;
    private (string voicePath, double delay)? _pending;

    /// <summary>
    /// Whatever PlayVoice() resolved (auto or explicit Say) for the dialogue entry currently being
    /// processed, or null if this entry has no voice. Reset at the top of every OnDialogueChanged so a
    /// voiceless entry doesn't inherit the previous entry's value. Read by BacklogViewController (a
    /// later DialogueChanged subscriber - core ISingleton objects like this one are always constructed
    /// before any UI ViewController's _EnterTree runs, so subscription order is guaranteed, not
    /// incidental) to remember each history entry's voice for on-demand replay.
    /// </summary>
    public (string VoicePath, double Delay)? LastVoice { get; private set; }

    // What PlayVoice() would have played for the most recently processed dialogue entry while
    // IsRestoring was true. RestorePath only flips IsRestoring back to false *after* its whole replay
    // loop finishes, so even the landed-on (last) entry's own DialogueChanged fires while still
    // "restoring" - without this, that last entry's voice would be silently dropped along with every
    // other replayed entry's. Played once restore actually finishes (RestoreStarts(false)), mirroring
    // Nova1's separate GameCharacterController.ReplayVoice step for the line a restore lands on.
    //
    // Must be reset to null at the top of every OnDialogueChanged (see there), not just overwritten
    // inside PlayVoice() - a voiceless entry never calls PlayVoice() at all, so without an unconditional
    // reset, landing a restore replay on a voiceless entry left this holding whatever voice the
    // *previous* (voiced) entry queued, and RestoreStarts(false) would play that stale, wrong line as
    // soon as the restore finished (load a save / jump via Backlog onto a voiceless line, or onto a line
    // whose own auto_voice_delay made the wrong line's audio only become audible a moment later, after
    // the player's next click).
    private (string voicePath, double delay)? _deferredVoice;

    public AutoVoiceController()
    {
        GameState.Instance.DialogueChanged.Subscribe(OnDialogueChanged);
        GameState.Instance.RestoreStarts.Subscribe(OnRestoreStarts);
    }

    public void SetEnabled(string name, bool value) => _enabled[name] = value;
    public void SetIndex(string name, int value) => _index[name] = value;
    public void SetPrefix(string name, string value) => _prefix[name] = value;
    public void SetDelay(double value) => _delay = value;

    public void DisableAll()
    {
        foreach (var name in _enabled.Keys.ToList())
        {
            _enabled[name] = false;
        }
    }

    /// <summary>Explicit voice line, e.g. NovaScript's say(speaker_name, voice_name, delay). Queued
    /// here rather than played immediately so it fires in sync with DialogueChanged, same as Nova1's
    /// GameCharacterController.Say deferring playback to its own OnDialogueChanged.</summary>
    public void Say(string speakerName, string voiceName, double delay, bool overrideAutoVoice)
    {
        _pending = ($"{speakerName}/{voiceName}", delay);
        if (overrideAutoVoice)
        {
            _overridden = true;
        }
    }

    private void OnDialogueChanged(DialogueChangedData data)
    {
        LastVoice = null;
        _deferredVoice = null;

        if (_pending is { } pending)
        {
            _pending = null;
            PlayVoice(pending.voicePath, pending.delay);
        }

        if (_overridden)
        {
            _overridden = false;
            return;
        }

        var name = data.DisplayData.CharacterName;
        if (string.IsNullOrEmpty(name) || !_enabled.GetValueOrDefault(name))
        {
            return;
        }

        var index = _index.GetValueOrDefault(name, 0);
        var prefix = _prefix.GetValueOrDefault(name, "");
        var voiceName = prefix + index.ToString().PadLeft(PadWidth, '0');
        PlayVoice($"{name}/{voiceName}", _delay);
        _index[name] = index + 1;
        _delay = 0;
    }

    private void PlayVoice(string voicePath, double delay)
    {
        LastVoice = (voicePath, delay);
        if (GameState.Instance.IsRestoring)
        {
            // Don't actually play yet - just remember it, so a fast replay through many dialogue
            // entries doesn't fire a burst of overlapping clips. Whichever entry is replayed last
            // (including the final, landed-on one) overwrites this, and that's exactly the one whose
            // voice should actually be heard once the replay finishes.
            _deferredVoice = (voicePath, delay);
            return;
        }
        PlayVoiceNow(voicePath, delay);
    }

    private AudioPlayerController VoiceChannel =>
        (AudioPlayerController)((PropertyState)ObjectManager.Instance.Objects["voice"]).Binding;

    private void PlayVoiceNow(string voicePath, double delay)
    {
        VoiceChannel.PlayDelayed(voicePath, delay);
    }

    /// <summary>
    /// On-demand replay of a previously recorded voice (BacklogViewController's "play voice" button) -
    /// always plays immediately, bypassing the IsRestoring deferral in PlayVoice since this is a direct
    /// user action, not a line being (re-)displayed by script execution.
    /// </summary>
    public void PlayImmediate(string voicePath, double delay) => PlayVoiceNow(voicePath, delay);

    /// <summary>Whether the voice channel is currently mid-line (including its pre-delay wait) -
    /// read by AutoSkipController so Auto waits for it and Skip cuts it off.</summary>
    public bool IsVoicePlaying => VoiceChannel.IsPlaying;

    /// <summary>Force-stops whatever voice line is currently playing (Skip cutting it short).</summary>
    public void StopVoice() => VoiceChannel.Stop();

    private void OnRestoreStarts(bool isStarting)
    {
        if (isStarting || _deferredVoice is not { } voice)
        {
            return;
        }
        _deferredVoice = null;
        PlayVoiceNow(voice.voicePath, voice.delay);
    }

    public void Sync() { }
    public void SyncImmediate() { }
    public void SyncBackend() { }

    public void ResetToBaseline()
    {
        _enabled.Clear();
        _index.Clear();
        _prefix.Clear();
        _delay = 0;
        _overridden = false;
        _pending = null;
        _deferredVoice = null;
    }
}
