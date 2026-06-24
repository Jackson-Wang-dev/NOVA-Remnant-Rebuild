namespace Nova;

/// <summary>
/// A node in the checkpoint tree. Each record represents one visit to a flow chart node with a specific
/// variable state (<see cref="VariablesHash"/>). Records form a tree via <see cref="ParentId"/>/
/// <see cref="ChildId"/>/<see cref="SiblingId"/> so that multiple playthroughs sharing the same prefix
/// reuse the same records, mirroring Nova1's CheckpointManager.NodeRecord but addressed by an in-memory
/// integer id instead of a byte offset into a hand-rolled block file.
/// </summary>
public class NodeRecord
{
    public const int NoId = 0;

    public int Id { get; init; }
    public string Name { get; init; }
    public ulong VariablesHash { get; init; }

    /// <summary>
    /// Dialogue index at which this record started.
    /// </summary>
    public int BeginDialogue { get; init; }

    /// <summary>
    /// One past the last dialogue index that has been reached under this record.
    /// </summary>
    public int EndDialogue { get; set; }

    public int ParentId { get; set; } = NoId;
    public int ChildId { get; set; } = NoId;
    public int SiblingId { get; set; } = NoId;

    public NodeRecord() { }

    public NodeRecord(int id, string name, int beginDialogue, ulong variablesHash)
    {
        Id = id;
        Name = name;
        VariablesHash = variablesHash;
        BeginDialogue = beginDialogue;
        EndDialogue = beginDialogue;
    }
}
