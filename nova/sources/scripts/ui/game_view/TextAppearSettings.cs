namespace Nova;

/// <summary>
/// How a dialogue entry's text should reveal itself. Mirrors the "textappear" modes settable from
/// NovaScript via DialogueBox.set_text_appear(): 0 = whole text appears instantly, 1 = whole text
/// fades in, 2 = characters appear one by one with no fade, 3 = characters fade in one by one.
/// </summary>
public readonly struct TextAppearSettings
{
    public int Mode { get; init; }
    /// <summary>Characters per second, used by modes 2 and 3.</summary>
    public float CharSpeed { get; init; }
    /// <summary>Fade duration in seconds: the whole-text fade in mode 1, or each character's own
    /// fade-in window in mode 3.</summary>
    public float FadeDuration { get; init; }
    /// <summary>One-shot pause before the reveal itself starts (mirrors Nova1's
    /// DialogueBoxController.textAnimationDelay / text_delay()) - e.g. box_hide_show() uses this to
    /// keep newly-appended text invisible until roughly when the box itself becomes visible again.
    /// </summary>
    public float Delay { get; init; }
}
