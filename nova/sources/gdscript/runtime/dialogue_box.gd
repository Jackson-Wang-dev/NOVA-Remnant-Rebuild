class_name DialogueBox extends BuiltIn

static var box_pos_presets: Dictionary = {
	bottom = {
		box = "default_box",
		anchor = [0.1, 0.9, 0.65, 0.95],
		style = "light",
	},
	top = {
		box = 'default_box',
		anchor = [0.1, 0.9, 0.05, 0.35],
		style = "light",
	},
	center = {
		box = 'default_box',
		anchor = [0.1, 0.9, 0.35, 0.65],
		style = "center",
	},
	left = {
		box = 'basic_box',
		anchor = [0, 0.5, 0, 1],
		style = "dark",
	},
	right = {
		box = 'basic_box',
		anchor = [0.5, 1, 0, 1],
		style = "dark",
	},
	full = {
		box = 'basic_box',
		anchor = [0, 1, 0, 1],
		style = "dark_center",
	},
	hide = {
		box = null,
	},
}

# index 0 = dark text (for light backgrounds), index 1 = white text (for dark/transparent
# backgrounds, paired with text_material="outline" for legibility)
static var box_text_color_palette: Array = [Color(0.05, 0.05, 0.05, 1), Color(1, 1, 1, 1)]

static var box_style_presets: Dictionary = {
	light = {
		tint = 1,
		alignment = "left",
		text_color = 0,
		text_material = '',
	},
	center = {
		tint = 1,
		alignment = "center",
		text_color = 0,
		text_material = '',
	},
	dark = {
		tint = [0, 0.5],
		alignment = "left",
		text_color = 1,
		text_material = "outline",
	},
	dark_center = {
		tint = [0, 0.5],
		alignment = "center",
		text_color = 1,
		text_material = "outline",
	},
	transparent = {
		tint = [0, 0],
		alignment = "left",
		text_color = 1,
		text_material = "outline",
	},
	subtitle = {
		tint = [0, 0],
		alignment = "center",
		text_color = 1,
		text_material = "outline",
	},
}

#@export
static func set_box(pos_name="bottom", style_name=null):
	var pos = box_pos_presets[pos_name];

	var box = o[pos.box] if pos.get("box") != null else null

	if box != null:
		var anchor = pos.get("anchor", [0, 1, 0, 1])
		box.anchor_left = anchor[0]
		box.anchor_right = anchor[1]
		box.anchor_top = anchor[2]
		box.anchor_bottom = anchor[3]

		var offset = pos.get("offset", [0, 0, 0, 0])
		box.offset_left = offset[0]
		box.offset_right = offset[1]
		box.offset_top = offset[2]
		box.offset_bottom = offset[3]

		var style = box_style_presets[style_name if style_name != null else pos.get("style", "light")]
		var tint = style.tint
		var gray = tint[0] if tint is Array else tint
		var alpha = tint[1] if tint is Array else 1
		box.BackgroundColor = Color(gray, gray, gray, alpha)
		box.Alignment = style.alignment
		box.TextColor = box_text_color_palette[style.text_color]
		box.Outline = style.text_material == "outline"

	_nova.GameViewController.SwitchDialogueBox(box, true)

# textappear: 0 = whole text appears instantly, 1 = whole text fades in, 2 = characters appear
# one by one with no fade, 3 = characters fade in one by one. char_speed (chars/sec) paces modes
# 2/3; fade_duration (seconds) is the whole-text fade in mode 1, or each character's own fade-in
# window in mode 3. Applies to entries appended to the current box from this point on.
#@export
static func set_text_appear(mode=0, char_speed=30.0, fade_duration=0.3):
	var box = _nova.GameViewController.CurrentDialogueBox
	if box != null:
		box.TextAppearMode = mode
		box.TextAppearCharSpeed = char_speed
		box.TextAppearFadeDuration = fade_duration

# Ports Nova1's dialogue_box.lua box_alignment - sets text alignment ("left"/"center"/"right") on
# whichever box is currently showing, independent of set_box's own per-preset default alignment.
#@export
static func box_alignment(mode="left") -> void:
	var box = _nova.GameViewController.CurrentDialogueBox
	if box != null:
		box.Alignment = mode

# Ports Nova1's dialogue_box.lua new_page - clears the current box's text content without switching
# box/position (set_box already calls this internally via GameViewController.Switch's cleanText).
#@export
static func new_page() -> void:
	var box = _nova.GameViewController.CurrentDialogueBox
	if box != null:
		box.NewPage()

