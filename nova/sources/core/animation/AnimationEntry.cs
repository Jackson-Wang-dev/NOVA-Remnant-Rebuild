using System.Collections.Generic;
using Godot;

namespace Nova;

public partial class AnimationEntry : RefCounted
{
    private readonly AnimationState _animationState;
    public readonly IAnimation Animation;
    public readonly List<AnimationEntry> Children = [];
    public Tween Tween = null;

    // Set only by Root() below. AnimationExecutor.EnqueueAnimation uses this to tell apart a real
    // double-enqueue bug from Root's expected re-entrancy: StateManager.Sync() calls Play() (which
    // re-enqueues Root) on every dialogue step, but Root's own zero-duration Tween only reports
    // Finished on a later frame - so a second dialogue step landing before that signal fires is
    // normal, not an error, and whatever children it just added are still picked up once the
    // in-flight Tween's Finished callback reads entry.Children.
    public readonly bool IsRoot;

    // How many of Children have already been handed to AnimationExecutor.EnqueueAnimation at least
    // once. Root gets re-enqueued on every dialogue step (see above), and each round's Finished walks
    // Children again to pick up whatever got added since - without this cursor that walk would also
    // re-enqueue children from earlier rounds that are still mid-flight (false "already playing") or
    // already finished (silently replayed from scratch instead of being left alone). Lives on the
    // entry rather than the executor since every entry's Children can be walked this way, not just Root's.
    public int NextUnstartedChildIndex = 0;

    private AnimationEntry(AnimationState animationState, IAnimation animation, bool isRoot = false)
    {
        _animationState = animationState;
        Animation = animation;
        IsRoot = isRoot;
        animation.Init();
    }

    private AnimationEntry Entry(IAnimation animation)
    {
        var entry = new AnimationEntry(_animationState, animation);
        Children.Add(entry);
        _animationState.Add(entry);
        return entry;
    }

    private AnimationEntry Property<[MustBeVariant] T>(PropertyState obj, StringName property, T to,
        double duration, bool relative)
    {
        var animation = new PropertyAnimation<T>()
        {
            Object = obj,
            Property = property,
            To = to,
            Duration = duration,
            Relative = relative,
        };
        return Entry(animation);
    }

    public AnimationEntry PropertyVector2(PropertyState obj, StringName property, Vector2 to,
        double duration, bool relative)
    {
        return Property(obj, property, to, duration, relative);
    }

    public AnimationEntry PropertyVector3(PropertyState obj, StringName property, Vector3 to,
        double duration, bool relative)
    {
        return Property(obj, property, to, duration, relative);
    }

    public AnimationEntry PropertyColor(PropertyState obj, StringName property, Color to,
        double duration, bool relative)
    {
        return Property(obj, property, to, duration, relative);
    }

    public AnimationEntry PropertyDouble(PropertyState obj, StringName property, double to,
        double duration, bool relative)
    {
        return Property(obj, property, to, duration, relative);
    }

    public AnimationEntry Delay(double duration)
    {
        return Entry(new DelayAnimation { Duration = duration });
    }

    public AnimationEntry Action(Callable callback)
    {
        return Entry(new ActionAnimation { Callback = callback });
    }

    /// <summary>
    /// How long until this entry and every entry still queued under it are done - own step's duration
    /// plus the slowest child branch (children of one entry run in parallel once it finishes, see
    /// AnimationExecutor.OnFinishEntry), recursively. Mirrors Nova1's AnimationEntry.totalDuration.
    /// Used by the Colorless port's `wait_all` (wait for everything still queued on anim_hold).
    /// </summary>
    public double TotalDuration
    {
        get
        {
            var childrenMax = 0.0;
            foreach (var child in Children)
            {
                var d = child.TotalDuration;
                if (d > childrenMax)
                {
                    childrenMax = d;
                }
            }
            return Animation.Duration + childrenMax;
        }
    }

    /// <summary>
    /// Stop the whole AnimationState this entry belongs to - exposed on the entry (rather than only on
    /// AnimationState itself) because GDScript only ever holds a Root AnimationEntry reference (the "o"
    /// shortcut binds e.g. "anim_hold" to AnimationState.Root, not the AnimationState). Used by the
    /// Colorless port's anim_hold_begin/anim_hold_end and wait_all's stop-when-done callback, mirroring
    /// Nova1's `anim_hold:stop()`.
    /// </summary>
    public void Stop() => _animationState.Stop();

    // Children.Clear() alone would leave NextUnstartedChildIndex pointing past the now-empty list,
    // so AnimationExecutor.OnFinishEntry's next walk (NextUnstartedChildIndex..Children.Count) would
    // skip every child freshly queued after the reset. Always clear both together.
    //
    // Also walks every descendant (recursively, since a discarded child may itself have queued
    // grandchildren) calling Animation.Finish() before dropping them - a child queued via
    // entry.PropertyDouble(...) already took its PropertyState.Hold() eagerly in Init() the moment the
    // script called it, regardless of whether it ever got a Tween (AnimationExecutor.EnqueueAnimation
    // is what would normally trigger that release via OnFinishEntry). Without this, a child sitting
    // unstarted past NextUnstartedChildIndex when the whole tree gets torn down (AnimationState.Finish,
    // e.g. anim_hold_begin()/anim_hold_end()'s Stop()) would leak its hold forever, permanently
    // shielding that property from ever being Sync()'d to its real value again. Finish() is idempotent
    // (see PropertyAnimation.Finish), so re-visiting an already-finished child here is harmless.
    public void ClearChildren()
    {
        foreach (var child in Children)
        {
            child.Animation.Finish();
            child.ClearChildren();
        }
        Children.Clear();
        NextUnstartedChildIndex = 0;
    }

    public static AnimationEntry Root(AnimationState state)
    {
        return new AnimationEntry(state, new DelayAnimation { Duration = 0 }, isRoot: true);
    }
}
