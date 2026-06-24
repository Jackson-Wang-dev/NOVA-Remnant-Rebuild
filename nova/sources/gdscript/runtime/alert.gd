# Named AlertHelper rather than "Alert" to avoid any ambiguity with the C# Nova.Alert/AlertController
# naming Nova1 itself used for this feature (no actual collision in GDScript, just matching this
# codebase's established habit of giving runtime helper classes distinct names - see AnimHelper).
class_name AlertHelper extends BuiltIn

# Ports Nova1's alert.lua alert(text) - a blocking modal notice (ConfirmDialog.ShowNoticeText,
# OK-only, no callback) that also force-stops Auto/Skip so the player doesn't auto-advance past it
# unread. text is shown as-is, not looked up as an i18n key: I18n.Translate already falls back to
# returning the key itself verbatim when no translation entry matches (see I18n.cs), which is
# exactly the "show this literal runtime string" behavior alert()/notify() need for inline scenario
# text. Goes through ShowNoticeText rather than ShowNotice(messageKey, params object[] args) directly
# - see that method's doc comment for why the params overload isn't reachable from GDScript at all.
#@export
static func alert(text: String) -> void:
	DialogueBox.stop_auto_skip()
	_nova.ConfirmDialog.ShowNoticeText(text)

# Ports Nova1's alert.lua notify(text) - a non-blocking, auto-dismissing toast (NotifyToast), as
# opposed to alert()'s blocking modal. Never stops Auto/Skip: Nova1's notify() doesn't either.
#@export
static func notify(text: String) -> void:
	_nova.NotifyToast.Show(text)