# Ports Nova1's dialogue_box.lua text_delay - a one-shot pause before the *next* appended entry's
# text reveal animation starts (DialogueBoxController.TextAppearDelay, reset every dialogue step -
# see its declaration). Used by box_hide_show below to keep new text invisible until roughly when
# the box itself becomes visible again.
#@export
static func text_delay(time: float) -> void:
	var box = _nova.GameViewController.CurrentDialogueBox
	if box != null:
		box.TextAppearDelay = time

# Ports Nova1's dialogue_box.lua box_hide_show - hides the dialogue box (e.g. to give a cutscene full-
# screen room) and brings it back after duration. Hides via box.HidePanelImmediate() directly rather
# than set_box("hide", ...) (which switches CurrentDialogueBox to null) - Nova1's "hide" preset only
# moves the same box off-screen via anchors, it never stops being the active box, so dialogue text
# appended while "hidden" still lands on it (just invisible) and is there waiting once the box
# reappears. nova2's set_box("hide", ...) instead nulls CurrentDialogueBox, and
# GameViewController.OnDialogueChanged is `CurrentDialogueBox?.DisplayDialogue(...)` - any dialogue
# step whose text arrives while null is silently dropped, never buffered, so the box would later
# reappear empty instead of showing it. Going through HidePanelImmediate() keeps the same box as
# CurrentDialogueBox throughout, matching Nova1's behavior: GameViewController.Switch's
# "CurrentDialogueBox == box" branch later just re-shows it without clearing, so whatever got
# appended during the hidden window is still there.
#
# Uses a SceneTreeTimer rather than entry.Action(callback), same reason as graphics.gd's trans()/
# trans2(): a GDScript Callable passed into any C# method parameter (AnimationEntry.Action included)
# arrives empty on this project's pinned Godot 4.6.3 mono - see ActionAnimation.cs's caveat. A
# SceneTreeTimer connection is GDScript-to-GDScript and never crosses that boundary.
#
# Skips the hide-then-reveal-after-duration dance entirely during a restore replay (GameState.
# IsRestoring - MoveTo/LoadGame replay every eager block from a node's start, synchronously, with no
# real time elapsing in between) and just applies the final box state immediately. Otherwise the
# SceneTreeTimer scheduled here only fires duration *real* seconds after the replay call returns, by
# which point real-time gameplay has already resumed - so e.g. jumping via Backlog into any of
# ch1-4.txt (each opens with box_hide_show() as its very first action) would show no dialogue box at
# all for up to `duration` seconds after the jump, instead of landing directly in the dialogue box
# state the player clicked on.
#
# If CurrentDialogueBox is still null (no set_box() has ever run - e.g. this is ch1.txt's very first
# eager block, the opening line of a brand new game), there is no existing box to hide via
# HidePanelImmediate(): box_hide_show() used to just leave CurrentDialogueBox null for the whole
# `duration`, during which GameViewController.OnDialogueChanged's `CurrentDialogueBox?.DisplayDialogue
# (...)` silently dropped that first dialogue entry's text - the opening line never appeared, and the
# box itself only showed up empty once the deferred set_box() finally fired. Establishing the box up
# front via set_box() (then immediately hiding the same box) gives CurrentDialogueBox something real to
# buffer text on for the rest of this synchronous dialogue step, same as the already-non-null case
# below; the deferred set_box() call after `duration` then just reveals it without clearing (Switch's
# "CurrentDialogueBox == box" branch), so the opening line appears together with the box exactly like
# every other box_hide_show() use.
#@export
static func box_hide_show(duration=1.0, pos_name="bottom", style_name=null) -> void:
	if _nova.IsRestoring:
		set_box(pos_name, style_name)
		return
	var box = _nova.GameViewController.CurrentDialogueBox
	if box == null:
		set_box(pos_name, style_name)
		box = _nova.GameViewController.CurrentDialogueBox
	if box != null:
		box.HidePanelImmediate()
	var callback = func(): set_box(pos_name, style_name)
	(Engine.get_main_loop() as SceneTree).create_timer(duration).timeout.connect(callback)
	text_delay(duration)

# Dev-facing switch for Skip's default "only fast-forward already-read content" restriction (see
# AutoSkipController) - there's no player-facing settings toggle for this yet, just this script hook.
#@export
static func allow_skip_unread(value=true) -> void:
	_nova.GameViewController.AllowSkipUnread = value

# Forcibly cancels Auto/Skip from script, e.g. before a minigame or other section that shouldn't be
# auto-advanced through. Mirrors Nova1's stop_auto_ff().
#@export
static func stop_auto_skip() -> void:
	_nova.GameViewController.CancelAutoSkip()
