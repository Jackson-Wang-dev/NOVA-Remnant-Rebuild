using System;
using Godot;

namespace Nova;

/// <summary>
/// Generic modal confirm/notice popup, the first piece of an Alert-equivalent in nova2. A single
/// instance lives in game.tscn's Canvas (drawn above everything, including ButtonRing) and is reached
/// via the static Instance, mirroring Nova1's static Alert.Show - but unlike Alert, there is no
/// ignoreKey/checkbox support yet (nothing in nova2 persists "don't show again" flags), and no queue
/// (a second Show while one is open just replaces the pending callbacks).
/// </summary>
public partial class ConfirmDialog : PanelController
{
    [Export]
    private Label _message;
    [Export]
    private Button _okButton;
    [Export]
    private Button _cancelButton;

    private Action _onConfirm;
    private Action _onCancel;

    public static ConfirmDialog Instance { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        // Not driven by I18nText: that script attaches to the node and replaces its C# wrapper type,
        // which breaks the [Export] Button NodePath binding above (Button and I18nText are unrelated
        // siblings under Control, so the cast throws) - see porting-guide.md decision log.
        _okButton.Text = I18n.__("alert.confirm");
        _cancelButton.Text = I18n.__("alert.cancel");
        _okButton.Pressed += OnOkPressed;
        _cancelButton.Pressed += OnCancelPressed;
    }

    public override void _ExitTree()
    {
        Instance = null;
        _okButton.Pressed -= OnOkPressed;
        _cancelButton.Pressed -= OnCancelPressed;
    }

    public void Show(string messageKey, Action onConfirm, Action onCancel = null, params object[] args)
    {
        _message.Text = I18n.__(messageKey, args);
        _onConfirm = onConfirm;
        _onCancel = onCancel;
        _cancelButton.Visible = true;
        this.ShowPanelImmediate();
    }

    public void ShowNotice(string messageKey, params object[] args)
    {
        _message.Text = I18n.__(messageKey, args);
        _onConfirm = null;
        _onCancel = null;
        _cancelButton.Visible = false;
        this.ShowPanelImmediate();
    }

    /// <summary>
    /// Shows text taken literally, not as an i18n key - the entry point NovaScript's alert() needs
    /// (see alert.gd), which only ever passes a runtime narrative string with no translation entry
    /// of its own. Doesn't forward to ShowNotice(text): that still routes through I18n.Translate,
    /// which falls back to returning the key verbatim when nothing matches (an intentional, silent
    /// fallback elsewhere) but also logs a "Missing translation for: ..." warning on every miss -
    /// harmless but guaranteed noise for every single alert() call, since the text is never meant to
    /// resolve as a key in the first place. Also not reachable from GDScript directly for an
    /// unrelated reason: ShowNotice/Show take "params object[] args", which Godot's C#-to-GDScript
    /// method binding can't marshal - see the porting-guide.md decision log for the full story.
    /// </summary>
    public void ShowNoticeText(string text)
    {
        _message.Text = text;
        _onConfirm = null;
        _onCancel = null;
        _cancelButton.Visible = false;
        this.ShowPanelImmediate();
    }

    private void OnOkPressed()
    {
        this.HidePanelImmediate();
        _onConfirm?.Invoke();
    }

    private void OnCancelPressed()
    {
        this.HidePanelImmediate();
        _onCancel?.Invoke();
    }
}
