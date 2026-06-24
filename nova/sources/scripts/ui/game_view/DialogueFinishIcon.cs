using Godot;

namespace Nova;

/// <summary>
/// Simplified stand-in for Nova1's DialogueFinishIcon (a RenderTexture + Camera3D + spinning cube
/// rig): a plain 2D label that breathes (fades in and out) to hint "click to continue", instead of
/// porting that 3D rig for an affordance this minor.
/// </summary>
public partial class DialogueFinishIcon : Label
{
    private const double BreatheDuration = 1.0;

    public override void _Ready()
    {
        var tween = CreateTween().SetLoops();
        tween.TweenProperty(this, "modulate:a", 0.3, BreatheDuration);
        tween.TweenProperty(this, "modulate:a", 1.0, BreatheDuration);
    }
}
