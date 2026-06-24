using System;
using Godot;

namespace Nova;

/// <summary>
/// A single save slot in the SaveLoadController grid: thumbnail + id + date + "latest" badge + delete
/// button. Mirrors Nova1's SaveEntryController, but re-binds click callbacks by storing them in a field
/// instead of Add/RemoveListener churn - this instance is reused across pages by Init being called again
/// with new data, like Nova1, just with a simpler rebind mechanism (see ConfirmDialog for the same idiom).
/// </summary>
public partial class SaveEntryController : Control
{
    [Export]
    private Button _thumbnailButton;
    [Export]
    private TextureRect _thumbnailImage;
    [Export]
    private Label _idText;
    [Export]
    private Label _dateText;
    [Export]
    private Control _latestBadge;
    [Export]
    private Button _deleteButton;

    private Action _onThumbnailClicked;
    private Action _onDeleteClicked;

    public override void _Ready()
    {
        _thumbnailButton.Pressed += () => _onThumbnailClicked?.Invoke();
        _deleteButton.Pressed += () => _onDeleteClicked?.Invoke();
    }

    /// <summary>
    /// thumbnail == null means the slot is empty (no bookmark at this save id): id is still shown (so
    /// the grid stays a stable layout), but date/latest badge/delete button are all hidden, matching
    /// Nova1's "empty slot" presentation.
    /// </summary>
    public void Init(string idText, string dateText, bool isLatest, Texture2D thumbnail,
        Action onThumbnailClicked, Action onDeleteClicked)
    {
        var occupied = thumbnail != null;

        _idText.Text = idText;
        _dateText.Visible = occupied;
        _dateText.Text = dateText;
        _latestBadge.Visible = occupied && isLatest;
        _thumbnailImage.Texture = thumbnail;

        _onThumbnailClicked = onThumbnailClicked;
        _thumbnailButton.Disabled = onThumbnailClicked == null;

        _onDeleteClicked = onDeleteClicked;
        _deleteButton.Visible = occupied && onDeleteClicked != null;
    }
}
