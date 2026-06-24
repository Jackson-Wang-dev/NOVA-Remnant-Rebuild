using System.Collections.Generic;
using Godot;

namespace Nova;

public class AnimationExecutor
{
    private readonly string _debugName;
    private readonly HashSet<AnimationEntry> _runningPool = [];
    private bool _isStopping = false;

    public readonly Event OnFinish = new();

    public AnimationExecutor(string debugName)
    {
        _debugName = debugName;
    }

    // Used by AnimationState.SyncImmediate() to tell "I am genuinely idle" apart from "some other,
    // unrelated AnimationState's OnFinish fired and is forcing a blanket flush" - see that method.
    public bool IsIdle => _runningPool.Count == 0;

    private void OnFinishEntry(AnimationEntry entry, bool result)
    {
        if (_isStopping)
        {
            return;
        }
        _runningPool.Remove(entry);
        entry.Tween = null;
        entry.Animation.Finish();
        if (result)
        {
            for (var i = entry.NextUnstartedChildIndex; i < entry.Children.Count; i++)
            {
                EnqueueAnimation(entry.Children[i]);
            }
            entry.NextUnstartedChildIndex = entry.Children.Count;
        }
        if (_runningPool.Count <= 0)
        {
            OnFinish.Invoke();
        }
    }

    public void EnqueueAnimation(AnimationEntry entry)
    {
        if (_runningPool.Contains(entry))
        {
            // Benign for Root (see AnimationEntry.IsRoot) - re-entrant Play() calls before its
            // in-flight Tween reports Finished. Anything else hitting this is a real double-enqueue.
            if (!entry.IsRoot)
            {
                Utils.Warn(
                    $"Animation already playing: track={_debugName} " +
                    $"animation={entry.Animation.GetType().Name} children={entry.Children.Count} " +
                    $"poolSize={_runningPool.Count}"
                );
            }
            return;
        }
        _runningPool.Add(entry);
        var tween = Utils.CurrentSceneTree.CreateTween();
        entry.Tween = tween;
        var result = entry.Animation.Execute(tween);
        tween.Finished += () => OnFinishEntry(entry, result);
    }

    public void Stop()
    {
        _isStopping = true;
        foreach (var entry in _runningPool)
        {
            entry.Tween?.Kill();
            entry.Tween = null;
            entry.Animation.Finish();
            RunPendingActions(entry);
        }
        if (_runningPool.Count > 0)
        {
            OnFinish.Invoke();
        }
        _runningPool.Clear();
        _isStopping = false;
    }

    // A forced Stop() correctly abandons whatever animated step was cut short, along with everything
    // queued under it - but a trailing zero-duration Action (e.g. trans()'s clear_callback, which
    // detaches a shader material/VfxLayer so the screen doesn't stay stuck mid-transition) is cleanup
    // that must still run whether the chain finished naturally or got interrupted; OnFinishEntry never
    // reaches it otherwise, since Stop() never enqueues the remaining children. Walks only through
    // Action children, recursively (covers a chain of cleanup Actions), and stops at the first real
    // animated child - that one (and anything under it) stays correctly skipped.
    private static void RunPendingActions(AnimationEntry entry)
    {
        foreach (var child in entry.Children)
        {
            if (child.Animation is ActionAnimation action)
            {
                action.Callback.Call();
                RunPendingActions(child);
            }
        }
    }
}
