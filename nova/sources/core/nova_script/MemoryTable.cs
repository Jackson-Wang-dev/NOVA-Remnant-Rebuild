using System.Collections.Generic;

namespace Nova;

/// <summary>
/// Maps dialogue text hashes to their memory-degraded variants, ported from Nova1's MemoryTable.
/// Populated at script parse time by DialogueEntryParser (from &lt;mmr&gt;/&lt;dmg&gt; tags), read at
/// backlog display time by BacklogViewController.
/// </summary>
public static class MemoryTable
{
    public readonly record struct MemoryVariant(string Mmr, string Dmg);

    public static readonly Dictionary<ulong, MemoryVariant> Variants = new();

    public static void Clear()
    {
        Variants.Clear();
    }
}
