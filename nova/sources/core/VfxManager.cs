using System.Collections.Generic;
using Godot;

namespace Nova;

/// <summary>
/// Lazily creates the ShaderMaterial for a (target node, shader name) pair, assigns it to the node's
/// material_override (Sprite3D/other GeometryInstance3D) or material (CanvasItem) property - via the
/// generic string-keyed Godot property path (see Apply() below for why a typed C# property/cast
/// doesn't work here) - and wraps it in a PropertyState so vfx() can drive its shader_parameter/*
/// uniforms through o.anim.PropertyDouble exactly like any other animatable property (see
/// PropertyState.cs - it forwards Get/Set by string path too, no special-casing needed).
///
/// Two distinct paths, matching Nova1's transition.lua get_renderer_pp/set_mat split:
/// - Single-slot targets (bg/fg/character Sprite3D): GetState/ClearState below. Nova1's plain
///   SpriteController/GameCharacterController objects only ever have one material slot
///   (renderer.material) - layer_id must be 0 for these, see Nova1's set_mat() warning.
/// - "cam": Nova1's CameraController carries a PostProcessing component with a List&lt;Material&gt;
///   layers, Blit-chained in order (each layer's output feeds the next - see PostProcessing.cs
///   Blit()/SetLayer()/ClearLayer()) so e.g. ch4.txt's "失色症" reveal can have mono+color+colorless
///   active on layers 0/1/2 simultaneously. Godot's canvas_item hint_screen_texture is a native
///   equivalent: stacking the fixed VfxLayer0..VfxLayer3 ColorRects (see game_view.tscn, sitting above
///   "Game" and below "Text"/"GameUI" so dialogue UI is unaffected, exactly like Nova1's post-process
///   only touching the camera's color buffer before the UI Canvas overlay) lets each layer's shader read
///   "everything drawn so far" and replace it, chaining automatically in node order - no manual
///   RenderTexture ping-pong needed. An inactive layer is simply hidden (visible=false) rather than
///   given a passthrough material, which is cheaper than Nova1's "set to a no-op Default shader" and
///   visually identical. GetLayerState/ClearLayer below are this layer-aware path, used only for "cam".
///
/// One PropertyState per (target, shader) pair is kept alive for the lifetime of the session and tracked
/// by StateManager so its dirty values still get baseline-reset/replayed on load, the same as M5's
/// CurrentPose or M7's Volume - this is what lets VFX parameters participate in save/restore "for free"
/// without a Nova1-style RestorableMaterial snapshot mechanism (see porting-guide.md M8 decision log).
/// Re-activating a shader on a target reassigns its already-built material rather than rebuilding it, so
/// switching back and forth between effects on the same target doesn't lose whatever _T/parameter values
/// were last set on each.
/// </summary>
public partial class VfxManager : RefCounted, ISingleton
{
    private readonly Dictionary<(Node Target, string ShaderName), PropertyState> _cache = [];

    public void OnEnter()
    {
        ObjectManager.Instance.BindObject("vfx", this);
    }

    public void OnReady() { }

    public void OnExit() { }

    public PropertyState GetState(Node target, string shaderName)
    {
        var key = (target, shaderName);
        ShaderMaterial material;
        PropertyState state;
        if (_cache.TryGetValue(key, out state))
        {
            material = (ShaderMaterial)state.Binding;
            Apply(target, material);
        }
        else
        {
            material = new ShaderMaterial
            {
                Shader = GD.Load<Shader>($"{Assets.ResourceRoot}shaders/{shaderName}.gdshader")
            };
            Apply(target, material);

            state = new PropertyState(material);
            StateManager.Instance.TrackState(state);
            _cache[key] = state;
        }

        // Spatial shaders (Sprite3D targets) have no automatic "this node's texture" builtin the way
        // canvas_item shaders do, so _MainTex has to be forwarded explicitly - see fade.gdshader. Read
        // via the generic Get(StringName) path rather than `target is Sprite3D` - target's static C#
        // type here is whatever ObjectBinder-style script is attached to it (see Apply() below for why
        // that's never the node's own native class), so a typed pattern match would never match.
        var texture = target.Get("texture");
        if (texture.VariantType != Variant.Type.Nil)
        {
            material.SetShaderParameter("_MainTex", texture);
        }

        return state;
    }

    /// <summary>Removes the vfx material override, reverting the target to its plain (unfiltered)
    /// look. Mirrors Nova1's set_mat(obj, defaultMat, 0) for non-PostProcessing objects.</summary>
    public void ClearState(Node target)
    {
        Apply(target, null);
    }

    public PropertyState GetLayerState(int layerId, string shaderName)
    {
        var layer = LayerNode(layerId);
        layer.Set("visible", true);
        return GetState(layer, shaderName);
    }

    public void ClearLayer(int layerId)
    {
        LayerNode(layerId).Set("visible", false);
    }

    // Layer "visible" is mutated directly on the raw node (see ClearLayer/GetLayerState above), never
    // through PropertyState's _Set() - so it never enters _baseline and StateManager.ResetToBaseline()
    // can't restore it. A layer left visible=true by a trans()/trans2() whose SceneTreeTimer cleanup
    // (graphics.gd) hasn't fired yet (e.g. the player navigates away within that window) would
    // otherwise stay stuck showing its last captured screenshot into the next game session. Called
    // from GameState.StartGame() alongside ResetToBaseline() for exactly this reason.
    public void ClearAllLayers()
    {
        for (var i = 0; i < 4; i++)
        {
            ClearLayer(i);
        }
    }

    /// <summary>
    /// Freezes the current game viewport render into a static texture - used by trans/trans_fade's
    /// "cam" path to snapshot the screen before instantly applying state changes, then crossfade away
    /// from the snapshot to reveal the live (now-updated) result. Reuses the same capture
    /// GameViewController already uses for save-slot thumbnails (CaptureThumbnail), just wrapped into a
    /// Texture2D instead of saved to disk. Previously scoped out (porting-guide.md M8 decision log:
    /// "零正式剧本用例") - Colorless's ch1.txt anim:trans_fade(cam, ...) is the real use case that
    /// decision didn't anticipate.
    /// </summary>
    public ImageTexture CaptureScreen()
    {
        var image = NovaController.Instance.GameViewController.CaptureThumbnail();
        return ImageTexture.CreateFromImage(image);
    }

    private static Node LayerNode(int layerId)
    {
        return (Node)((PropertyState)ObjectManager.Instance.Objects[$"cam_vfx_{layerId}"]).Binding;
    }

    /// <summary>
    /// Sets whichever of material_override (Sprite3D/other GeometryInstance3D) or material (CanvasItem)
    /// actually exists on target, via the generic string-keyed Godot property path rather than a C#
    /// type check/cast. target's static C# type is the attached script's class (e.g. ObjectBinder,
    /// which only extends Node) regardless of the underlying native node's real engine type (Sprite3D,
    /// ColorRect, ...) - a script-attached node is never also statically castable to its native
    /// subtype's C# class, exactly the problem PropertyState.Get/Set route around the same way. Setting
    /// a nonexistent property through this path is a silent no-op (same as PropertyState relies on), so
    /// trying both names unconditionally is safe.
    /// </summary>
    private static void Apply(Node target, ShaderMaterial material)
    {
        target.Set("material_override", material);
        target.Set("material", material);
    }

    public static VfxManager Instance => NovaController.Instance.GetObj<VfxManager>();
}
