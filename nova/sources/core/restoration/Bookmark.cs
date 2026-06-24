using System;

namespace Nova;

/// <summary>
/// A save slot. Points into the checkpoint tree (<see cref="SaveManager"/>) by node record id + dialogue
/// index, rather than embedding a full state snapshot: loading a bookmark replays the script from the
/// start node down to that position (see GameState's restore path), matching the "pure script replay"
/// principle from upstream issue #1 instead of Nova1's snapshot-based instant resume.
/// </summary>
public class Bookmark
{
    public int NodeRecordId { get; set; }
    public int DialogueIndex { get; set; }
    public DialogueDisplayData Description { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.Now;
    public long GlobalSaveIdentifier { get; set; }
}

public enum BookmarkType
{
    AutoSave = 101,
    QuickSave = 201,
    NormalSave = 301
}

public class BookmarkMetadata
{
    private int _saveId;

    public int SaveId
    {
        get => _saveId;
        set
        {
            Type = SaveIdToBookmarkType(value);
            _saveId = value;
        }
    }

    public BookmarkType Type { get; private set; }
    public DateTime CreationTime { get; set; }

    public static BookmarkType SaveIdToBookmarkType(int saveId)
    {
        if (saveId >= (int)BookmarkType.NormalSave)
        {
            return BookmarkType.NormalSave;
        }

        if (saveId >= (int)BookmarkType.QuickSave)
        {
            return BookmarkType.QuickSave;
        }

        return BookmarkType.AutoSave;
    }
}
