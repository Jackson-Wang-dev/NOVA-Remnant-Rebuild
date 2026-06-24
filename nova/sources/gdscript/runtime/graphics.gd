class_name Graphics extends BuiltIn

# entry is which AnimationEntry to queue the animated steps onto (defaults to the per-dialogue
# o.anim root) - lets Colorless's `entry:move(...)`/`anim_hold:move(...)` Lua chaining (entry
# captured from a previous wait_all/loop/move call, queuing relative to THAT point rather than
# always the dialogue root) translate to a trailing GDScript arg instead of needing GDScript to
# grow Lua's colon method-call sugar. Returns the entry the next chained call should attach to
# (the last branch queued, matching Nova1's animation_high_level.lua move() returning its last
# _then(...) - an arbitrary-but-consistent pick since position/scale/angle run in parallel anyway).
#@export
static func move(obj: Variant, coord: Variant, scale=null, angle=null, duration=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	obj = _get_obj(obj)
	# "cam" (MainCamera, Camera3D in Orthogonal projection) has no meaningful "scale" - Nova1's
	# CameraController special-cases this the same way (camera.size = scale instead of
	# transform.localScale = scale). Duck-typed on the native "size" property's presence, same
	# pattern as CurrentPose elsewhere in this file.
	var is_cam = "size" in obj
	if coord != null and not coord is Vector3:
		if scale == null:
			scale = _get_index(coord, 2, null)
		if angle == null:
			angle = _get_index(coord, 4, null)

		# coord's shorthand layout is [x, y, scale, z, angle] (matches Nova1's {x, y, scale, z, angle}
		# table, 1-indexed there so z is its 4th slot) - z must be read from index 3, not handed
		# straight to _get_vec3 alongside x/y, or it would read back the scale value sitting at index 2
		# instead (see porting-guide.md decision log: this silently moved obj/cam along z by whatever
		# the scale value was, whenever scale != z, e.g. ch3.txt's `move(cam, {1, 1, 4})` camera zoom).
		coord = _get_vec3([_get_index(coord, 0, null), _get_index(coord, 1, null), _get_index(coord, 3, null)], obj.position)
	# Normalize scale/angle to Vector3 regardless of whether they came from the coord shorthand
	# above or were passed directly as a scalar (e.g. move(obj, [x, y], 0.4)) - a bare scalar assigned
	# straight to the Vector3 "scale"/"rotation_degrees" property silently breaks the property.
	# Camera "size" is already a plain float, so it skips this Vector3 normalization entirely.
	if scale != null and not is_cam and not scale is Vector3:
		scale = _get_vec3(scale, obj.scale, func(s): return Vector3(s, s, 1))
	if angle != null and not angle is Vector3:
		angle = _get_vec3(angle, obj.rotation_degrees, func(s): return Vector3(0, 0, s))

	var scale_property = "size" if is_cam else "scale"
	if duration != null and duration > 0:
		if coord != null:
			entry.PropertyVector3(obj, "position", coord, duration, false)
		if scale != null:
			if is_cam:
				entry = entry.PropertyDouble(obj, scale_property, scale, duration, false)
			else:
				entry = entry.PropertyVector3(obj, scale_property, scale, duration, false)
		if angle != null:
			entry = entry.PropertyVector3(obj, "rotation_degrees", angle, duration, false)
		return entry
	else:
		if coord != null:
			obj.position = coord
		if scale != null:
			obj.set(scale_property, scale)
		if angle != null:
			obj.rotation_degrees = angle
		return entry

#@export
static func tint(obj, color, duration=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	obj = _get_obj(obj)
	color = _parse_color(color)

	# CompositeSpriteController-bound objects (character standing sprites) have no native "modulate"
	# (they're a plain Node3D container, not a Sprite3D) - they expose their own PascalCase "Modulate"
	# instead, which forwards to whichever Sprite3D is the current pose's live display. Same
	# CurrentPose duck-typing check show()/hide() already use.
	var property = "Modulate" if "CurrentPose" in obj else "modulate"
	if duration != null and duration > 0:
		return entry.PropertyColor(obj, property, color, duration, false)
	else:
		obj.set(property, color)
		return entry

# Ports Nova1's animation_high_level.lua env_tint - a second tint channel for standing character
# composites only (CompositeSpriteController.EnvironmentColor), multiplying with tint()'s own
# Modulate.RGB rather than replacing it (Nova1: base.color = _color * _environmentColor). Meant for
# slow ambient/lighting shifts (dusk, night) that should compose with tint()'s short performance cues
# instead of fighting over the same channel - see tut02.txt's "env_tint 与 tint 的效果可以叠加". Unlike
# tint(), there is no native-sprite fallback: Nova1's env_tint only ever targets
# GameCharacterController and warns otherwise.
#@export
static func env_tint(obj, color, duration=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	obj = _get_obj(obj)
	color = _parse_color(color)

	if not "CurrentPose" in obj:
		push_warning("Cannot find CompositeSpriteController for ", obj)
		return entry

	if duration != null and duration > 0:
		return entry.PropertyColor(obj, "EnvironmentColor", color, duration, false)
	else:
		obj.set("EnvironmentColor", color)
		return entry

#@export
static func show(obj, image_path, coord=null, color=null, duration=null) -> void:
	obj = _get_obj(obj)

	if coord != null:
		move(obj, coord)
	if color != null:
		tint(obj, color)

	# CompositeSpriteController-bound objects (character standing sprites) treat image_path as a
	# pose name/string instead of a flat texture path - see character.gd. Detected by property
	# presence rather than a type check, since obj here is the PropertyState wrapper, not the node.
	if "CurrentPose" in obj:
		if duration != null:
			obj.Binding.FadeDuration = duration
		obj.CurrentPose = Character.get_pose(obj.Binding.BindName, image_path)
		return

	var path = c.resource_root
	if obj.has_meta("folder"):
		path += obj.get_meta("folder") + "/"
	path += image_path + ".png"

	obj.texture = load(path)
	obj.visible = true

#@export
static func hide(obj) -> void:
	obj = _get_obj(obj)
	if "CurrentPose" in obj:
		obj.CurrentPose = ""
		return
	obj.visible = false

# shader_layer is either a bare shader name (single-slot target, or layer 0 on "cam") or a
# [shader_name, layer_id] pair (only meaningful for "cam" - see VfxManager.cs for why bg/fg/character
# targets only ever have one material slot). shader_name=null clears that target/layer, reverting to
# the plain unfiltered look. shader_name resolves to resources/shaders/<shader_name>.gdshader.
# properties are applied once as static uniform values (no per-property animation - see
# porting-guide.md M8 decision log, vfx_multi isn't ported).
#@export
static func vfx(obj: Variant, shader_layer: Variant, t=1.0, duration=null, properties=null,
		entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var shader_name = shader_layer[0] if shader_layer is Array else shader_layer
	var layer_id = shader_layer[1] if shader_layer is Array else 0

	var state
	if obj == "cam":
		if shader_name == null:
			o.vfx.ClearLayer(layer_id)
			return entry
		state = o.vfx.GetLayerState(layer_id, shader_name)
	else:
		obj = _get_obj(obj)
		if shader_name == null:
			o.vfx.ClearState(obj.Binding)
			return entry
		state = o.vfx.GetState(obj.Binding, shader_name)

	_apply_vfx_properties(state, properties)
	if duration != null and duration > 0:
		return entry.PropertyDouble(state, "shader_parameter/_T", t, duration, false)
	else:
		state.set("shader_parameter/_T", t)
		return entry

# immediate=true ALSO writes straight to the real ShaderMaterial (state.Binding), on top of the
# normal deferred state.set() - needed by trans()/trans2()'s setup steps, which run in the same
# script block as a show()/hide() texture swap that is itself deferred (see PropertyState.cs):
# VfxManager.GetState()'s own _MainTex auto-capture reads the live node's texture synchronously,
# before that swap has reached the real node, so it captures the stale pre-swap texture. Deferring
# this function's own writes on top of that would leave a window where the shader material is
# already attached (Apply() in VfxManager.cs is itself unconditional/immediate) but its uniforms
# still hold stale leftover/default values - the immediate write closes that window. Crucially this
# must NOT *replace* state.set() (an earlier version did): VfxManager caches one ShaderMaterial per
# (target, shader) pair and reuses it across calls (e.g. trans_fade then trans_left on the same bg),
# so PropertyState's own _properties cache for "shader_parameter/_T" survives from the previous
# trans() call too - if the immediate write bypassed _Set() entirely, the NEXT trans() call's
# PropertyAnimation.Init() would read that stale cached value back out instead of the fresh one,
# making it animate from a wrong (often equal-to-target, i.e. invisible) starting point. Writing
# both keeps the real material correct *right now* and keeps PropertyState's cache correct for
# whoever reads it next - including this same function the next time it's reused.
static func _apply_vfx_properties(state, properties, immediate=false) -> void:
	if properties == null:
		return
	for key in properties:
		var value = properties[key]
		# Texture-typed uniforms (_SubTex, _Mask, ...) are passed as plain resource-root-relative
		# paths, like show()'s image_path - everything else (float/Color/Vector uniforms) is
		# passed as an already-typed GDScript value, so a plain heuristic on the value's own type
		# is enough; Nova1 instead looks up each shader's per-property type from
		# ShaderInfoDatabase, which this port doesn't carry over (see porting-guide.md M8).
		if value is String:
			value = load(c.resource_root + value + ".png")
		if immediate:
			state.Binding.set_shader_parameter(key, value)
		state.set("shader_parameter/" + key, value)

# Ports Nova1's transition.lua `trans` - a crossfade between whatever obj is currently showing and
# image_name_or_func's result, using a fade.gdshader-family shader (_MainTex=new, _SubTex=old,
# _T 1->0 reveals new). image_name_or_func is either a plain image path (obj swaps directly, like
# show()) or a Callable run while the old frame is still frozen as _SubTex (obj == "cam" - lets the
# callback do several instant show()/hide() calls, e.g. a full scene change, before the crossfade
# reveals the result). Captures the pre-swap texture as _SubTex and the post-swap texture as
# _MainTex explicitly (rather than relying on VfxManager.GetState's own auto-_MainTex capture) since
# that capture reads the live node, which is still showing the OLD texture at this point - the swap
# above only reaches the live node later, at Sync() time (see PropertyState.cs).
#@export
static func trans(obj: Variant, image_name_or_func: Variant, shader_name: String, duration=1.0,
		properties=null, color2=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	var props = {} if properties == null else properties.duplicate()
	if color2 != null:
		props["_SubColor"] = color2

	var state
	var clear_callback: Callable
	if obj == "cam":
		var old_texture = o.vfx.CaptureScreen()
		if image_name_or_func is Callable:
			image_name_or_func.call()
		else:
			show(obj, image_name_or_func)
		props["_SubTex"] = old_texture
		# "<shader>_screen" instead of the bare shader_name: the cam VfxLayer stack is a
		# canvas_item ColorRect, and _MainTex there has to be hint_screen_texture (auto-samples
		# the post-swap live scene) rather than fade.gdshader's plain source_color _MainTex - see
		# fade_screen.gdshader. There is no static "new frame" texture to assign explicitly here:
		# image_name_or_func's swap above hasn't actually rendered a new frame yet.
		state = o.vfx.GetLayerState(0, shader_name + "_screen")
		clear_callback = func(): o.vfx.ClearLayer(0)
	else:
		var node = _get_obj(obj)
		var old_texture = node.texture
		show(obj, image_name_or_func)
		props["_SubTex"] = old_texture if old_texture != null else node.texture
		props["_MainTex"] = node.texture
		state = o.vfx.GetState(node.Binding, shader_name)
		clear_callback = func(): o.vfx.ClearState(node.Binding)

	# _T set to its starting value (1.0, fully hiding _MainTex behind _SubTex) BEFORE _apply_vfx_properties
	# writes _MainTex/_SubTex below - not after. _T's real value going into this call is whatever the
	# *previous* trans()-family call on this same cached material left it at (0.0, fully revealing
	# _MainTex, if that previous transition ran to completion) - if _MainTex got overwritten to *this*
	# call's new/target image first, there's a real window where the material immediately reads
	# _T=0.0 + _MainTex=new-target-image at once, i.e. the shader briefly shows the fully-resolved
	# target image instead of the old one - this is RenderingServer applying these two immediate
	# set_shader_parameter calls as separate commands, not necessarily atomically within one frame.
	# Flipping the order closes that window: _T is already hiding _MainTex by the time _MainTex changes.
	state.Binding.set_shader_parameter("_T", 1.0)
	state.set("shader_parameter/_T", 1.0)
	_apply_vfx_properties(state, props, true)
	entry = entry.PropertyDouble(state, "shader_parameter/_T", 0.0, duration, false)
	# clear_callback used to ride along as entry.Action(clear_callback), queued onto the C# animation
	# tree alongside the PropertyDouble step above. That silently never fired: a GDScript Callable
	# passed as a parameter into a C# method arrives empty (Method/Target both blank - confirmed by
	# inspecting AnimationEntry.Action's received Callable directly), a Godot 4.6.3 Mono interop gap,
	# not a logic bug in AnimationExecutor. A SceneTreeTimer sidesteps it entirely - the connect() call
	# is GDScript-to-GDScript, never crossing into C#, and fires on a real-time delay independent of
	# whether the click stream advances past this transition before or after it naturally finishes
	# (the entire point: it must run either way, see AnimationExecutor.Stop's RunPendingActions comment).
	#
	# During a restore replay (MoveTo/LoadGame replays every eager block synchronously, no real time
	# elapsing in between - same reasoning as dialogue_box.gd's box_hide_show), that real-time delay
	# would instead fire `duration` real seconds *after* the replay returns, i.e. once the player is
	# already back in live gameplay possibly several dialogue steps further along - clobbering whatever
	# shares this same vfx layer/material slot by then (e.g. a later vfx() call reusing cam layer 0).
	# Run the cleanup immediately instead, matching restore's existing "skip the animated dance, land on
	# the final state" approximation everywhere else.
	if _nova.IsRestoring:
		clear_callback.call()
	else:
		(Engine.get_main_loop() as SceneTree).create_timer(duration).timeout.connect(clear_callback)
	return entry

#@export
static func trans_fade(obj: Variant, image_name_or_func: Variant, duration=1.0, entry=null) -> Variant:
	return trans(obj, image_name_or_func, "fade", duration,
		{ "_Mask": "masks/gray", "_Vague": 0.5, "_InvertMask": 0.0 }, null, entry)

# _Vague/_InvertMask are spelled out explicitly on every trans_left/right/up/down call (even ones
# matching fade.gdshader's own declared defaults) rather than left to whatever the shader's default
# is - VfxManager caches one ShaderMaterial per (target, shader) pair and reuses it across calls
# (see VfxManager.cs), so e.g. trans_fade's _Vague=0.5 would otherwise leak into a later trans_left
# on the same target and silently flatten its wipe shape back into a uniform fade.
#@export
static func trans_left(obj: Variant, image_name_or_func: Variant, duration=1.0, entry=null) -> Variant:
	return trans(obj, image_name_or_func, "fade", duration,
		{ "_Mask": "masks/wipe_left", "_Vague": 0.25, "_InvertMask": 0.0 }, null, entry)

#@export
static func trans_right(obj: Variant, image_name_or_func: Variant, duration=1.0, entry=null) -> Variant:
	return trans(obj, image_name_or_func, "fade", duration,
		{ "_Mask": "masks/wipe_left", "_Vague": 0.25, "_InvertMask": 1.0 }, null, entry)

#@export
static func trans_up(obj: Variant, image_name_or_func: Variant, duration=1.0, entry=null) -> Variant:
	return trans(obj, image_name_or_func, "fade", duration,
		{ "_Mask": "masks/wipe_up", "_Vague": 0.25, "_InvertMask": 0.0 }, null, entry)

#@export
static func trans_down(obj: Variant, image_name_or_func: Variant, duration=1.0, entry=null) -> Variant:
	return trans(obj, image_name_or_func, "fade", duration,
		{ "_Mask": "masks/wipe_up", "_Vague": 0.25, "_InvertMask": 1.0 }, null, entry)

# Ports Nova1's transition.lua `trans2` - generic "ease an existing vfx shader's _T up, swap content
# while fully obscured, ease back down" sequencer (distinct from trans()'s crossfade: trans2 reuses
# whatever vfx()-style shader is passed - mono/color/radial_blur/... - rather than always the
# fade.gdshader family, since the use case is a punch-style hide/reveal, not a literal crossfade).
# shader_layer/properties/properties2 follow vfx()'s own conventions exactly (bare name or
# [name, layer_id] pair, raw shader_parameter values). image_name_or_func may be null (trans2 used
# purely as a vfx punch, e.g. ch4.txt's radial_blur reveal with no image swap at all).
#@export
static func trans2(obj: Variant, image_name_or_func: Variant, shader_layer: Variant, duration=1.0,
		properties=null, duration2=null, properties2=null, color2=null, entry=null) -> Variant:
	entry = entry if entry != null else o.anim
	duration2 = duration2 if duration2 != null else duration

	var shader_name = shader_layer[0] if shader_layer is Array else shader_layer
	var layer_id = shader_layer[1] if shader_layer is Array else 0

	var state
	var clear_callback: Callable
	if obj == "cam":
		state = o.vfx.GetLayerState(layer_id, shader_name)
		clear_callback = func(): o.vfx.ClearLayer(layer_id)
	else:
		var node = _get_obj(obj)
		state = o.vfx.GetState(node.Binding, shader_name)
		clear_callback = func(): o.vfx.ClearState(node.Binding)

	# _T set before _apply_vfx_properties, not after - see trans()'s matching comment for why: closes
	# the window where a reused cached material's leftover _T from a *previous* call could briefly
	# pair with *this* call's freshly-written properties before _T itself catches up.
	state.Binding.set_shader_parameter("_T", 0.0)
	state.set("shader_parameter/_T", 0.0)
	_apply_vfx_properties(state, properties, true)

	var middle_callback = func():
		state.Binding.set_shader_parameter("_T", 1.0)
		state.set("shader_parameter/_T", 1.0)
		if image_name_or_func != null:
			if image_name_or_func is Callable:
				image_name_or_func.call()
			else:
				show(obj, image_name_or_func, null, color2)
		_apply_vfx_properties(state, properties2, true)

	entry = entry.PropertyDouble(state, "shader_parameter/_T", 1.0, duration, false)
	entry = entry.PropertyDouble(state, "shader_parameter/_T", 0.0, duration2, false)
	# See trans()'s matching comment: entry.Action(callback) silently never fires (a GDScript Callable
	# arrives empty once passed through a C# method parameter, a Godot 4.6.3 Mono interop gap) - these
	# two callbacks ride on SceneTreeTimers instead, scheduled by real elapsed time from now. Same
	# restore guard as trans() too: a replay shouldn't leave either callback dangling on a real-time
	# delay that fires after the player has already moved further into live gameplay.
	if _nova.IsRestoring:
		middle_callback.call()
		clear_callback.call()
	else:
		var tree = Engine.get_main_loop() as SceneTree
		tree.create_timer(duration).timeout.connect(middle_callback)
		tree.create_timer(duration + duration2).timeout.connect(clear_callback)
	return entry
