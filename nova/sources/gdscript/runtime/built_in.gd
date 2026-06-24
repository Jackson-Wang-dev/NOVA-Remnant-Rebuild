class_name BuiltIn

static var _nova: Node

static func _static_init():
	var tree: SceneTree = Engine.get_main_loop()
	_nova = tree.root.get_node("NovaController")
	print("Script Init", _nova.ObjectManager)

#@export
static var o: Dictionary:
	get:
		return _nova.ObjectManager.Objects

#@export
static var c: Dictionary:
	get:
		return _nova.ObjectManager.Constants

static func _get_obj(obj: Variant) -> Variant:
	if obj is String:
		return o[obj]
	return obj

static func _get_index(arr: Array, index: int, default=null) -> Variant:
	return arr[index] if index < len(arr) and arr[index] != null else default

static func _get_vec3(input, default: Vector3, single_default=null) -> Vector3:
	if input is Vector3:
		return input
	elif (input is int or input is float) and single_default != null:
		return single_default.call(input)
	elif input != null:
		var x = _get_index(input, 0, default.x)
		var y = _get_index(input, 1, default.y)
		var z = _get_index(input, 2, default.z)
		return Vector3(x, y, z)
	else:
		return default

# Ports Nova1's animation_high_level.lua parse_color (non-vector branch only - nova2 has no Vector4
# color-animation variant). A bare number broadcasts to all three RGB channels with alpha defaulting
# to 1 - the idiom Colorless scripts use to reset a tint to white, e.g. `tint(gaotian, 1)` (tut02.txt/
# tut05.txt). A 2-element array is {gray, alpha}; 3 is {r, g, b} (alpha defaults to 1); 4 is {r, g, b, a}.
static func _parse_color(color) -> Color:
	if color is Color:
		return color
	if color is int or color is float:
		return Color(color, color, color, 1)
	match len(color):
		1:
			return Color(color[0], color[0], color[0], 1)
		2:
			return Color(color[0], color[0], color[0], color[1])
		3:
			return Color(color[0], color[1], color[2], 1)
		_:
			return Color(color[0], color[1], color[2], color[3])

#@export
static func pop_prefix(s: String, prefix: String, sep_len: int=0) -> Array:
	if s.begins_with(prefix):
		return [prefix, s.substr(prefix.length() + sep_len)]
	else:
		return [null, s]
