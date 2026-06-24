using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Nova;

public enum SaveLoadMode
{
    Save,
    Load
}

/// <summary>
/// Save/Load panel, shared between both modes via <see cref="Mode"/> like Nova1's SaveViewController.
/// Paging only ever covers the NormalSave range now (3x3 per page) - QuickSave and AutoSave each get a
/// single always-visible dedicated slot next to the grid instead of Nova1's full extra "page" per type,
/// since nova2 only ever writes one fixed QuickSave bookmark and never writes AutoSave at all (a whole
/// 9-slot page for at most 1 real entry was wasteful). The AutoSave slot is a deliberate empty
/// placeholder so a future autosave trigger only needs to start writing into that saveId, not touch this
/// UI again (see porting-guide.md decision log).
///
/// Simplified vs. Nova1: no separate large preview pane (slots show their own info directly) and no
/// blurred-screenshot backdrop (a flat dim color instead) - both logged as decisions, not omissions.
/// </summary>
public partial class SaveLoadController : ViewController
{
    private const int Rows = 3;
    private const int Cols = 3;
    private const int SlotsPerPage = Rows * Cols;
    private const string DateTimeFormat = "yyyy/MM/dd  HH:mm";

    [Export]
    private GridContainer _grid;
    [Export]
    private PackedScene _saveEntryScene;
    [Export]
    private SaveEntryController _quickSaveEntry;
    [Export]
    private SaveEntryController _autoSaveEntry;
    [Export]
    private Button _backButton;
    [Export]
    private Button _pageLeftButton;
    [Export]
    private Button _pageRightButton;
    [Export]
    private Label _pageText;

    private GameState _gameState;
    private SaveManager _saveManager;

    private readonly List<SaveEntryController> _entries = [];
    private readonly Dictionary<int, ImageTexture> _thumbnailCache = [];

    public SaveLoadMode Mode { get; set; }
    public bool FromTitle { get; set; }

    private int _page = 1;
    private int _maxPage = 1;

    public override void _EnterTree()
    {
        base._EnterTree();

        _gameState = GameState.Instance;
        _saveManager = SaveManager.Instance;

        // Not driven by I18nText: that script attaches to the node and replaces its C# wrapper type,
        // which breaks the [Export] Button NodePath binding above - see ConfirmDialog for the same note.
        _backButton.Text = I18n.__("help.close");

        for (var i = 0; i < SlotsPerPage; i++)
        {
            var entry = _saveEntryScene.Instantiate<SaveEntryController>();
            _grid.AddChild(entry);
            _entries.Add(entry);
        }

        _backButton.Pressed += CloseToOrigin;
        _pageLeftButton.Pressed += PageLeft;
        _pageRightButton.Pressed += PageRight;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        _backButton.Pressed -= CloseToOrigin;
        _pageLeftButton.Pressed -= PageLeft;
        _pageRightButton.Pressed -= PageRight;
    }

    public override void ShowPanel(bool doTransition, Action onFinish)
    {
        if (!Active)
        {
            var beginId = (int)BookmarkType.NormalSave;
            var saveId = Mode == SaveLoadMode.Save
                ? _saveManager.GetMinUnusedSaveId(beginId, int.MaxValue)
                : _saveManager.GetLatestSaveId(beginId, int.MaxValue);
            _page = saveId == NodeRecord.NoId ? 1 : SaveIdToPage(saveId);
        }

        RefreshPage();
        base.ShowPanel(doTransition, onFinish);
    }

    private void CloseToOrigin()
    {
        if (FromTitle)
        {
            this.SwitchView<TitleController>();
        }
        else
        {
            this.SwitchView<GameViewController>();
        }
    }

    #region Paging

    private void PageLeft()
    {
        if (_page > 1)
        {
            --_page;
            RefreshPage();
        }
    }

    private void PageRight()
    {
        if (_page < _maxPage)
        {
            ++_page;
            RefreshPage();
        }
    }

