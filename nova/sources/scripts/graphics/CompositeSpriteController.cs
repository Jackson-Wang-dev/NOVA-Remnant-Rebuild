using Godot;

namespace Nova;

/// <summary>
/// Character standing sprite assembled from layered parts (e.g. "body+eye_normal+mouth_smile+hair"),
/// mirroring Nova1's CompositeSpriteController/pose.lua. Each pose is first rendered into its own
/// offscreen SubViewport and flattened to a single texture, then displayed/cross-faded as one Sprite3D
/// - mirroring why Nova1 merges into a RenderTexture before fading: fading each part's alpha
/// independently lets parts that should be fully opaque "see through" to whatever is layered beneath
/// them mid-fade (every transparent layer becomes partially see-through at the same time), which looks
/// like the layer order broke even though it didn't. Flattening first means there is only ever one
/// alpha value being animated per pose, so the cross-fade is a plain dissolve between two flat images.
/// </summary>
public partial class CompositeSpriteController : Node3D
{
    [Export]
    private string _bindName;
    [Export]
    private string _imageFolder;

    // Readable from GDScript so character.gd's pose table (keyed the same way as Nova1's pose.lua,
    // by the object's bound script name) can look itself up without needing a separate lookup field.
    public string BindName => _bindName;

    private string _currentPose = "";
    private Composite _activeComposite;
    private Godot.Collections.Dictionary _offsets = [];

    // Plain field, not PropertyState-bound: configures how the *next* CurrentPose change behaves,
    // same role as DialogueBoxController.TextAppearMode - an immediate behavioral knob, not a
    // tweened/restorable visual property in its own right (only CurrentPose itself is that).
    public double FadeDuration = 0.3;

    [Export]
    public string CurrentPose
    {
        get => _currentPose;
        set => ApplyPose(value);
    }

    private Color _modulate = Colors.White;
    private Color _environmentColor = Colors.White;

    // The composite is a plain Node3D (no native "modulate"), so tint()/o.anim.PropertyColor can't
    // target it directly the way they target a real Sprite3D like "fg"/"bg" - this forwards to
    // whichever Sprite3D is currently the live display, and keeps reapplying itself across pose
    // changes (each pose swap builds a brand new Sprite3D - see BuildComposite) so a tint set once
    // doesn't get lost the next time the character changes pose. graphics.gd's tint() special-cases
    // composite-bound objects to target this property instead of the native "modulate" by name.
    [Export]
    public Color Modulate
    {
        get => _modulate;
        set
        {
            _modulate = value;
            ApplyCombinedModulate();
        }
    }

    // Ports Nova1's GameCharacterController.environmentColor - a second, independently-driven tint
    // channel that multiplies with Modulate's RGB rather than replacing it (Nova1:
    // base.color = _color * _environmentColor), for ambient/lighting tints (dusk, night) that should
    // compose with tint()'s short-lived performance cues rather than fight over the same channel - see
    // graphics.gd's env_tint(). Alpha is deliberately left to Modulate alone (not multiplied in): this
    // controller's own fade tweens (ApplyPose's cross-fade) drive alpha directly via "modulate:a", and
    // no Colorless usage of env_tint ever supplies an alpha component anyway.
    [Export]
    public Color EnvironmentColor
    {
        get => _environmentColor;
        set
        {
            _environmentColor = value;
            ApplyCombinedModulate();
        }
    }

    private Color CombinedRgb => new(
        _modulate.R * _environmentColor.R,
        _modulate.G * _environmentColor.G,
        _modulate.B * _environmentColor.B);

    private void ApplyCombinedModulate()
    {
        if (_activeComposite == null)
        {
            return;
        }
        var current = _activeComposite.Display.Modulate;
        var rgb = CombinedRgb;
        _activeComposite.Display.Modulate = new Color(rgb.R, rgb.G, rgb.B, current.A);
    }

    // One merged pose: an offscreen viewport holding the (unscaled, native-position) layered parts,
    // and the flat Sprite3D under `this` that actually displays the merged result and gets cross-faded.
    private class Composite
    {
        public SubViewport Viewport;
        public Sprite3D Display;
    }

    public override void _EnterTree()
    {
        var state = new PropertyState(this)
        {
            InitProperties = ["CurrentPose", "Modulate", "EnvironmentColor"]
        };
        StateManager.Instance.BindPropertyState(_bindName, state);
    }

    public override void _Ready()
    {
        var path = $"{Assets.ResourceRoot}standings/{_imageFolder}/_offsets.json";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file != null)
        {
            _offsets = Json.ParseString(file.GetAsText()).AsGodotDictionary();
        }
        else
        {
            GD.PrintErr($"[CompositeSpriteController:{_bindName}] failed to open offsets file at {path}");
        }
        ApplyPose(_currentPose);
    }

    private void ApplyPose(string pose)
    {
        if (!IsNodeReady())
        {
            // PropertyState.InitProperties only reads the field's default value via the getter; _Ready
            // re-applies it once _offsets is loaded and this node can actually build the composite.
            _currentPose = pose;
            return;
        }
        if (pose == _currentPose && _activeComposite != null)
        {
            return;
        }

        var oldComposite = _activeComposite;
        _currentPose = pose;
        _activeComposite = BuildComposite(pose);

        var fade = FadeDuration > 0 && !GameState.Instance.IsRestoring && (oldComposite != null || _activeComposite != null);
        if (_activeComposite != null)
        {
            // The new Sprite3D always starts at the current tint's RGB (so a standing tint survives
            // a pose change instead of flashing back to white) - alpha starts at 0 only when about to
            // be cross-faded in, otherwise it's already the final target alpha with no tween needed.
            var rgb = CombinedRgb;
            _activeComposite.Display.Modulate = new Color(rgb.R, rgb.G, rgb.B, fade ? 0 : _modulate.A);
        }
        if (!fade)
        {
            FreeComposite(oldComposite);
            return;
        }

        var tween = CreateTween().SetParallel();
        if (_activeComposite != null)
        {
            tween.TweenProperty(_activeComposite.Display, "modulate:a", _modulate.A, FadeDuration);
        }
        if (oldComposite != null)
        {
            tween.TweenProperty(oldComposite.Display, "modulate:a", 0.0, FadeDuration);
        }
        tween.Finished += () => FreeComposite(oldComposite);
    }

    private static void FreeComposite(Composite composite)
    {
        if (composite == null)
        {
            return;
        }
        composite.Display.QueueFree();
        composite.Viewport.QueueFree();
    }

    private Composite BuildComposite(string pose)
    {
        if (string.IsNullOrEmpty(pose))
        {
            return null;
        }

        var result = PoseCompositor.Build(this, _imageFolder, pose, _offsets);
        var center = result.Bounds.GetCenter();
        var display = new Sprite3D
        {
            Texture = result.Texture,
            PixelSize = PoseCompositor.PixelSize,
            Position = new Vector3(center.X, center.Y, 0),
        };
        AddChild(display);

        return new Composite { Viewport = result.Viewport, Display = display };
    }
}
