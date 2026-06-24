# Ports the HyBloom fork's time-scroll Lua glue (no Lua source file survives to check the exact
# original binding, so this is designed from TimeScrollController.cs's own doc comments) - a
# full-screen time-lapse overlay (o.time_scroll, see TimeScrollController.cs) used for "X months
# later"-style jumps. Modeled on video.gd's video_play(): duration is computed once via the pure
# GetTotalDuration(...) (safe to call during a restore replay), then the chain is always blocked for
# that same duration via Delay() - only the actual visual Play() is skipped during a restore replay,
# same "no real time elapses during RestorePath" reasoning as video_play()/wait_all().
class_name TimeScrollHelper extends BuiltIn

#@export
static func time_scroll(from_year, from_month, from_day, from_hour, from_min, from_sec,
		to_year, to_month, to_day, to_hour, to_min, to_sec, mid_duration=2.0, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var duration = o.time_scroll.GetTotalDuration(from_year, from_month, from_day, from_hour, from_min, from_sec,
		to_year, to_month, to_day, to_hour, to_min, to_sec, mid_duration)
	if not _nova.IsRestoring:
		o.time_scroll.Play(from_year, from_month, from_day, from_hour, from_min, from_sec,
			to_year, to_month, to_day, to_hour, to_min, to_sec, mid_duration)
	return entry.Delay(duration)
