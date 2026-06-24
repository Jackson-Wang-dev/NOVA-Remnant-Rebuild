using System;
using Godot;

namespace Nova;

public partial class DialogueEntryController : Control
{
    [Export]
    private Label _nameText;
    [Export]
    private RichTextLabel _contentText;

    public DialogueDisplayData DisplayData { get; private set; }
    private Color _textColor;
    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            UpdateColor();
        }
    }

    // "left" / "center" / "right", matches box_style_presets.alignment in dialogue_box.gd
    private string _alignment = "left";
    public string Alignment
    {
        get => _alignment;
        set
        {
            _alignment = value;
            UpdateText();
        }
    }

    private bool _outline;
    public bool Outline
    {
        get => _outline;
        set
        {
            _outline = value;
            UpdateOutline();
        }
    }

    private TextAppearSettings _appear;
    private Tween _revealTween;
    private CharFadeEffect _charFadeEffect;

    public bool IsRevealing => _revealTween != null && _revealTween.IsValid() && _revealTween.IsRunning();

    /// <summary>Fires once, exactly when this entry's text finishes revealing (naturally or via
    /// CompleteReveal()) - reset at the start of every Init() so a pooled, reused entry doesn't keep
    /// stale subscribers from its previous life.</summary>
    public event Action RevealFinished;

    /// <summary>
    /// Jump straight to the fully-shown end state, skipping whatever's left of the reveal Tween.
    /// Mirrors Nova1's click-forces-text-to-finish behavior (GameViewController.Step calling
    /// NovaAnimation.StopAll(AnimationType.Text) before deciding whether the click advances the
    /// dialogue or just finishes the current text).
    /// </summary>
    public void CompleteReveal()
    {
        if (!IsRevealing)
        {
            return;
        }
        // Kill() aborts without emitting Tween.Finished, so the RevealFinished notification (e.g.
        // DialogueBoxController showing the "click to continue" icon) has to be fired manually here.
        _revealTween?.Kill();
        _revealTween = null;
        Modulate = new Color(Modulate, 1f);
        _contentText.VisibleRatio = 1f;
        RevealFinished?.Invoke();
    }

    public override void _EnterTree()
    {
        I18n.Instance.LocaleChanged.Subscribe(UpdateText);
    }

    public override void _Ready()
    {
        UpdateText();
        UpdateColor();
        UpdateOutline();
    }

    public override void _ExitTree()
    {
        I18n.Instance.LocaleChanged.Unsubscribe(UpdateText);
        _revealTween?.Kill();
        // clear references
        DisplayData = default;
        TextColor = default;
    }

    private void UpdateText()
    {
        if (!IsNodeReady())
        {
            return;
        }
        var name = I18n.__(DisplayData.DisplayNames);
        _nameText.Visible = !string.IsNullOrEmpty(name);
        _nameText.Text = name;
        _nameText.HorizontalAlignment = _alignment switch
        {
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };
        var dialogue = I18n.__(DisplayData.Dialogues);
        var alignment = _alignment switch
        {
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };

        if (_appear.Mode == 3)
        {
            // PushCustomfx attaches the effect directly to the text that follows and explicitly
            // doesn't need any tag registration - a BBCode tag declared via a "bbcode" field
            // didn't get recognized when set from C# (it rendered as literal "[charfade]" text).
            _charFadeEffect ??= new CharFadeEffect();
            // Plain Clear() only clears the tag stack, not the Text property itself - Godot's docs
            // warn the old Text content "will show again if the label is redrawn" in that case.
            // Setting Text to "" clears both, so a later redraw can't revert to stale content.
            _contentText.Text = "";
            _contentText.PushParagraph(alignment);
            _contentText.PushCustomfx(_charFadeEffect, new Godot.Collections.Dictionary());
            _contentText.AddText(dialogue);
            _contentText.PopAll();
        }
        else
        {
            _contentText.Text = alignment switch
            {
                HorizontalAlignment.Center => $"[center]{dialogue}[/center]",
                HorizontalAlignment.Right => $"[right]{dialogue}[/right]",
                _ => dialogue,
            };
        }
    }

    private void UpdateColor()
    {
        if (!IsNodeReady())
        {
            return;
        }
        _nameText.AddThemeColorOverride("font_color", TextColor);
        _contentText.AddThemeColorOverride("default_color", TextColor);
    }

    // Stands in for Nova1's per-entry font material (e.g. "outline"); nova2 has no material
    // system for fonts yet, so this maps the one material Nova1's box styles actually use
    // (an outline, for legibility over dark/transparent backgrounds) onto Godot's built-in
    // font outline theme properties instead of porting a material asset pipeline.
    private void UpdateOutline()
    {
        if (!IsNodeReady())
        {
            return;
        }
        var outlineSize = _outline ? 4 : 0;
        _nameText.AddThemeConstantOverride("outline_size", outlineSize);
        _nameText.AddThemeColorOverride("font_outline_color", Colors.Black);
        _contentText.AddThemeConstantOverride("outline_size", outlineSize);
        _contentText.AddThemeColorOverride("font_outline_color", Colors.Black);
    }

    // Nova1 skips text reveal animation during restore/fast-forward/jump (DialogueBoxController.
    // AppendDialogue's "needAnimation" gate); restore replay here would otherwise fire a fresh
    // reveal Tween for every entry it replays past, so mirror that and just snap to fully shown.
    // appear.Delay (DialogueBoxController.TextAppearDelay / dialogue_box.gd's text_delay()) is the one
    // exception that still needs a Tween even under Mode 0 - see the early-return condition below.
    private void StartReveal(TextAppearSettings appear)
    {
        _revealTween?.Kill();
        Modulate = new Color(Modulate, 1f);

        if (GameState.Instance.IsRestoring || (appear.Mode == 0 && appear.Delay <= 0f))
        {
            _revealTween = null;
            _contentText.VisibleRatio = 1f;
            return;
        }

        if (appear.Mode == 0)
        {
            Modulate = new Color(Modulate, 0f);
        }
        else
        {
            _contentText.VisibleRatio = 0f;
        }

        _revealTween = CreateTween();
        if (appear.Delay > 0f)
        {
            _revealTween.TweenInterval(appear.Delay);
        }
        AppendRevealTween(_revealTween, appear);
        _revealTween.Finished += () => RevealFinished?.Invoke();
    }

    private void AppendRevealTween(Tween tween, TextAppearSettings appear)
    {
        var totalChars = Mathf.Max(_contentText.GetTotalCharacterCount(), 1);
        var charSpeed = Mathf.Max(appear.CharSpeed, 1f);
        var fadeDuration = Mathf.Max(appear.FadeDuration, 0.01f);

        switch (appear.Mode)
        {
            case 0:
                tween.TweenProperty(this, "modulate:a", 1.0, 0.0);
                break;
            case 1:
                tween.TweenProperty(this, "modulate:a", 1.0, (double)fadeDuration);
                break;
            case 2:
                tween.TweenProperty(_contentText, "visible_ratio", 1.0, (double)(totalChars / charSpeed));
                break;
            case 3:
                _charFadeEffect.CharsPerSecond = charSpeed;
                _charFadeEffect.FadeDuration = fadeDuration;
                // Set right when the actual reveal is about to start, not when StartReveal was called -
                // otherwise a positive appear.Delay would bias CharFadeEffect's elapsed-time math by
                // that same delay, since the leading TweenInterval above hasn't run yet at that point.
                tween.TweenCallback(Callable.From(() => _charFadeEffect.RevealStartUsec = Time.GetTicksUsec()));
                tween.TweenProperty(_contentText, "visible_ratio", 1.0, (double)(totalChars / charSpeed));
                break;
        }
    }

    public void Init(DialogueDisplayData displayData, Color textColor, string alignment, bool outline,
        TextAppearSettings appear)
    {
        RevealFinished = null;
        DisplayData = displayData;
        _alignment = alignment;
        _outline = outline;
        _textColor = textColor;
        _appear = appear;
        UpdateText();
        UpdateColor();
        UpdateOutline();
        StartReveal(appear);
    }
}
