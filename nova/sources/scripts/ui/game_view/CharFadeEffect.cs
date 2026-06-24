using Godot;

namespace Nova;

/// <summary>
/// Drives textappear mode 3 ("逐字渐变显示"): fades each character in shortly after the typewriter
/// reveal (RichTextLabel.VisibleRatio, tweened by DialogueEntryController) passes it. Attached via
/// RichTextLabel.PushCustomfx (DialogueEntryController.UpdateText) rather than a BBCode tag: a
/// custom BBCode tag declared as a plain "bbcode" field didn't get recognized when set from C# (it
/// rendered as literal text), while PushCustomfx attaches the effect directly to the text that
/// follows and explicitly doesn't require any tag registration.
/// </summary>
public partial class CharFadeEffect : RichTextEffect
{
    public float CharsPerSecond = 30f;
    public float FadeDuration = 0.2f;
    public ulong RevealStartUsec;

    public override bool _ProcessCustomFX(CharFXTransform charFx)
    {
        var elapsedSec = (Time.GetTicksUsec() - RevealStartUsec) / 1_000_000.0;
        var revealTime = charFx.RelativeIndex / Mathf.Max(CharsPerSecond, 1f);
        var t = (elapsedSec - revealTime) / Mathf.Max(FadeDuration, 0.001f);
        var alpha = Mathf.Clamp((float)t, 0f, 1f);
        var color = charFx.Color;
        color.A *= alpha;
        charFx.Color = color;
        return true;
    }
}