    private void RefreshPage()
    {
        var beginId = (int)BookmarkType.NormalSave;
        var usedIds = _saveManager.GetUsedSaveIds(beginId, int.MaxValue).ToList();
        var maxUsedPage = usedIds.Count == 0 ? 1 : SaveIdToPage(usedIds[^1]);
        _maxPage = Mode == SaveLoadMode.Save ? maxUsedPage + 1 : Math.Max(maxUsedPage, 1);
        _page = Math.Clamp(_page, 1, _maxPage);

        _pageLeftButton.Disabled = _page <= 1;
        _pageRightButton.Disabled = _page >= _maxPage;
        _pageText.Text = $"{_page} / {_maxPage}";

        var pageBegin = beginId + (_page - 1) * SlotsPerPage;
        var latestId = _saveManager.GetLatestSaveId(beginId, int.MaxValue);

        for (var i = 0; i < SlotsPerPage; i++)
        {
            var saveId = pageBegin + i;
            RefreshEntry(_entries[i], saveId, SaveIdToDisplayId(saveId).ToString(), saveId == latestId);
        }

        RefreshEntry(_quickSaveEntry, (int)BookmarkType.QuickSave, I18n.__("bookmark.quicksave.page"), false);

        // AutoSave never has a real bookmark yet (no trigger writes it) and is never user-savable -
        // unlike an empty NormalSave/QuickSave slot, it must stay inert even in Save mode.
        _autoSaveEntry.Init(I18n.__("bookmark.autosave.page"), "", false, null, null, null);
    }

    private static int SaveIdToPage(int saveId)
    {
        return (saveId - (int)BookmarkType.NormalSave) / SlotsPerPage + 1;
    }

    private static int SaveIdToDisplayId(int saveId)
    {
        return saveId - (int)BookmarkType.NormalSave + 1;
    }

    #endregion

    #region Entries

    private void RefreshEntry(SaveEntryController entry, int saveId, string idText, bool isLatest)
    {
        if (_saveManager.BookmarksMetadata.TryGetValue(saveId, out var meta))
        {
            var dateText = meta.CreationTime.ToString(DateTimeFormat);
            Action onClick = Mode == SaveLoadMode.Save
                ? () => ConfirmDialog.Instance.Show("bookmark.overwrite.confirm", () => SaveSlot(saveId), null, idText)
                : () => ConfirmDialog.Instance.Show("bookmark.load.confirm", () => LoadSlot(saveId), null, idText);
            Action onDelete = () => ConfirmDialog.Instance.Show("bookmark.delete.confirm", () => DeleteSlot(saveId), null, idText);
            entry.Init(idText, dateText, isLatest, GetThumbnail(saveId), onClick, onDelete);
        }
        else
        {
            Action onClick = Mode == SaveLoadMode.Save ? () => SaveSlot(saveId) : null;
            entry.Init(idText, "", false, null, onClick, null);
        }
    }

    private ImageTexture GetThumbnail(int saveId)
    {
        if (_thumbnailCache.TryGetValue(saveId, out var cached))
        {
            return cached;
        }

        var path = _saveManager.GetScreenshotPath(saveId);
        if (path == null)
        {
            return null;
        }

        var image = new Image();
        if (image.Load(path) != Error.Ok)
        {
            return null;
        }

        var texture = ImageTexture.CreateFromImage(image);
        _thumbnailCache[saveId] = texture;
        return texture;
    }

    private void InvalidateThumbnail(int saveId)
    {
        _thumbnailCache.Remove(saveId);
    }

    #endregion

    #region Actions

    private void SaveSlot(int saveId)
    {
        var image = ViewManager.GameView.CaptureThumbnail();
        _gameState.SaveBookmark(saveId);
        _saveManager.SaveScreenshot(saveId, image);
        InvalidateThumbnail(saveId);
        RefreshPage();
    }

    private void LoadSlot(int saveId)
    {
        _gameState.LoadGame(saveId);
        this.SwitchView<GameViewController>();
    }

    private void DeleteSlot(int saveId)
    {
        _saveManager.DeleteBookmark(saveId);
        InvalidateThumbnail(saveId);
        RefreshPage();
    }

    #endregion
}
