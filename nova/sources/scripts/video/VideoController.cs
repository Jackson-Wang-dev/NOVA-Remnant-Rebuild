using Godot;

namespace Nova;

/// <summary>
/// Full-screen video playback, mirroring Nova1's VideoController. Bound directly to ObjectManager
/// (like VfxManager/AutoVoiceController), not PropertyState - video()/video_play()/video_hide() are
/// real actions (load a stream, start/stop a player) that need to take effect immediately within the
/// same eager block, not the deferred dirty-then-Sync() semantics PropertyState gives plain properties.
///
/// Self-registers as an IStateObject (see StateManager.RegisterState) purely for the ResetToBaseline
/// hook: GameState.StartGame/MoveTo sweep every tracked state's ResetToBaseline() before a fresh game
/// or a restore replay starts, which is exactly when a video left playing from the live session (now
/// abandoned) needs to be force-cleared - same reasoning as VfxManager.ClearAllLayers(), but reachable
/// for free through the existing generic sweep instead of a bespoke call site.
/// </summary>
public partial class VideoController : VideoStreamPlayer, IStateObject
{
    [Export]
    private string _bindName;
    [Export]
    private string _videoFolder;

    private string _currentVideo;

    public override void _EnterTree()
    {
        StateManager.Instance.RegisterState(_bindName, this);
    }

    /// <summary>Loads videoName's clip and shows the player, but does not start playback - mirrors
    /// Nova1's SetVideo (Prepare() without Play()). Playback is a separate, explicit Play() call so a
    /// script can stage the clip and start it in two distinct NovaScript statements (video(name) then
    /// anim:video_play()), matching test_video.txt's call shape.</summary>
    public void SetVideo(string videoName)
    {
        if (videoName == _currentVideo)
        {
            return;
        }
        Stream = GD.Load<VideoStream>($"{Assets.ResourceRoot}videos/{videoName}.ogv");
        Visible = true;
        _currentVideo = videoName;
    }

    /// <summary>Stops playback and detaches the clip, hiding the player - mirrors Nova1's ClearVideo
    /// (called after the animation entry driving video_play's wait is done).</summary>
    public void ClearVideo()
    {
        if (string.IsNullOrEmpty(_currentVideo))
        {
            return;
        }
        Stop();
        Stream = null;
        Visible = false;
        _currentVideo = null;
    }

    /// <summary>Starts playback of whatever clip SetVideo last staged and returns its length in
    /// seconds, so video.gd's video_play() can default its blocking duration to "the whole clip"
    /// without nova2 having to remember the length anywhere else (Nova1's video.lua does the same -
    /// duration = duration or videoPlayer.clip.length). Named PlayVideo, not Play, so it doesn't
    /// collide with VideoStreamPlayer's own native Play() this class inherits. No-ops (returns 0) if
    /// no clip is staged.</summary>
    public double PlayVideo()
    {
        if (Stream == null)
        {
            return 0.0;
        }
        Play();
        return GetStreamLength();
    }

    public void Sync() { }
    public void SyncImmediate() { }
    public void SyncBackend() { }

    public void ResetToBaseline() => ClearVideo();
}
