using System.Collections.Generic;
using Godot;

namespace Nova;

public partial class AnimationState : RefCounted, IStateObject
{
    public readonly AnimationEntry Root;
    public readonly Event OnFinish = new();

    private readonly List<AnimationEntry> _animations = [];
    private readonly AnimationExecutor _executor;

    public bool IsRunning => _animations.Count > 0;

    // Debug-only label ("anim"/"anim_hold") so AnimationExecutor's diagnostics can say which track a
    // double-enqueue warning came from - StateManager owns two independent AnimationState instances and
    // the bare warning gave no way to tell them apart.
    public AnimationState(string debugName = "?")
    {
        _executor = new AnimationExecutor(debugName);
        Root = AnimationEntry.Root(this);
        _executor.OnFinish.Subscribe(Finish);
    }

    private void Finish()
    {
        // Root.Children is empty exactly when nothing was ever queued on this track (e.g.
        // HoldAnimation's 0-duration Root delay completing on its own, every dialogue step, since
        // anim_hold sees no use yet) - a trivial completion, not a real one. Only broadcast OnFinish
        // for a real completion: it drives StateManager.SyncImmediate(), which loops *every* tracked
        // PropertyState. This guard only covers the *other* track being fully idle that step - it does
        // nothing the (much more common) case of *both* tracks having real, simultaneously-finishing
        // work, e.g. anim_hold running a multi-second vfx() ramp while anim's own short per-dialogue-step
        // animations finish on every click in between. That case used to still reach
        // PropertyState.SyncImmediate's old unconditional "_holdingProperties.Clear()", which forced
        // every animated property - including the unrelated, still-running anim_hold one - straight to
        // its final value early; confirmed via timestamped logs showing position/scale/rotation snapping
        // to their final values ~3ms into a 500ms Tween, then the Tween catching back up - the "flash to
        // end state, then animates normally" symptom reported for every o.anim-driven property
        // (move/tint/vfx/standing sprites alike). Now fixed at the source instead (PropertyState's
        // reference-counted Hold()/Release(), released per-PropertyAnimation via
        // AnimationExecutor/AnimationEntry.ClearChildren rather than blanket-cleared here), so this
        // hadWork guard is now only an optimization (skip a no-op broadcast), not load-bearing for
        // correctness.
        var hadWork = Root.Children.Count > 0;
        _animations.Clear();
        Root.ClearChildren();
        if (hadWork)
        {
            OnFinish.Invoke();
        }
    }

    public void Add(AnimationEntry entry)
    {
        _animations.Add(entry);
    }

    public void Stop()
    {
        _executor.Stop();
    }

    public void Play()
    {
        _executor.EnqueueAnimation(Root);
    }

    public void Sync()
    {
        Play();
    }

    public void SyncImmediate()
    {
        // PropertyAnimation<T>.Init() already wrote the final target value into the bound
        // PropertyState eagerly when the script called o.anim.PropertyXxx(...), regardless of
        // whether Play() ever runs. So during restore (which never calls Play()), the only thing
        // left to do is drop the bookkeeping that would otherwise make IsRunning stick at true
        // forever; the subsequent PropertyState.SyncImmediate() in the same StateManager pass
        // flushes the held value and lets the already-final dirty value reach the real node.
        //
        // Guarded on the executor actually being idle: StateManager.SyncImmediate() loops over every
        // tracked IStateObject, including *both* AnimationState instances (Animation/HoldAnimation),
        // whenever *either one's* OnFinish fires - so this can run as a side effect of the other,
        // unrelated track completing (e.g. HoldAnimation finishing instantly because nothing was ever
        // queued on it). Without this guard that wrongly wipes Root.Children/_animations out from
        // under a genuinely still-running chain on *this* track, making IsRunning go false while its
        // Tween is still live - GameViewController.Step() then advances instead of stopping it, and
        // the orphaned Tween keeps animating/clears its VfxManager state in the background, racing the
        // next dialogue step's own trans()/vfx() call on the same cached material.
        if (_executor.IsIdle)
        {
            _animations.Clear();
            Root.ClearChildren();
        }
    }

    public void SyncBackend() { }

    public void ResetToBaseline()
    {
        Stop();
    }
}
