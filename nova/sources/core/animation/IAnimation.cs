using Godot;

namespace Nova;

public interface IAnimation
{
    /// <summary>
    /// Called when animation entry is created.
    /// </summary>
    void Init() { }
    /// <returns>Whether children should be executed.</returns>
    bool Execute(Tween tween);
    /// <summary>
    /// Called exactly once per entry, whenever this step stops being live - natural Tween completion
    /// (AnimationExecutor.OnFinishEntry), a forced Stop() while running, or being discarded unstarted
    /// (AnimationEntry.ClearChildren). Lets PropertyAnimation release the PropertyState.Hold() it took
    /// in Init() - see PropertyAnimation.Finish() for why this can't just ride on the existing
    /// AnimationState.OnFinish broadcast (StateManager.SyncImmediate's cross-track stomping bug).
    /// </summary>
    void Finish() { }
    /// <summary>
    /// How long this single step takes, not counting any children - used by AnimationEntry.TotalDuration
    /// (e.g. wait_all's "how long until anim_hold's queued animations are done").
    /// </summary>
    double Duration { get; }
}
