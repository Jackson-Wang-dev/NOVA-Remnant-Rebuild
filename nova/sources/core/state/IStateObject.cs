namespace Nova;

public interface IStateObject
{
    /// <summary>
    /// Sync with normal
    /// </summary>
    void Sync();
    /// <summary>
    /// Sync during fastward.
    /// </summary>
    void SyncImmediate();
    /// <summary>
    /// Sync during restoration.
    /// </summary>
    void SyncBackend();
    /// <summary>
    /// Reset to the value held before the game ever touched this state, undoing anything that drifted
    /// outside of what a restore replay will re-apply. Called once before a restore replay starts.
    /// </summary>
    void ResetToBaseline() { }
}
