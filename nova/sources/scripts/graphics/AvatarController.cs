using System.Collections.Generic;
using Godot;

namespace Nova;

/// <summary>
/// Headshot avatar shown next to dialogue text, mirroring Nova1's AvatarController/AvatarConfigs.
/// Deliberately decoupled from the on-screen CompositeSpriteController for the same character: this
/// tracks whoever is currently *speaking* (GameState.CurrentCharacterName) and whatever pose was last
/// staged for them via avatar.gd's avatar(), independent of whether/what pose their full body happens
/// to be showing on stage - same as Nova1, where avatar() can show a character who isn't on screen.
///
/// Crops a sub-rect of the merged pose texture (PoseCompositor, shared with CompositeSpriteController)
/// via Godot's AtlasTexture instead of Nova1's second render camera - AtlasTexture natively displays a
/// cropped region of any texture, no extra camera/viewport needed just for the crop.
///
/// One instance drives every dialogue box's avatar slot (see DialogueBoxController.AvatarSlot) rather
/// than one instance per box like Nova1: there's no per-box avatar state to keep in sync this way (so
/// Nova1's swap_last_avatar_name carry-over around box switches isn't needed - see porting-guide.md).
/// </summary>
public partial class AvatarController : Node, IStateObject
{
    // BindName (lowercase) is the key into character.gd's Character.poses table; ImageFolder
    // (capitalized) is the on-disk standings/ subfolder - same split as CompositeSpriteController's
    // _bindName/_imageFolder. Conflating these into one string was a bug: Character.get_pose() needs
    // the lowercase form to find the pose table at all (silently falls back to treating the raw pose
    // alias as a literal, nonexistent part filename otherwise), while PoseCompositor.Build() needs the
    // capitalized form to find the actual PNG files.
    private record Config(string BindName, string ImageFolder, (string Prefix, Rect2 Rect)[] Rects);

    // Ported from Main.unity's AvatarConfigs component - the only place in Nova1 that links a
    // speaker's Chinese display/hidden name to their composite sprite's pinyin bind name + image
    // folder (see porting-guide.md M5 decision on the two otherwise-unrelated namespaces). Each
    // rect is in pixels, relative to the top-left of that pose's own tight bounding box (matches how
    // the configured numbers were authored against Nova1's per-pose CompositeSpriteMerger.GetMergedSize).
    private static readonly Dictionary<string, Config> Configs = new()
    {
        ["王二宫"] = new Config("ergong", "Ergong", [("", new Rect2(0, 0, 800, 800))]),
        ["张浅野"] = new Config("qianye", "Qianye", [("", new Rect2(50, 0, 800, 800))]),
        ["孙西本"] = new Config("xiben", "Xiben", [("", new Rect2(0, 0, 800, 800))]),
        ["陈高天"] = new Config("gaotian", "Gaotian", [("", new Rect2(0, 0, 800, 800))]),
    };

    // Sticky per-speaker staged pose/tint, mirroring Nova1's AvatarController.characterToPose - set by
    // avatar(), cleared by avatar_hide()/avatar_clear(), and persists across dialogue entries until
    // explicitly changed (showing again whenever that character speaks again).
    private readonly Dictionary<string, string> _characterToPose = [];
    private readonly Dictionary<string, Color> _tint = [];
    private readonly Dictionary<string, Godot.Collections.Dictionary> _offsetsCache = [];

    private SubViewport _activeViewport;

    public double FadeDuration = 0.3;

    public override void _EnterTree()
    {
        StateManager.Instance.RegisterState("avatar", this);
        GameState.Instance.DialogueChanged.Subscribe(_ => Refresh());
    }

    /// <summary>Resolves the current speaker's bind name, for avatar()'s pose-alias lookup
    /// (Character.get_pose) in GDScript - kept there rather than here so pose-alias resolution stays
    /// in one place (graphics.gd's show()/hide() already does the same lookup for on-screen bodies).</summary>
    public string GetCurrentBindName()
    {
        var name = GameState.Instance.CurrentCharacterName;
        return name != null && Configs.TryGetValue(name, out var config) ? config.BindName : null;
    }

    public void SetPose(string pose, Color tint)
    {
        var name = GameState.Instance.CurrentCharacterName;
        if (string.IsNullOrEmpty(name) || !Configs.ContainsKey(name))
        {
            Utils.Warn($"[AvatarController] avatar() called with unknown/empty current speaker '{name}'.");
            return;
        }
        _characterToPose[name] = pose;
        _tint[name] = tint;
        Refresh();
    }

