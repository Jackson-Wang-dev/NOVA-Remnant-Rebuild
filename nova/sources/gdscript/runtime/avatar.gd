class_name Avatar extends BuiltIn

# avatar(pose) is implicitly for "whoever is currently speaking" (no explicit character argument),
# mirroring Nova1's avatar.lua. Pose alias resolution stays here (same Character.get_pose lookup
# graphics.gd's show()/hide() already does for on-screen bodies) - o.avatar (AvatarController) only
# ever deals in already-resolved "+"-joined pose strings, same division of labor as CompositeSpriteController.
#@export
static func avatar(pose, color=null) -> void:
	var bind_name = o.avatar.GetCurrentBindName()
	if bind_name == null:
		push_warning("avatar(): current speaker has no avatar config")
		return

	var tint = Color(1, 1, 1, 1)
	if color != null:
		if color is Color:
			tint = color
		else:
			var a = _get_index(color, 3, 1)
			tint = Color(color[0], color[1], color[2], a)

	o.avatar.SetPose(Character.get_pose(bind_name, pose), tint)

#@export
static func avatar_hide(name=null) -> void:
	o.avatar.HideCharacter(name)

#@export
static func avatar_clear() -> void:
	o.avatar.ClearAll()
