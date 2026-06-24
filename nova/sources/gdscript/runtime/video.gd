# Ports Nova1's video.lua - a single full-screen VideoStreamPlayer (o.video, see VideoController.cs)
# rather than Nova1's VideoController+VideoPlayer pair, since Godot's VideoStreamPlayer already does
# its own audio playback internally (no separate AudioSource routing needed the way Unity's VideoPlayer
# requires). Clips live at resources/videos/<name>.ogv (Godot's built-in Theora codec).
class_name VideoHelper extends BuiltIn

#@export
static func video(video_name: String) -> void:
	o.video.SetVideo(video_name)

#@export
static func video_hide() -> void:
	o.video.ClearVideo()

# video_play calls Play() immediately rather than deferring it via entry.Action(callback) - same
# Callable-arrives-empty reason as graphics.gd's trans()/animation.gd's wait_all() (see
# ActionAnimation.cs's caveat). duration defaults to the clip's own length (mirrors Nova1's
# `duration = duration or videoPlayer.clip.length`), then blocks the chain on a plain Delay - there's
# no per-frame seek/sync needed since the VideoStreamPlayer just runs on its own once started.
#
# Skipped entirely during a restore replay, same reasoning as wait_all()/fade_out(): RestorePath runs
# every eager block synchronously with no real time elapsing, so actually starting playback (and its
# audio track) here would just be wasted decoding for a clip the replay is about to fast-forward past
# anyway. The video stays loaded (from the replayed video() call) but paused - the same end state
# Nova1's own VideoController.Restore leaves it in (it only ever calls SetVideo, never Play, on load).
#@export
static func video_play(duration=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	if _nova.IsRestoring:
		return entry
	var length = o.video.PlayVideo()
	duration = duration if duration != null else length
	return entry.Delay(duration)
