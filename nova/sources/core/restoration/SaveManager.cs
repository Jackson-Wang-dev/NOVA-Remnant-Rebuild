using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Newtonsoft.Json;

namespace Nova;

/// <summary>
/// Owns persistence for the checkpoint tree, the reached-dialogue/reached-end history log, small
/// per-profile global save data, and bookmarks (save slots).
///
/// This replaces Nova1's CheckpointManager + CheckpointSerializer + CheckpointBlock. Per upstream
/// issue #1 (https://github.com/Lunatic-Works/Nova2/issues/1), the hand-rolled 4096-byte block linked
/// list is dropped; each logical piece is its own JSON file under user://save/, and the whole file is
/// rewritten on <see cref="Flush"/> instead of being incrementally patched in place ("交给文件系统").
///
/// Unlike Nova1, there is no GameStateCheckpoint snapshot stored per dialogue: loading a bookmark
/// replays the script (see GameState's restore path) instead of deserializing a full render-state
/// snapshot, matching the "pure script replay" principle from issue #1. The NodeRecord tree still
/// exists (and is built during normal play, not only during restore) so that bookmarks have a stable
/// address and future variable-based dedup of revisited nodes has somewhere to live.
/// </summary>
public class SaveManager : ISingleton
{
    private const string SaveDir = "user://save/";
    private const string GlobalSavePath = SaveDir + "global_save.json";
    private const string ReachedPath = SaveDir + "reached.json";
    private const string CheckpointsPath = SaveDir + "checkpoints.json";
    private const string BookmarksDir = SaveDir + "bookmarks/";

    private GlobalSave _globalSave;

    private readonly Dictionary<string, List<ReachedDialogueData>> _reachedDialogues = [];
    private readonly HashSet<string> _reachedEnds = [];

    private readonly Dictionary<int, NodeRecord> _nodeRecords = [];
    private int _rootId = NodeRecord.NoId;
    private int _nextNodeRecordId = NodeRecord.NoId;
    private int _lastCreatedId = NodeRecord.NoId;

    public readonly Dictionary<int, BookmarkMetadata> BookmarksMetadata = [];

    private bool _dirty;

    public void OnEnter()
    {
        EnsureDir(SaveDir);
        EnsureDir(BookmarksDir);

        _globalSave = ReadJson<GlobalSave>(GlobalSavePath) ?? GlobalSave.Create();
        LoadReached();
        LoadCheckpoints();
        ScanBookmarks();
    }

    public void OnReady() { }

    public void OnExit()
    {
        Flush();
    }

    #region File I/O helpers

    private static void EnsureDir(string path)
    {
        if (!DirAccess.DirExistsAbsolute(path))
        {
            DirAccess.MakeDirRecursiveAbsolute(path);
        }
    }

    private static T ReadJson<T>(string path) where T : class
    {
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<T>(Utils.GetFileAsText(path));
    }

    private static void WriteJson<T>(string path, T data)
    {
        using var fs = Utils.OpenFile(path, FileAccess.ModeFlags.Write);
        fs.StoreString(JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    #endregion

    #region Reached data

    private class ReachedFile
    {
        public Dictionary<string, List<ReachedDialogueData>> ReachedDialogues { get; set; } = [];
        public List<string> ReachedEnds { get; set; } = [];
    }

    private void LoadReached()
    {
        _reachedDialogues.Clear();
        _reachedEnds.Clear();
        var file = ReadJson<ReachedFile>(ReachedPath);
        if (file == null)
        {
            return;
        }

        foreach (var (name, list) in file.ReachedDialogues)
        {
            _reachedDialogues[name] = list;
        }

        foreach (var end in file.ReachedEnds)
        {
            _reachedEnds.Add(end);
        }
    }

    private static List<ReachedDialogueData> Ensure(Dictionary<string, List<ReachedDialogueData>> dict, string key)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = [];
            dict[key] = list;
        }

        return list;
    }

    public bool IsReachedAnyHistory(string nodeName, int dialogueIndex)
    {
        return _reachedDialogues.TryGetValue(nodeName, out var list) &&
            dialogueIndex < list.Count && list[dialogueIndex].NodeName != null;
    }

    public ReachedDialogueData GetReachedDialogue(string nodeName, int dialogueIndex)
    {
        return _reachedDialogues[nodeName][dialogueIndex];
    }

    public void SetReachedDialogue(ReachedDialogueData data)
    {
        if (IsReachedAnyHistory(data.NodeName, data.DialogueIndex))
        {
            return;
        }

        var list = Ensure(_reachedDialogues, data.NodeName);
        while (list.Count <= data.DialogueIndex)
        {
            list.Add(default);
        }

        list[data.DialogueIndex] = data;
        _dirty = true;
    }

