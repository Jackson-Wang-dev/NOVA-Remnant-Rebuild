using System;
using Godot;

namespace Nova;

/// <summary>
/// A single row in the backlog view: clickable text (jump back, with confirm) + an optional "play
/// voice" button shown only when the entry recorded one. Mirrors Nova1's LogEntryController, but
/// without its double-click/select-twice gesture - jump uses the same one-click-then-ConfirmDialog
/// pattern already established for every other risky action this round (overwrite/delete/load/etc.).
///
/// The text is a plain Label (not a Label nested inside a Button) so its wrapped, multi-line minimum
/// size correctly propagates up through the HBoxContainer to the list's VBoxContainer - a Button does
/// not size itself to an arbitrary child Control, which made every row collapse to one line's height
/// and overlap the next row. Clicks are detected directly on the Label via GuiInput instead.
/// </summary>
public partial class BacklogEntryController : Control
{
    [Export]
    private Label _text;
    [Export]
    private Button _playVoiceButton;

    private Action _onJump;
    private Action _onPlayVoice;

    public override void _Ready()
    {
        _text.GuiInput += OnTextGuiInput;
        _playVoiceButton.Pressed += () => _onPlayVoice?.Invoke();
    }

    private void OnTextGuiInput(InputEvent evt)
    {
        if (evt is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _onJump?.Invoke();
        }
    }

    public void Init(string text, Action onJump, Action onPlayVoice)
    {
        _text.Text = text;
        _onJump = onJump;
        _onPlayVoice = onPlayVoice;
        _playVoiceButton.Visible = onPlayVoice != null;
    }

    /// <summary>Overwrites just the displayed text, used by BacklogViewController to refresh an
    /// already-initialized row with its memory-degraded variant as the entry ages.</summary>
    public void SetText(string text)
    {
        _text.Text = text;
    }
}
