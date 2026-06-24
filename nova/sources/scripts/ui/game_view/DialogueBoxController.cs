using System;
using Godot;

namespace Nova;

public partial class DialogueBoxController : PanelController
{
    public enum Mode
    {
        Overwrite,
        Append
    }

    [Export]
    private string _bindName;
    [Export]
    private Mode _mode;
    [Export]
    private Control _background;
    [Export]
    private DialogueTextController _textController;
    [Export]
    private Button _closeButton;
    [Export]
    private Control _finishIcon;
    [Export]
    private TextureRect _avatar;

    // Read by AvatarController, which drives every dialogue box's slot in lockstep (see its doc
    // comment) - this box doesn't own or update its own avatar state.
    public TextureRect AvatarSlot => _avatar;

    private float _opacity;
    private Color _backgroundColor;
    private Color _textColor;
    private string _alignment = "left";
    private bool _outline;

    [Export]
    public float Opacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            UpdateBackgroundColor();
            UpdateTextColor();
        }
    }
    [Export]
    public Color BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            _backgroundColor = value;
            UpdateBackgroundColor();
        }
    }
    [Export]
    public Color TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            UpdateTextColor();
        }
    }
    [Export]
    public string Alignment
    {
        get => _alignment;
        set
        {
            _alignment = value;
            _textController?.UpdateAlignment(value);
        }
    }
    [Export]
    public bool Outline
    {
        get => _outline;
        set
        {
            _outline = value;
            _textController?.UpdateOutline(value);
        }
    }

    private bool _closeButtonShown = true;
    public bool CloseButtonShown
    {
        get => _closeButtonShown;
        set
        {
            _closeButtonShown = value;
            if (_closeButton != null)
            {
                _closeButton.Visible = value;
            }
        }
    }

    // Configured per-box from NovaScript via DialogueBox.set_text_appear(); applies to entries
    // appended from this point on, not retroactively to ones already shown. Not PropertyState-bound
    // like Opacity/BackgroundColor/etc.: it's immediate behavioral config consumed once per new
    // entry, not a tweened visual property that needs restore/Sync semantics.
    public int TextAppearMode { get; set; }
    public float TextAppearCharSpeed { get; set; } = 30f;
    public float TextAppearFadeDuration { get; set; } = 0.3f;

    // Unlike the three above, this is a one-shot: consumed by the very next AppendDialogue and reset
    // on every OnDialogueWillChange (mirrors Nova1's textAnimationDelay/ResetTextAnimationConfig) -
    // a leftover positive delay must never silently carry over and stall later, unrelated dialogue.
    public float TextAppearDelay { get; set; }

    public bool IsCurrent => ViewManager.GameView.CurrentDialogueBox == this;

    public override void _EnterTree()
    {
        var state = new PropertyState(this)
        {
            InitProperties = ["Opacity", "BackgroundColor", "TextColor", "Alignment", "Outline"]
        };
        StateManager.Instance.BindPropertyState(_bindName, state);

        GameState.Instance.DialogueWillChange.Subscribe(OnDialogueWillChange);
        GameState.Instance.ChoiceOccurs.Subscribe(OnChoiceOccurs);
    }

    public override void _ExitTree()
    {
        GameState.Instance.DialogueWillChange.Unsubscribe(OnDialogueWillChange);
        GameState.Instance.ChoiceOccurs.Unsubscribe(OnChoiceOccurs);
        _closeButton.Pressed -= OnCloseButtonPressed;
    }

    public override void _Ready()
    {
        UpdateBackgroundColor();
        UpdateTextColor();
        _closeButton.Pressed += OnCloseButtonPressed;
        _closeButton.Visible = _closeButtonShown;
    }

    private void OnDialogueWillChange()
    {
        TextAppearDelay = 0f;
        ShowDialogueFinishIcon(false);
    }

    private void OnChoiceOccurs(ChoiceOccursData _)
    {
        ShowDialogueFinishIcon(false);
    }

    private void OnCloseButtonPressed()
    {
        ViewManager.GameView.HideUI();
    }

    public void ShowDialogueFinishIcon(bool to)
    {
        if (_finishIcon != null)
        {
            _finishIcon.Visible = to;
        }
    }

    public bool IsTextRevealing => _textController.IsRevealing;

    public void CompleteTextReveal()
    {
        _textController.CompleteReveal();
    }

    private void UpdateBackgroundColor()
    {
        if (!IsNodeReady())
        {
            return;
        }
        _background.Modulate = new Color(_backgroundColor, _backgroundColor.A * _opacity);
    }

    private void UpdateTextColor()
    {
        if (!IsNodeReady())
        {
            return;
        }
        _textController.UpdateColor(_textColor);
    }

    public void DisplayDialogue(DialogueDisplayData displayData)
    {
        var entry = _mode switch
        {
            Mode.Overwrite => OverwriteDialogue(displayData),
            Mode.Append => AppendDialogue(displayData),
            _ => throw new ArgumentOutOfRangeException(),
        };

        // The finish icon should only appear once the text is actually fully shown, not the
        // instant the entry is created - wait for the reveal Tween if there is one (mode 0 / a
        // restore replay has none, so show immediately in that case).
        if (entry.IsRevealing)
        {
            entry.RevealFinished += OnEntryRevealFinished;
        }
        else
        {
            ShowDialogueFinishIcon(true);
        }
    }

    private void OnEntryRevealFinished()
    {
        ShowDialogueFinishIcon(true);
    }

    public void NewPage()
    {
        _textController.Clear();
    }

    private DialogueEntryController AppendDialogue(DialogueDisplayData displayData)
    {
        var appear = new TextAppearSettings
        {
            Mode = TextAppearMode,
            CharSpeed = TextAppearCharSpeed,
            FadeDuration = TextAppearFadeDuration,
            Delay = TextAppearDelay,
        };
        return _textController.AddEntry(displayData, _textColor, _alignment, _outline, appear);
    }

    private DialogueEntryController OverwriteDialogue(DialogueDisplayData displayData)
    {
        NewPage();
        return AppendDialogue(displayData);
    }
}