    public bool IsReachedEnd(string endName) => _reachedEnds.Contains(endName);

    public void SetReachedEnd(string endName)
    {
        if (_reachedEnds.Add(endName))
        {
            _dirty = true;
        }
    }

    #endregion

    #region Global variables

    /// <summary>
    /// Backing store for gv_ variables (see Variables.cs), reusing GlobalSave's generic per-profile
    /// Data dictionary instead of a dedicated file - global variables share the same persistence
    /// lifetime as the rest of GlobalSave (flushed on explicit save and on exit).
    /// </summary>
    public object GetGlobalVariable(string name)
    {
        return _globalSave.Data.GetValueOrDefault(name);
    }

    public void SetGlobalVariable(string name, object value)
    {
        if (value == null)
        {
            if (_globalSave.Data.Remove(name))
            {
                _dirty = true;
            }
        }
        else
        {
            _globalSave.Data[name] = value;
            _dirty = true;
        }
    }

    #endregion

    #region Checkpoint tree

    private class CheckpointsFile
    {
        public int RootId { get; set; } = NodeRecord.NoId;
        public int NextId { get; set; } = NodeRecord.NoId;
        public int LastCreatedId { get; set; } = NodeRecord.NoId;
        public List<NodeRecord> Records { get; set; } = [];
    }

    private void LoadCheckpoints()
    {
        _nodeRecords.Clear();
        _rootId = NodeRecord.NoId;
        _nextNodeRecordId = NodeRecord.NoId;
        _lastCreatedId = NodeRecord.NoId;

        var file = ReadJson<CheckpointsFile>(CheckpointsPath);
        if (file == null)
        {
            return;
        }

        _rootId = file.RootId;
        _nextNodeRecordId = file.NextId;
        _lastCreatedId = file.LastCreatedId;
        foreach (var record in file.Records)
        {
            _nodeRecords[record.Id] = record;
        }
    }

    public NodeRecord GetNodeRecord(int id)
    {
        return _nodeRecords.GetValueOrDefault(id);
    }

    public bool IsLastNodeRecord(NodeRecord record)
    {
        return record.Id == _lastCreatedId;
    }

    /// <summary>
    /// Get or create the next node record, mirroring Nova1's CheckpointManager.GetNextNodeRecord:
    /// records with the same name and variablesHash under the same parent are deduplicated (reused)
    /// rather than creating a new sibling.
    /// </summary>
    public NodeRecord GetNextNodeRecord(NodeRecord prevRecord, string name, ulong variablesHash, int beginDialogue)
    {
        NodeRecord childRecord = null;
        var id = prevRecord?.ChildId ?? _rootId;
        while (id != NodeRecord.NoId)
        {
            childRecord = _nodeRecords[id];
            if (childRecord.Name == name && childRecord.VariablesHash == variablesHash)
            {
                return childRecord;
            }

            id = childRecord.SiblingId;
        }

        var newId = ++_nextNodeRecordId;
        var newRecord = new NodeRecord(newId, name, beginDialogue, variablesHash);
        if (childRecord != null)
        {
            childRecord.SiblingId = newId;
        }
        else if (prevRecord != null)
        {
            prevRecord.ChildId = newId;
        }
        else
        {
            _rootId = newId;
        }

        if (prevRecord != null)
        {
            newRecord.ParentId = prevRecord.Id;
        }

        _nodeRecords[newId] = newRecord;
        _lastCreatedId = newId;
        _dirty = true;
        return newRecord;
    }

    public void AppendDialogue(NodeRecord record, int dialogueIndex)
    {
        record.EndDialogue = dialogueIndex + 1;
        _dirty = true;
    }

    /// <summary>
    /// Walk from the root down to the given node record, for replaying a bookmarked position.
    /// </summary>
    public List<NodeRecord> GetPathTo(int nodeRecordId)
    {
        var path = new List<NodeRecord>();
        for (var id = nodeRecordId; id != NodeRecord.NoId;)
        {
            var record = _nodeRecords[id];
            path.Add(record);
            id = record.ParentId;
        }

        path.Reverse();
        return path;
    }

    #endregion

    #region Bookmarks

    private static string BookmarkPath(int saveId) => $"{BookmarksDir}sav{saveId:D3}.json";

