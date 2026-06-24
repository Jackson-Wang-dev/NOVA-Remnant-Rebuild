class_name ConditionBlock extends RuntimeBlock

func __eval() -> bool:
	push_error("Must override __eval in child")
	return false

func run() -> bool:
	return __eval()

# See BaseBlock._get/_set for why this is duplicated here instead of living on RuntimeBlock.
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
