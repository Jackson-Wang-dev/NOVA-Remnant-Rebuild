class_name Character extends BuiltIn

# Ported verbatim from Nova1's pose.lua: character (bound name, e.g. "ergong") -> pose name ->
# "+"-joined part names. Composite character sprites are looked up here in show(); this table is
# orthogonal to AutoVoiceController's CharacterName keying (which uses the Chinese display/hidden
# name from dialogue text, e.g. "张浅野") - Nova1 already kept these as two separate namespaces
# (luaGlobalName for the bound sprite object vs. the in-text speaker name for voice), not something
# introduced by this port.
static var poses: Dictionary = {
	ergong = {
		normal = "body+mouth_smile+eye_normal+eyebrow_normal+hair",
	},
	gaotian = {
		normal = "body+mouth_smile+eye_normal+eyebrow_normal+hair",
		cry = "body+mouth_smile+eye_cry+eyebrow_normal+hair",
	},
	qianye = {
		normal = "body+mouth_close+eye_normal+eyebrow_normal+hair",
	},
	xiben = {
		normal = "body+mouth_close+eye_normal+eyebrow_normal+hair",
	},
}

# Mirrors Nova1's get_pose_by_name: pose_name already containing "+" is a literal pose string
# (used directly, no lookup); otherwise it's a named alias looked up in `poses`.
static func get_pose(character_name: String, pose_name: String) -> String:
	if pose_name.find("+") >= 0:
		return pose_name
	var character_poses = poses.get(character_name)
	if character_poses != null and character_poses.has(pose_name):
		return character_poses[pose_name]
	push_warning("Unknown pose '%s' for composite sprite '%s'" % [pose_name, character_name])
	return pose_name
