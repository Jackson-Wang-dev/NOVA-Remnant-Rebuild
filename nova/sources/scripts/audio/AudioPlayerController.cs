using Godot;

namespace Nova;

/// <summary>
/// One audio channel (BGM/BGS/voice). PropertyState-bound so CurrentTrack/Volume participate in
/// save/restore and o.anim.PropertyDouble fades, the same way DialogueBoxController's Opacity does.
/// Mirrors Nova1's AudioController, which plays this same dual role for both bgm and bgs.
/// </summary>
public partial class AudioPlayerController : Node
{
    [Export]
    private string _bindName;
    [Export]
    private string _audioFolder;
    [Export]
    private bool _loop;
    [Export]
    private AudioStreamPlayer _player;

    private string _currentTrack;
    private double _volume = 1.0;
    private bool _busy;

    /// <summary>True from the moment PlayDelayed is called (including through its pre-delay wait)
    /// until playback actually finishes or is Stop()'d - used by AutoVoiceController/AutoSkipController
    /// to know whether a voice line is still "in progress" for auto-advance purposes.</summary>
    public bool IsPlaying => _busy;

    [Export]
    public string CurrentTrack
    {
        get => _currentTrack;
        set
        {
            _currentTrack = value;
            UpdateTrack();
        }
    }

    [Export]
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            UpdateVolume();
        }
    }

    public override void _EnterTree()
    {
        var state = new PropertyState(this)
        {
            InitProperties = ["CurrentTrack", "Volume"]
        };
        StateManager.Instance.BindPropertyState(_bindName, state);
    }

    public override void _Ready()
    {
        UpdateVolume();
        _player.Finished += OnFinished;
    }

    private void OnFinished()
    {
        _busy = false;
        // Most BGM assets loop via their own embedded Ogg loop points, so Finished normally never
        // fires mid-track; this is the fallback for tracks that don't carry a loop point.
        if (_loop && !string.IsNullOrEmpty(_currentTrack))
        {
            _player.Play();
            _busy = true;
        }
    }

    private void UpdateTrack()
    {
        if (!IsNodeReady())
        {
            return;
        }
        if (string.IsNullOrEmpty(_currentTrack))
        {
            _player.Stop();
            _player.Stream = null;
            return;
        }
        var path = $"{Assets.ResourceRoot}audio/{_audioFolder}/{_currentTrack}.ogg";
        _player.Stream = GD.Load<AudioStream>(path);
        _player.Play();
    }

    private void UpdateVolume()
    {
        if (!IsNodeReady())
        {
            return;
        }
        _player.VolumeDb = (float)Mathf.LinearToDb(_volume);
    }

    /// <summary>Plays voicePath ("character/file") after delay seconds. Callers (AutoVoiceController)
    /// are responsible for not calling this while GameState.IsRestoring - see AutoVoiceController.
    /// PlayVoice/_deferredVoice for why that's not a simple early-return here.</summary>
    public async void PlayDelayed(string voicePath, double delay)
    {
        _busy = true;
        if (delay > 0)
        {
            await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        }
        CurrentTrack = voicePath;
    }

    /// <summary>Force-stops playback immediately (e.g. Skip cutting off a voice line). Godot's
    /// AudioStreamPlayer.Stop() does not emit Finished, so _busy has to be cleared here manually.</summary>
    public void Stop()
    {
        _busy = false;
        _player.Stop();
    }
}