    private void ScanBookmarks()
    {
        BookmarksMetadata.Clear();
        using var dir = DirAccess.Open(BookmarksDir);
        if (dir == null)
        {
            return;
        }

        dir.ListDirBegin();
        for (var fileName = dir.GetNext(); fileName != ""; fileName = dir.GetNext())
        {
            var match = Regex.Match(fileName, @"^sav(\d+)\.json$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id))
            {
                continue;
            }

            var bookmark = ReadJson<Bookmark>(BookmarksDir + fileName);
            if (bookmark == null)
            {
                continue;
            }

            BookmarksMetadata[id] = new BookmarkMetadata { SaveId = id, CreationTime = bookmark.CreationTime };
        }
    }

    /// <summary>
    /// Used save slot ids in [begin, end), sorted - drives Tier 3's save/load grid (page contents,
    /// "latest" badge) without introducing any new persisted concept; it is a pure scan of the
    /// already-persisted BookmarksMetadata.
    /// </summary>
    public IEnumerable<int> GetUsedSaveIds(int begin, int end)
    {
        return BookmarksMetadata.Keys.Where(id => id >= begin && id < end).OrderBy(id => id);
    }

    public int GetMinUnusedSaveId(int begin, int end)
    {
        var id = begin;
        while (id < end && BookmarksMetadata.ContainsKey(id))
        {
            ++id;
        }

        return id;
    }

    /// <summary>
    /// The most recently created bookmark in [begin, end), or NodeRecord.NoId if none exists.
    /// </summary>
    public int GetLatestSaveId(int begin, int end)
    {
        var ids = GetUsedSaveIds(begin, end).ToList();
        return ids.Count == 0
            ? NodeRecord.NoId
            : ids.OrderByDescending(id => BookmarksMetadata[id].CreationTime).First();
    }

    public void SaveBookmark(int saveId, NodeRecord nodeRecord, int dialogueIndex, DialogueDisplayData description)
    {
        var bookmark = new Bookmark
        {
            NodeRecordId = nodeRecord.Id,
            DialogueIndex = dialogueIndex,
            Description = description,
            GlobalSaveIdentifier = _globalSave.Identifier
        };
        WriteJson(BookmarkPath(saveId), bookmark);
        BookmarksMetadata[saveId] = new BookmarkMetadata { SaveId = saveId, CreationTime = bookmark.CreationTime };

        // Bookmarks are written immediately (not gated on Flush) since they are explicit user actions;
        // still flush the tree/reached logs now so the bookmark's NodeRecordId is guaranteed resolvable
        // even if the game crashes before the next OnExit.
        _dirty = true;
        Flush();
    }

    public Bookmark LoadBookmark(int saveId)
    {
        var bookmark = ReadJson<Bookmark>(BookmarkPath(saveId));
        if (bookmark != null && bookmark.GlobalSaveIdentifier != _globalSave.Identifier)
        {
            Utils.Warn($"Bookmark {saveId} belongs to a different global save (identifier mismatch).");
        }

        return bookmark;
    }

    public void DeleteBookmark(int saveId)
    {
        var path = BookmarkPath(saveId);
        if (FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(path);
        }

        var screenshotPath = ScreenshotPath(saveId);
        if (FileAccess.FileExists(screenshotPath))
        {
            DirAccess.RemoveAbsolute(screenshotPath);
        }

        BookmarksMetadata.Remove(saveId);
    }

    #endregion

    #region Screenshots

    /// <summary>
    /// Bookmark thumbnails are stored as their own PNG file next to the bookmark's json (path derived
    /// by convention from the save id, like BookmarkPath) rather than a field on Bookmark - deferred
    /// from M1 to this Tier 3 pass per the decision log, see porting-guide.md.
    /// </summary>
    private static string ScreenshotPath(int saveId) => $"{BookmarksDir}sav{saveId:D3}.png";

    private const int ThumbnailWidth = 320;

    public void SaveScreenshot(int saveId, Image image)
    {
        var height = image.GetHeight() * ThumbnailWidth / image.GetWidth();
        image.Resize(ThumbnailWidth, height, Image.Interpolation.Lanczos);
        image.SavePng(ScreenshotPath(saveId));
    }

    /// <summary>
    /// Path to the saved screenshot for saveId, or null if it has none (e.g. corrupted/missing file).
    /// </summary>
    public string GetScreenshotPath(int saveId)
    {
        var path = ScreenshotPath(saveId);
        return FileAccess.FileExists(path) ? path : null;
    }

    #endregion

    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        WriteJson(GlobalSavePath, _globalSave);
        WriteJson(ReachedPath, new ReachedFile
        {
            ReachedDialogues = _reachedDialogues,
            ReachedEnds = [.. _reachedEnds]
        });
        WriteJson(CheckpointsPath, new CheckpointsFile
        {
            RootId = _rootId,
            NextId = _nextNodeRecordId,
            LastCreatedId = _lastCreatedId,
            Records = [.. _nodeRecords.Values]
        });

        _dirty = false;
    }

    public static SaveManager Instance => NovaController.Instance.GetObj<SaveManager>();
}
