using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nova.Exceptions;

namespace Nova;

public partial class GameState : ISingleton
{
    private FlowChartGraph _flowChartGraph;

    public FlowChartNode CurrentNode { get; private set; }
    private int _currentDialogueIndex;
    private DialogueEntry _currentDialogueEntry;
    private bool _ensureCheckpoint;

    // Internal speaker name of whichever entry is currently executing/just executed its Default
    // action - stable for the whole BeforeCheckpoint/Default/DialogueChanged/AfterDialogue sequence
    // of one entry (see UpdateDialogue), so callers like AvatarController can read "who is currently
    // speaking" both while Default-stage script calls (e.g. avatar()) run and when DialogueChanged fires.
    public string CurrentCharacterName => _currentDialogueEntry?.CharacterName;

    /// <summary>
    /// NodeRecord id for the dialogue entry currently being processed - valid to read synchronously
    /// from a DialogueChanged handler (BacklogViewController does, to remember where to jump back to).
    /// </summary>
    public int CurrentNodeRecordId => _nodeRecord?.Id ?? NodeRecord.NoId;

    /// <summary>
    /// Index of the dialogue entry currently being displayed within CurrentNode - paired with
    /// CurrentNodeRecordId to address "where the player is" (see ReloadScripts/MoveTo).
    /// </summary>
    public int CurrentDialogueIndex => _currentDialogueIndex;

    /// <summary>
    /// Pointer into SaveManager's checkpoint tree for the current (node, dialogue index) position.
    /// Advanced alongside CurrentNode/_currentDialogueIndex; see MoveToNextNode and SaveCheckpoint.
    /// </summary>
    private NodeRecord _nodeRecord;

    private enum State
    {
        Normal,
        Ended,
        Restoring,
        Upgrading
    }
    private State _state = State.Normal;
    public bool IsEnded => _state == State.Ended;
    public bool IsRestoring => _state == State.Restoring;
    public bool IsUpgrading => _state == State.Upgrading;
    public bool CanStepForward => CurrentNode != null && _state == State.Normal && !_fence.Taken;

    /// <summary>
    /// The current coroutine for async functions.
    /// </summary>
    private Coroutine _coroutine;
    /// <summary>
    /// The fence used to switch context back to game and get result from user interaction. (i.e. a pause)
    /// </summary>
    private readonly Fence _fence = new();

    #region Events

    public readonly Event GameStarted = new();
    /// <summary>
    /// This event will be triggered if the node has changed. The new node name will be sent to all listeners.
    /// </summary>
    public readonly Event<NodeChangedData> NodeChanged = new();
    /// <summary>
    /// This event will be triggered if the content of the dialogue will change. It will be triggered before
    /// the lazy execution block of the new dialogue is invoked.
    /// </summary>
    public readonly Event DialogueWillChange = new();
    /// <summary>
    /// This event will be triggered if the content of the dialogue has changed, but before the dialogue box
    /// receives the text. All state objects will sync to frontend components at this point.
    /// </summary>
    public readonly Event<DialogueChangedData> DialogueChangedEarly = new();
    /// <summary>
    /// This event will be triggered if the content of the dialogue has changed. The new dialogue text will be
    /// sent to all listeners.
    /// </summary>
    public readonly Event<DialogueChangedData> DialogueChanged = new();
    /// <summary>
    /// This event will be triggered if choices occur, either when branches occur or when choices are
    /// triggered from the script.
    /// </summary>
    public readonly Event<ChoiceOccursData> ChoiceOccurs = new();
    /// <summary>
    /// This event will be triggered if the story route has reached an end.
    /// </summary>
    public readonly Event<ReachedEndData> RouteEnded = new();
    public readonly Event<bool> RestoreStarts = new();

    #endregion

    public void OnEnter()
    {
        _flowChartGraph = NovaController.Instance.GetObj<ScriptLoader>().FlowChartGraph;
    }

    public void OnReady() { }

    public void OnExit()
    {
        CancelCoroutine();
    }

    private void StartCoroutine(Func<CancellationToken, Task> asyncFunc)
    {
        ResetCoroutineContext();
        _coroutine = Coroutine.Start(asyncFunc);
    }

    private void CancelCoroutine()
    {
        _coroutine?.Cancel();
        _coroutine = null;
        ResetCoroutineContext();
    }

    private void ResetCoroutineContext()
    {
        // TODO
    }

