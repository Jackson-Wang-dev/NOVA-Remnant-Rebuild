class_name Audio extends BuiltIn

# play/stop fold the fade duration into the same call rather than a separate anim:fade_in/fade_out
# chain (Nova1's NovaAnimation chain syntax isn't ported - same simplification graphics.gd already
# makes for move/tint). channel is "bgm"/"bgs"/"voice" (bound PropertyState names, see game.tscn).

#@export
static func play(channel: Variant, track_name: String, vol=0.5, duration=null) -> void:
	var obj = _get_obj(channel)
	if duration != null and duration > 0:
		obj.Volume = 0.0
		obj.CurrentTrack = track_name
		o.anim.PropertyDouble(obj, "Volume", vol, duration, false)
	else:
		obj.Volume = vol
		obj.CurrentTrack = track_name

#@export
static func stop(channel: Variant, duration=null) -> void:
	var obj = _get_obj(channel)
	if duration != null and duration > 0:
		# Leaves the track loaded at Volume 0 rather than actually stopping it - there is no
		# animation-finished callback hook (Nova1's ActionAnimationProperty chain isn't ported,
		# see porting-guide.md decision record), so a real stop-after-fade would need one.
		o.anim.PropertyDouble(obj, "Volume", 0.0, duration, false)
	else:
		obj.CurrentTrack = null

#@export
static func volume(channel: Variant, value: float, duration=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var obj = _get_obj(channel)
	if duration != null and duration > 0:
		return entry.PropertyDouble(obj, "Volume", value, duration, false)
	else:
		obj.Volume = value
		return entry

# Ports Nova1's audio.lua fade_in/fade_out - entry-chainable counterparts to play()/stop() that
# animate the volume ramp instead of folding it into the same call (play()/stop()'s existing
# duration param is still the right choice for the common immediate case - these exist because
# Colorless's anim:fade_in/fade_out chain off arbitrary entries, e.g. anim_hold).
#
# fade_in calls play() immediately rather than deferring it via entry.Action(callback) - that
# Callable arrives empty once passed through a C# method parameter on this project's pinned Godot
# 4.6.3 mono (see ActionAnimation.cs's caveat), so it would silently never fire. Calling play()
# right away is observably identical for every actual call site (entry is always freshly the
# per-dialogue/hold root with nothing else queued ahead of it yet), same approximation
# graphics.gd's trans()/trans2() and box_hide_show already make.
#@export
static func fade_in(channel: Variant, track_name: String, vol=0.5, duration=1.0, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var obj = _get_obj(channel)
	play(channel, track_name, 0.0)
	return volume(obj, vol, duration, entry)

# fade_out's stop() runs off a SceneTreeTimer instead of entry.Action(callback) for the same
# Callable-arrives-empty reason as fade_in above. Skips straight to stop() during a restore replay
# (same reasoning as graphics.gd's trans()/animation.gd's wait_all()) - otherwise the real-time timer
# scheduled here fires `duration` real seconds after the replay returns and stops whatever the player
# has since started playing for real on the same channel.
#@export
static func fade_out(channel: Variant, duration=1.0, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var obj = _get_obj(channel)
	entry = volume(obj, 0.0, duration, entry)
	if _nova.IsRestoring:
		stop(channel)
	else:
		(Engine.get_main_loop() as SceneTree).create_timer(duration).timeout.connect(func(): stop(channel))
	return entry

#@export
static func sound(track_name: String, vol=0.5) -> void:
	o.sound.PlayClip(track_name, vol)

#@export
static func say(speaker_name: String, voice_name: String, delay=0.0, override_auto_voice=true) -> void:
	o.auto_voice.Say(speaker_name, voice_name, delay, override_auto_voice)

#@export
static func auto_voice_on(name: String, index: Variant) -> void:
	if index is Array:
		o.auto_voice.SetPrefix(name, index[0])
		index = index[1]
	o.auto_voice.SetEnabled(name, true)
	o.auto_voice.SetIndex(name, index)

#@export
static func auto_voice_off(name: String) -> void:
	o.auto_voice.SetEnabled(name, false)

#@export
static func auto_voice_off_all() -> void:
	o.auto_voice.DisableAll()

#@export
static func set_auto_voice_delay(value: float) -> void:
	o.auto_voice.SetDelay(value)
