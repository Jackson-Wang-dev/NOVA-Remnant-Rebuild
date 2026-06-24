using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Nova;

/// <summary>
/// 回看/History view - Nova1's LogController, renamed BacklogViewController per upstream issue #1's
/// naming guidance. Builds its list purely from live DialogueChanged events (cleared on every
/// GameStarted, which LoadGame/MoveTo also invoke) rather than Nova1's IRestorable
/// snapshot+GetDialogueHistory tree-walk - replaying a load/jump naturally re-fires DialogueChanged for
/// every step along the path, which rebuilds this list for free and correctly drops any since-rewound
/// future entries, matching nova2's "pure script replay" save model (see porting-guide.md).
///
/// Simplified vs. Nova1: no virtualized loop-scroll-rect (Godot has no built-in equivalent and nova2
/// caps the list at MaxEntries instead of reimplementing one) and no first-shown onboarding hint (no
/// generic settings/flag persistence backend exists for it yet) - both logged as decisions.
/// </summary>
public partial class BacklogViewController : ViewController
{
    private const int MaxEntries = 200;

    private readonly struct BacklogEntry(DialogueDisplayData displayData, int nodeRecordId, int dialogueIndex,
        (string VoicePath, double Delay)? voice, ulong textHash)
    {
        public DialogueDisplayData DisplayData { get; } = displayData;
        public int NodeRecordId { get; } = nodeRecordId;
        public int DialogueIndex { get; } = dialogueIndex;
        public (string VoicePath, double Delay)? Voice { get; } = voice;
        public ulong TextHash { get; } = textHash;
    }

    [Export]
    private ScrollContainer _scroll;
    [Export]
    private VBoxContainer _list;
    [Export]
    private PackedScene _entryScene;
    [Export]
    private Button _backButton;

    private GameState _gameState;
    private AutoVoiceController _autoVoice;

    private readonly List<BacklogEntry> _entries = [];
    private readonly List<BacklogEntryController> _rows = [];

    public override void _EnterTree()
    {
        base._EnterTree();

        _gameState = GameState.Instance;
        _autoVoice = StateManager.Instance.AutoVoice;

        // Not driven by I18nText: that script attaches to the node and replaces its C# wrapper type,
        // which breaks the [Export] Button NodePath binding above - see ConfirmDialog for the same note.
        _backButton.Text = I18n.__("help.close");
        _backButton.Pressed += Close;

        _gameState.GameStarted.Subscribe(Clear);
        _gameState.DialogueChanged.Subscribe(OnDialogueChanged);
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        _backButton.Pressed -= Close;
        _gameState.GameStarted.Unsubscribe(Clear);
        _gameState.DialogueChanged.Unsubscribe(OnDialogueChanged);
    }

    public override void ShowPanel(bool doTransition, Action onFinish)
    {
        base.ShowPanel(doTransition, onFinish);
        CallDeferred(nameof(ScrollToBottom));
    }

    // Right-click closes the backlog, mirroring the ring's right-click-to-cancel gesture. _Input
    // (not _GuiInput) so it fires regardless of what's under the cursor, same as GameViewController's
    // Esc handler; Active guards against the panel still being in the tree but hidden.
    public override void _Input(InputEvent @event)
    {
        if (!Active)
        {
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ScrollToBottom()
    {
        _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }

    private void Close()
    {
        this.SwitchView<GameViewController>();
    }

    private void OnDialogueChanged(DialogueChangedData data)
    {
        var displayData = data.DisplayData;
        if (string.IsNullOrEmpty(displayData.FormatNameDialogue()))
        {
            return;
        }

        AddEntry(new BacklogEntry(displayData, _gameState.CurrentNodeRecordId, data.DialogueData.DialogueIndex,
            _autoVoice.LastVoice, data.DialogueData.TextHash));
    }

    private void AddEntry(BacklogEntry entry)
    {
        _entries.Add(entry);

        var row = _entryScene.Instantiate<BacklogEntryController>();
        _list.AddChild(row);
        _rows.Add(row);
        InitRow(row, entry);

        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
            _rows[0].QueueFree();
            _rows.RemoveAt(0);
        }

        RefreshDegradedText();
    }

    /// <summary>Ported from Nova1's LogController.GetDegradedDisplayData (HyBloom fork's "memory
    /// damage in backlog" feature, not upstream Colorless): a dialogue entry tagged with &lt;mmr&gt;/
    /// &lt;dmg&gt; in the script (see DialogueEntryParser/MemoryTable) silently swaps to a blurrier,
    /// then a damaged, variant once enough newer entries have piled up after it - simulating a
    /// character's memory decaying the further back you scroll. distance is "how many newer entries
    /// exist past this one", recomputed fresh every call rather than cached, since it changes every
    /// time a new dialogue line is added.</summary>
    private static string GetDegradedText(BacklogEntry entry, int distance)
    {
        if (!MemoryTable.Variants.TryGetValue(entry.TextHash, out var variant))
        {
            return entry.DisplayData.FormatNameDialogue();
        }

        var rng = new Random((int)(entry.TextHash & 0xFFFFFFFF));
        var mmrAt = rng.Next(10, 13);
        var dmgAt = mmrAt + rng.Next(6, 9);

        string overrideText = distance >= dmgAt && variant.Dmg != null ? variant.Dmg
            : distance >= mmrAt && variant.Mmr != null ? variant.Mmr
            : null;
        if (overrideText == null)
        {
            return entry.DisplayData.FormatNameDialogue();
        }

        var degraded = new DialogueDisplayData
        {
            CharacterName = entry.DisplayData.CharacterName,
            DisplayNames = entry.DisplayData.DisplayNames,
            Dialogues = entry.DisplayData.Dialogues.ToDictionary(kv => kv.Key, _ => overrideText)
        };
        return degraded.FormatNameDialogue();
    }

    /// <summary>Re-derives every visible row's text from its degradation distance. Called after every
    /// AddEntry since the distance (and therefore which rows just crossed their mmr/dmg threshold)
    /// shifts by one for every existing entry whenever a new dialogue line arrives. _entries/_rows are
    /// capped at MaxEntries, so this O(n) full refresh stays cheap.</summary>
    private void RefreshDegradedText()
    {
        var count = _entries.Count;
        for (var i = 0; i < count; i++)
        {
            _rows[i].SetText(GetDegradedText(_entries[i], count - 1 - i));
        }
    }

    private void InitRow(BacklogEntryController row, BacklogEntry entry)
    {
        Action onJump = () => ConfirmDialog.Instance.Show("log.moveback.confirm", () =>
        {
            _gameState.MoveTo(entry.NodeRecordId, entry.DialogueIndex);
            Close();
        });
        Action onPlayVoice = entry.Voice is { } voice
            ? () => _autoVoice.PlayImmediate(voice.VoicePath, voice.Delay)
            : null;
        row.Init(entry.DisplayData.FormatNameDialogue(), onJump, onPlayVoice);
    }

    private void Clear()
    {
        foreach (var row in _rows)
        {
            row.QueueFree();
        }

        _rows.Clear();
        _entries.Clear();
    }
}