    public void ResetGameState()
    {
        CancelCoroutine();
        CurrentNode = null;
        _currentDialogueIndex = 0;
        _currentDialogueEntry = null;
        _nodeRecord = null;
        _state = State.Ended;
        Variables.Instance.ClearLocal();
    }

    public void SignalFence<T>(T result)
    {
        _fence.Signal(result);
    }

    public FlowChartNode GetNode(string name, bool addDeferred = true)
    {
        var node = _flowChartGraph.GetNode(name);
        if (addDeferred)
        {
            ScriptLoader.AddDeferredDialogueChunks(node);
        }
        return node;
    }

    public IEnumerable<string> GetStartNodeNames(StartNodeType type)
    {
        return _flowChartGraph.GetStartNodeNames(type);
    }

    private void StartGame(FlowChartNode startNode)
    {
        ResetGameState();
        StateManager.Instance.ResetToBaseline();
        VfxManager.Instance.ClearAllLayers();
        _state = State.Normal;
        GameStarted.Invoke();
        MoveToNextNode(startNode);
    }

    public void StartGame(string startNode)
    {
        StartGame(GetNode(startNode));
    }

    public void Step()
    {
        if (!CanStepForward)
        {
            return;
        }

        if (_currentDialogueIndex + 1 < CurrentNode.DialogueEntryCount)
        {
            ++_currentDialogueIndex;
            UpdateGameState(false, true, false, true, false);
        }
        else
        {
            StepAtEndOfNode();
        }
    }

    /// <summary>
    /// Called after the current node or the current dialogue index has changed
    /// </summary>
    /// <remarks>
    /// Trigger events according to the current states and how they were changed
    /// </remarks>
    private void UpdateGameState(bool nodeChanged, bool dialogueChanged, bool firstEntryOfNode,
        bool dialogueStepped, bool fromCheckpoint)
    {
        if (nodeChanged)
        {
            NodeChanged.Invoke(new() { NewNode = CurrentNode.Name });
            if (firstEntryOfNode)
            {
                _ensureCheckpoint = true;
            }
        }

        if (dialogueChanged)
        {
            Utils.RuntimeAssert(_currentDialogueIndex >= 0 && (
                CurrentNode.DialogueEntryCount == 0 ||
                _currentDialogueIndex < CurrentNode.DialogueEntryCount),
                "Dialogue index out of range.");

            if (CurrentNode.DialogueEntryCount > 0)
            {
                _currentDialogueEntry = CurrentNode.GetDialogueEntryAt(_currentDialogueIndex);
                StartCoroutine(token => UpdateDialogue(firstEntryOfNode, dialogueStepped, fromCheckpoint, token));
            }
            else
            {
                StepAtEndOfNode();
            }
        }
    }

    private async Task ExecuteDialogueAction(DialogueActionStage stage, CancellationToken token)
    {
        _currentDialogueEntry.ExecuteAction(stage, IsRestoring);
        await _fence.Barrier(token);
    }

    private async Task UpdateDialogue(bool firstEntryOfNode, bool dialogueStepped,
        bool fromCheckpoint, CancellationToken token)
    {
        // 1. execute BeforeCheckpoint action
        if (!fromCheckpoint)
        {
            await ExecuteDialogueAction(DialogueActionStage.BeforeCheckpoint, token);
        }

        // 2. save Checkpoint
        var isReached = SaveCheckpoint(firstEntryOfNode, dialogueStepped);

        // 3. invoke will change event
        DialogueWillChange.Invoke();

        // 3. execute Default action
        await ExecuteDialogueAction(DialogueActionStage.Default, token);

        // 4. save reached data
        var isReachedAnyHistory = SaveReachedData(out var dialogueData);
        var dialogueChangedData = new DialogueChangedData()
        {
            DialogueData = dialogueData,
            DisplayData = _currentDialogueEntry.GetDisplayData(),
            IsReached = isReached, IsReachedAnyHistory = isReachedAnyHistory
        };

        // 5. invoke early event and then default event
        DialogueChangedEarly.Invoke(dialogueChangedData);
        DialogueChanged.Invoke(dialogueChangedData);

        // 6. execute AfterDialogue action
        await ExecuteDialogueAction(DialogueActionStage.AfterDialogue, token);

        // TODO: advancedDialogueHelper
    }

