using System.Collections.Generic;
using Godot;

namespace Nova;

public partial class DialogueTextController : Control
{
    [Export]
    private PackedScene _entryFactory;
    private NodePool<DialogueEntryController> _pool;

    private readonly List<DialogueEntryController> _entries = [];
    public IReadOnlyList<DialogueEntryController> Entries => _entries;
    public int EntryCount => _entries.Count;

    public override void _EnterTree()
    {
        _pool = new(_entryFactory);
    }

    public void Clear()
    {
        foreach (var entry in _entries)
        {
            _pool.Put(entry, this);
        }
        _entries.Clear();
    }

    public DialogueEntryController AddEntry(DialogueDisplayData displayData, Color textColor,
        string alignment, bool outline, TextAppearSettings appear)
    {
        var entry = _pool.Get(this, e => e.Init(displayData, textColor, alignment, outline, appear));
        _entries.Add(entry);
        return entry;
    }

    public void UpdateColor(Color color)
    {
        foreach (var entry in _entries)
        {
            entry.TextColor = color;
        }
    }

    public void UpdateAlignment(string alignment)
    {
        foreach (var entry in _entries)
        {
            entry.Alignment = alignment;
        }
    }

    public void UpdateOutline(bool outline)
    {
        foreach (var entry in _entries)
        {
            entry.Outline = outline;
        }
    }

    private DialogueEntryController LastEntry => _entries.Count == 0 ? null : _entries[^1];

    public bool IsRevealing => LastEntry?.IsRevealing ?? false;

    public void CompleteReveal()
    {
        LastEntry?.CompleteReveal();
    }
}
