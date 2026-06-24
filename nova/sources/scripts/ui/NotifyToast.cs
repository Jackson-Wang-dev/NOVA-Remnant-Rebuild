using Godot;

namespace Nova;

/// <summary>
/// Non-blocking, auto-dismissing toast - the second half of an Alert-equivalent in nova2, mirroring
/// Nova1's Alert.Show(content, onFinish) "simple notification which will fade out itself" overload
/// (ConfirmDialog already covers the modal/blocking half - see its doc comment). Never intercepts
/// clicks (mouse_filter Ignore in the .tscn) and has no queue: a second Show() while one is still
/// fading just kills and restarts the same Tween with the new text, the same simplification
/// ConfirmDialog already makes for its own "second Show replaces the pending one" case.
/// </summary>
public partial class NotifyToast : Control
{
    [Export]
    private Label _message;

    private Tween _tween;

    public static NotifyToast Instance { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        Instance = null;
    }

    public void Show(string text)
    {
        _tween?.Kill();
        _message.Text = text;
        Visible = true;
        Modulate = new Color(1, 1, 1, 0);
        _tween = CreateTween();
        _tween.TweenProperty(this, "modulate:a", 1.0, 0.2);
        _tween.TweenInterval(2.0);
        _tween.TweenProperty(this, "modulate:a", 0.0, 0.3);
        _tween.TweenCallback(Callable.From(() => Visible = false));
    }
}