    private void StepAtEndOfNode()
    {
        switch (CurrentNode.Type)
        {
            case FlowChartNodeType.Normal:
                MoveToNextNode(CurrentNode.Next);
                break;
            case FlowChartNodeType.Branching:
                StartCoroutine(token => DoBranch(CurrentNode.GetAllBranches(), token));
                break;
            case FlowChartNodeType.End:
                _state = State.Ended;
                var endName = _flowChartGraph.GetEndName(CurrentNode);
                SaveManager.Instance.SetReachedEnd(endName);
                RouteEnded.Invoke(new() { EndName = endName });
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void MoveToNextNode(FlowChartNode nextNode)
    {
        ScriptLoader.AddDeferredDialogueChunks(nextNode);
        // in case of empty node, do not change any of these
        // so the bookmark is left at the end of last node
        if (nextNode.DialogueEntryCount > 0)
        {
            _nodeRecord = SaveManager.Instance.GetNextNodeRecord(_nodeRecord, nextNode.Name, Variables.Instance.Hash, 0);
            _currentDialogueIndex = 0;
        }

        CurrentNode = nextNode;
        UpdateGameState(true, true, true, true, false);
    }

    private async Task DoBranch(IEnumerable<BranchInformation> branchInfos, CancellationToken token)
    {
        var choices = new List<ChoiceData>();
        var choiceNames = new List<string>();
        foreach (var branchInfo in branchInfos)
        {
            if (branchInfo.Mode == BranchMode.Jump)
            {
                if (GDRuntime.InvokeCondition(branchInfo.Condition))
                {
                    SelectBranch(branchInfo.Name);
                    return;
                }
                continue;
            }

            if (branchInfo.Mode == BranchMode.Show && !GDRuntime.InvokeCondition(branchInfo.Condition))
            {
                continue;
            }

            var choice = new ChoiceData()
            {
                Texts = branchInfo.Texts, ImageInfo = branchInfo.ImageInfo,
                Interactable = branchInfo.Mode != BranchMode.Enable || GDRuntime.InvokeCondition(branchInfo.Condition)
            };
            choices.Add(choice);
            choiceNames.Add(branchInfo.Name);
        }

        var fence = _fence.Take<int>(token);
        RaiseChoices(choices);
        var index = await fence;

        SelectBranch(choiceNames[index]);
    }

    public void RaiseChoices(IReadOnlyList<ChoiceData> choices)
    {
        ChoiceOccurs.Invoke(new() { Choices = choices });
    }

    private void SelectBranch(string branchName)
    {
        MoveToNextNode(CurrentNode.GetNext(branchName));
    }

    /// <summary>
    /// Advance _nodeRecord's bookkeeping for the current dialogue index.
    /// </summary>
    /// <remarks>
    /// No GameStateCheckpoint snapshot is recorded (unlike Nova1): loading a bookmark replays the
    /// script instead of deserializing a snapshot, see SaveManager and RestorePath. This method only
    /// maintains the checkpoint tree's bookkeeping (EndDialogue / branching-revisit dedup), which is
    /// still needed so bookmarks have a stable NodeRecord to point at.
    /// </remarks>
    /// <returns>
    /// Whether this exact (node, dialogue index) was already recorded under _nodeRecord before this
    /// call - true when re-stepping through previously-visited territory (e.g. during restore).
    /// </returns>
    private bool SaveCheckpoint(bool firstEntryOfNode, bool dialogueStepped)
    {
        var saveManager = SaveManager.Instance;

        // If we've stepped past everything previously recorded for this NodeRecord, but it already has
        // a child (i.e. some other continuation was already recorded from here), this step represents a
        // different path through the same node and needs its own sibling NodeRecord rather than mutating
        // the existing one. Mirrors Nova1's CheckpointManager.AppendSameNode trigger.
        var atEndOfNodeRecord = _nodeRecord.ChildId != NodeRecord.NoId && _currentDialogueIndex >= _nodeRecord.EndDialogue;
        var isReached = _currentDialogueIndex < _nodeRecord.EndDialogue;
        if (atEndOfNodeRecord)
        {
            _nodeRecord = saveManager.GetNextNodeRecord(_nodeRecord, _nodeRecord.Name, Variables.Instance.Hash, _currentDialogueIndex);
            isReached = _currentDialogueIndex < _nodeRecord.EndDialogue;
        }

        if (!isReached)
        {
            saveManager.AppendDialogue(_nodeRecord, _currentDialogueIndex);
        }

        return isReached;
    }

    private bool SaveReachedData(out ReachedDialogueData dialogueData)
    {
        var saveManager = SaveManager.Instance;
        var isReachedAnyHistory = saveManager.IsReachedAnyHistory(_nodeRecord.Name, _currentDialogueIndex);

        // Always build dialogueData from the live _currentDialogueEntry rather than trusting
        // saveManager.GetReachedDialogue's persisted record when isReachedAnyHistory - that record's
        // TextHash is whatever the script content hashed to the first time this (node, index) was
        // reached, possibly in an earlier session, and never gets refreshed. Editing a scenario .txt
        // (e.g. adding/changing an <mmr>/<dmg> line) changes the live hash but not the stale persisted
        // one, so MemoryTable.Variants - rebuilt fresh from the current script every launch - would
        // permanently miss the lookup in BacklogViewController. SetReachedDialogue below still only
        // writes once per (node, index), preserving the existing "first reached" bookkeeping used by
        // IsReachedAnyHistory's unread/skip and chapter-unlock checks.
        dialogueData = new ReachedDialogueData()
        {
            NodeName = _nodeRecord.Name,
            DialogueIndex = _currentDialogueIndex,
            NeedInterpolate = _currentDialogueEntry.NeedInterpolate,
            TextHash = _currentDialogueEntry.TextHash
        };
        if (!isReachedAnyHistory)
        {
            saveManager.SetReachedDialogue(dialogueData);
        }
        return isReachedAnyHistory;
    }

    #region Bookmarks (save / load)

    /// <summary>
    /// Save the current position as a bookmark (save slot).
    /// </summary>
    public void SaveBookmark(int saveId)
    {
        if (_nodeRecord == null)
        {
            Utils.Warn("Cannot save bookmark: no active game.");
            return;
        }

        SaveManager.Instance.SaveBookmark(saveId, _nodeRecord, _currentDialogueIndex,
            _currentDialogueEntry.GetDisplayData());
    }

    /// <summary>
    /// Load a bookmark by replaying the script from the start down to its saved position.
    /// </summary>
    public void LoadGame(int saveId)
    {
        var bookmark = SaveManager.Instance.LoadBookmark(saveId);
        if (bookmark == null)
        {
            Utils.Warn($"Failed to load bookmark {saveId}.");
            return;
        }

        MoveTo(bookmark.NodeRecordId, bookmark.DialogueIndex);
    }

#if DEBUG
    internal readonly struct LocateResult()
    {
        public bool Ok { get; init; }
        public string Error { get; init; }
        public string NodeName { get; init; }
        public int DialogueIndex { get; init; }
        public int NodeRecordId { get; init; }
        public bool Reached { get; init; }
    }

    internal LocateResult Locate(string file, int line)
    {
        if (string.IsNullOrEmpty(file))
        {
            return new LocateResult { Ok = false, Error = "Missing file" };
        }

        if (line <= 0)
        {
            return new LocateResult { Ok = false, Error = "Line must be positive" };
        }

        FlowChartNode matchedNode = null;
        var matchedDialogueIndex = -1;
        foreach (var node in _flowChartGraph)
        {
            if (!string.Equals(node.SourceFile, file, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(node.SourceFile, System.IO.Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ScriptLoader.AddDeferredDialogueChunks(node);
            for (var i = 0; i < node.DialogueEntryCount; i++)
            {
                var entry = node.GetDialogueEntryAt(i);
                if (entry.SourceStartLine <= line && line <= entry.SourceEndLine)
                {
                    matchedNode = node;
                    matchedDialogueIndex = i;
                    break;
                }
            }

            if (matchedNode != null)
            {
                break;
            }
        }

        if (matchedNode == null)
        {
            return new LocateResult { Ok = false, Error = $"No dialogue entry found for {file}:{line}" };
        }

        var record = SaveManager.Instance.LocateReachedNodeRecord(
            matchedNode.Name, matchedDialogueIndex, CurrentNodeRecordId);
        if (matchedNode == CurrentNode && matchedDialogueIndex > CurrentDialogueIndex)
        {
            record = null;
        }

        return new LocateResult
        {
            Ok = true,
            NodeName = matchedNode.Name,
            DialogueIndex = matchedDialogueIndex,
            NodeRecordId = record?.Id ?? NodeRecord.NoId,
            Reached = record != null
        };
    }

    /// <summary>
    /// Dev-only hot reload, mirroring Nova1's ReloadScriptsHelper (R key): re-parse the current
    /// scenario files from disk and resume at roughly the same position. ScriptLoader.OnEnter() is
    /// itself idempotent (Unfreeze/Clear/rebuild the FlowChartGraph in place, same object instance -
    /// see its own doc comment), so calling it again outside NovaController's normal once-only init
    /// flow is safe; MoveTo then replays the recorded path on the freshly rebuilt graph, the same
    /// mechanism LoadGame/Backlog-jump already use, re-resolving every node by name rather than
    /// reusing now-stale FlowChartNode references from before the reload.
    /// </summary>
    public void ReloadScripts()
    {
        var nodeRecordId = CurrentNodeRecordId;
        var dialogueIndex = _currentDialogueIndex;

        NovaController.Instance.GetObj<ScriptLoader>().OnEnter();

        if (nodeRecordId != NodeRecord.NoId)
        {
            MoveTo(nodeRecordId, dialogueIndex);
        }
    }
#endif

    /// <summary>
    /// Jump directly to a previously-reached (node record, dialogue index) position by replaying the
    /// script from the root - the same mechanism LoadGame uses for a bookmark, just addressed by node
    /// record id directly instead of going through a saved Bookmark file. Used by BacklogViewController's
    /// "jump back" action.
    /// </summary>
    public void MoveTo(int nodeRecordId, int dialogueIndex)
    {
        var path = SaveManager.Instance.GetPathTo(nodeRecordId);
        if (path.Count == 0)
        {
            Utils.Warn($"Cannot move to node record {nodeRecordId}: unknown node record.");
            return;
        }

        ResetGameState();
        StateManager.Instance.ResetToBaseline();
        _state = State.Restoring;
        RestoreStarts.Invoke(true);
        GameStarted.Invoke();
        StartCoroutine(token => RestorePath(path, dialogueIndex, token));
    }

    /// <summary>
    /// Replay the script along a previously recorded path down to the target dialogue index, instead of
    /// deserializing a state snapshot (see SaveManager's class doc). Bypasses branch selection UI
    /// entirely: which node comes next at a Branching node is already known from the recorded path, so
    /// this never calls DoBranch and never blocks on the fence.
    /// </summary>
    private async Task RestorePath(IReadOnlyList<NodeRecord> path, int targetDialogueIndex, CancellationToken token)
    {
        try
        {
            for (var i = 0; i < path.Count; i++)
            {
                var record = path[i];
                var node = _flowChartGraph.GetNode(record.Name) ??
                    throw new ScriptLoadingException($"Restore failed: unknown node {record.Name}");

                ScriptLoader.AddDeferredDialogueChunks(node);
                _nodeRecord = record;
                CurrentNode = node;
                NodeChanged.Invoke(new() { NewNode = node.Name });

                var isLastNode = i == path.Count - 1;
                var endIndex = isLastNode ? targetDialogueIndex : record.EndDialogue - 1;
                for (_currentDialogueIndex = 0; _currentDialogueIndex <= endIndex; _currentDialogueIndex++)
                {
                    _currentDialogueEntry = node.GetDialogueEntryAt(_currentDialogueIndex);
                    await UpdateDialogue(_currentDialogueIndex == 0, true, false, token);
                }
            }

            // The inner for loop's own increment leaves _currentDialogueIndex at targetDialogueIndex + 1
            // (one past the entry it just displayed), since the loop condition is re-checked - and fails -
            // *after* incrementing past the last iteration where _currentDialogueIndex == targetDialogueIndex.
            // Step() assumes _currentDialogueIndex is always the index of the *currently displayed* entry
            // (it does ++_currentDialogueIndex itself before showing the next one), so leaving it one too
            // far ahead here made the first click after a load skip straight past the next entry.
            _currentDialogueIndex = targetDialogueIndex;

            _state = State.Normal;
            RestoreStarts.Invoke(false);
        }
        catch (Exception e)
        {
            Utils.Warn($"Restore failed: {e}");
            _state = State.Ended;
            RestoreStarts.Invoke(false);
        }
    }

    #endregion

    public static GameState Instance => NovaController.Instance.GetObj<GameState>();
}