    public void HideCharacter(string name)
    {
        name = string.IsNullOrEmpty(name) ? GameState.Instance.CurrentCharacterName : name;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }
        _characterToPose.Remove(name);
        _tint.Remove(name);
        Refresh();
    }

    public void ClearAll()
    {
        _characterToPose.Clear();
        _tint.Clear();
        Refresh();
    }

    /// <summary>Shows whatever pose is staged for the current speaker, or hides if none. Called both
    /// right after a SetPose/Hide/Clear call (immediate feedback) and on every GameState.DialogueChanged
    /// (mirrors Nova1's add_action_after_lazy_block hook: a turn can switch to a speaker who already
    /// has a staged pose from an earlier entry, without that entry itself calling avatar() again).</summary>
    private void Refresh()
    {
        var name = GameState.Instance.CurrentCharacterName;
        AtlasTexture atlas = null;
        var tint = Colors.White;
        SubViewport newViewport = null;

        if (!string.IsNullOrEmpty(name) && _characterToPose.TryGetValue(name, out var pose) && !string.IsNullOrEmpty(pose))
        {
            var config = Configs[name];
            var offsets = GetOffsets(config.ImageFolder);
            var result = PoseCompositor.Build(this, config.ImageFolder, pose, offsets);
            newViewport = result.Viewport;
            atlas = new AtlasTexture { Atlas = result.Texture, Region = ResolveRect(config, pose, result.MarginPixels) };
            tint = _tint.GetValueOrDefault(name, Colors.White);
        }

        var oldViewport = _activeViewport;
        _activeViewport = newViewport;

        var slots = GetSlots();
        var fade = FadeDuration > 0 && !GameState.Instance.IsRestoring;
        if (!fade)
        {
            oldViewport?.QueueFree();
            foreach (var slot in slots)
            {
                slot.Texture = atlas;
                slot.Modulate = tint;
                slot.Visible = atlas != null;
            }
            return;
        }

        // Single fade-out-then-swap-then-fade-in rather than a true cross-dissolve (which is what
        // CompositeSpriteController does for on-screen bodies): each avatar slot is one TextureRect,
        // not a stack of layers that can hold an old and a new image at once, so there's nothing to
        // cross-fade between - simplification noted in porting-guide.md.
        var half = FadeDuration / 2;
        var tween = CreateTween();
        foreach (var slot in slots)
        {
            tween.Parallel().TweenProperty(slot, "modulate:a", 0.0, half);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            oldViewport?.QueueFree();
            foreach (var slot in slots)
            {
                slot.Texture = atlas;
                slot.Visible = atlas != null;
            }
        }));
        foreach (var slot in slots)
        {
            tween.Parallel().TweenProperty(slot, "modulate", tint, half);
        }
    }

    private static Rect2 ResolveRect(Config config, string pose, Vector2 marginPixels)
    {
        foreach (var (prefix, rect) in config.Rects)
        {
            if (pose.StartsWith(prefix))
            {
                return new Rect2(rect.Position + marginPixels, rect.Size);
            }
        }
        Utils.Warn($"[AvatarController] no configured crop rect matches pose '{pose}' for {config.ImageFolder}.");
        return new Rect2(marginPixels, Vector2.Zero);
    }

    private Godot.Collections.Dictionary GetOffsets(string imageFolder)
    {
        if (_offsetsCache.TryGetValue(imageFolder, out var cached))
        {
            return cached;
        }
        var path = $"{Assets.ResourceRoot}standings/{imageFolder}/_offsets.json";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        Godot.Collections.Dictionary offsets;
        if (file != null)
        {
            offsets = Json.ParseString(file.GetAsText()).AsGodotDictionary();
        }
        else
        {
            GD.PrintErr($"[AvatarController] failed to open offsets file at {path}");
            offsets = [];
        }
        _offsetsCache[imageFolder] = offsets;
        return offsets;
    }

    private static List<TextureRect> GetSlots()
    {
        var slots = new List<TextureRect>();
        foreach (var name in new[] { "default_box", "basic_box" })
        {
            if (ObjectManager.Instance.Objects.TryGetValue(name, out var obj) &&
                obj is PropertyState state &&
                state.Binding is DialogueBoxController box &&
                box.AvatarSlot != null)
            {
                slots.Add(box.AvatarSlot);
            }
        }
        return slots;
    }

    public void Sync() { }
    public void SyncImmediate() { }
    public void SyncBackend() { }

    public void ResetToBaseline()
    {
        _characterToPose.Clear();
        _tint.Clear();
        _activeViewport?.QueueFree();
        _activeViewport = null;
        foreach (var slot in GetSlots())
        {
            slot.Texture = null;
            slot.Visible = false;
        }
    }
}
