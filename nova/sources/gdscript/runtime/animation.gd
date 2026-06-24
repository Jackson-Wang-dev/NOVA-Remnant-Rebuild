# Named AnimHelper rather than "Animation" to avoid colliding with Godot's own builtin Animation
# resource class.
class_name AnimHelper extends BuiltIn

# Ports Nova1's animation_high_level.lua wait - a bare fixed delay, as opposed to wait_all's "wait as
# long as another AnimationState still has steps queued". Used directly by chained call sites like
# tut05.txt's `:wait(1)`.
#@export
static func wait(duration: float, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	return entry.Delay(duration)

# Ports Nova1's animation_high_level.lua wait_all - queue a delay for as long as target_entry's
# AnimationState still has steps queued (TotalDuration, see AnimationEntry.cs), then stop it (in
# case anything is still actually running past that point, e.g. a Tween that overran). Typical
# usage is `wait_all(anim_hold)` so the per-dialogue anim can block on whatever anim_hold is still
# doing before continuing - see ch4.txt's `anim:wait_all(anim_hold):action(...)`.
#
# Uses a SceneTreeTimer rather than entry.Action(callback) - a GDScript Callable passed into any
# C# method parameter (AnimationEntry.Action included) arrives empty on this project's pinned
# Godot 4.6.3 mono, so target_entry.Stop() would silently never fire - see ActionAnimation.cs's
# caveat and graphics.gd's trans()/trans2(), which hit the same gap first.
#@export
static func wait_all(target_entry, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var duration = target_entry.TotalDuration
	entry = entry.Delay(duration)
	# During a restore replay nothing is actually animating (Play() is never called - see
	# AnimationState.SyncImmediate), so target_entry is already sitting at its final state the instant
	# this line runs; stopping it right away is the correct equivalent of "wait for it to finish, then
	# stop it". Scheduling the real SceneTreeTimer here instead (same as live play) would fire `duration`
	# real seconds after the replay returns, force-stopping whatever unrelated hold animation the player
	# has since queued for real on the same AnimationState track.
	if _nova.IsRestoring:
		target_entry.Stop()
	else:
		(Engine.get_main_loop() as SceneTree).create_timer(duration).timeout.connect(func(): target_entry.Stop())
	return entry

# Ports Nova1's animation_high_level.lua loop - func_ is called with the current chain tail and
# must return a new tail (extra animation steps queued onto it); returning null ends the loop. The
# recursion happens via a plain static helper (not a self-referential closure variable) since
# GDScript lambdas capture by value, not by the mutable upvalue reference Lua's local functions
# get - each lambda here only captures func_/res, which are never reassigned after capture, so a
# value capture is exactly correct. Relies on AnimationExecutor.OnFinishEntry reading
# entry.Children lazily right after the entry's Tween finishes (see AnimationExecutor.cs) - calling
# res.Action(...) synchronously inside this callback, before that read happens, is what lets the
# chain keep extending itself indefinitely.
#@export
static func loop(func_: Callable, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	return _loop_step(func_, entry)

static func _loop_step(func_: Callable, tail) -> Variant:
	var res = func_.call(tail)
	if res == null:
		return tail
	return res.Action(func(): _loop_step(func_, res))

# Ports Nova1's checkpoint_helper.lua anim_hold_begin/anim_hold_end, minus the checkpoint-creation
# restraint (RestrainCheckpoint) - nova2's save model replays the script from a NodeRecord rather
# than snapshotting state (see porting-guide.md's M1 decision log), so there is no mid-hold
# checkpoint to restrain in the first place. Both ends just reset anim_hold: stop() clears any
# leftover queued/running state from a previous hold (begin) or force-finishes whatever was queued
# during this hold (end) - mirrors Nova1's literal anim_hold:stop() call in both functions.
#@export
static func anim_hold_begin() -> void:
	o.anim_hold.Stop()

#@export
static func anim_hold_end() -> void:
	o.anim_hold.Stop()

# Ports Nova1's animation_presets.lua cam_punch - a quick camera dip + zoom-in/out punch. Nova1
# uses a sine-decay ShakeEasing for the position dip; Godot's Tween eases have no equivalent, so
# this approximates with a plain down-then-back move (same "cosmetic, can be simplified" call as
# the porting-guide.md M2 decision log made for other minor easing-only effects) - revisit with a
# custom easing callback if a side-by-side check against Nova1 shows it reads as too flat. The zoom
# uses an explicit captured base size rather than nova2's additive Relative flag, since Nova1's
# camera-size relative here is multiplicative (size *= 0.9), not additive.
#@export
static func cam_punch(entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var cam = o.cam
	var base_size = cam.size
	var dip = Vector3(0, -0.2, 0)

	entry.PropertyVector3(cam, "position", dip, 0.2, true).PropertyVector3(cam, "position", -dip, 0.2, true)
	var zoom = entry.PropertyDouble(cam, "size", base_size * 0.9, 0.05, false)
	return zoom.PropertyDouble(cam, "size", base_size, 0.35, false)

# Arms a one-shot auto-advance for the *current* dialogue step: once whatever it's blocking on
# (text reveal, o.anim/o.anim_hold's queued entries, voice) all finish, the engine steps to the next
# entry on its own - no click needed - regardless of whether the player has Auto mode toggled on.
# Not tied to any specific animation type (video, a long trans(), a cam_punch() chain, ...) - call it
# from any dialogue step that should "play itself out, then continue" rather than wait on a click.
# See AutoSkipController.ForceAdvance for the engine side (GameViewController.ForceAdvance) and why a
# click is suppressed while this is pending. One-shot and scoped to this step only: AutoSkipController
# clears it on the next DialogueChanged regardless of whether it ever fired, so a step that doesn't
# call auto_step() again never inherits a stale pending flag from an earlier one.
#@export
static func auto_step() -> void:
	_nova.GameViewController.ForceAdvance()
