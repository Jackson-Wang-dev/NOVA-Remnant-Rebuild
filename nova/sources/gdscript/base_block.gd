class_name BaseBlock extends RuntimeBlock

func __eval() -> void:
	push_error("Must override __eval in child")

func run() -> void:
	__eval()

# Routes v_x/gv_x property access to the Variables singleton. Reads are compiled to self.get("v_x")
# (GDRuntime.RewriteVariables), not bare self.v_x - Object.get() safely returns null for an unset
# variable, whereas the bare dot-read syntax treats a null _get return as "property does not exist"
# and raises a runtime error instead. Writes (self.v_x = ...) go through _set below, which has no such
# ambiguity (its return is a plain bool, not the value itself). Defined here (and duplicated in
# ConditionBlock) rather than on RuntimeBlock, because RuntimeBlock is regenerated from scratch by
# addons/nova_macro on every build (see nova_macro.gd's _build()) and would silently drop any hand-added
# function.
func _get(property: StringName):
	var s := str(property)
	if s.begins_with("v_") or s.begins_with("gv_"):
		return o.variables.Get(s)
	return null

func _set(property: StringName, value) -> bool:
	var s := str(property)
	if s.begins_with("v_") or s.begins_with("gv_"):
		o.variables.Set(s, value)
		return true
	return false
