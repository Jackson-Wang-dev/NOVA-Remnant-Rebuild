using Godot;

namespace Nova;

/// <summary>
/// A 0-duration step that invokes a callback when its turn comes in the tree - the previously-skipped
/// Nova1 ActionAnimationProperty (see porting-guide.md M2 decision log: "没有用例，不做"). Now needed by
/// the Colorless port's `:action(...)` chaining (e.g. `anim:wait_all(anim_hold):action(show, cg, ...)`).
/// Callback is a Godot Callable (not a C# Action) - callers are GDScript `func(): ...` literals
/// crossing the GDScript/C# boundary, and Callable is the Variant-marshallable type for that direction
/// (same as how Tween.TweenCallback already takes a Callable elsewhere in this codebase).
///
/// CAVEAT (found 2026-06-21, see porting-guide.md decision log): on this project's pinned Godot
/// 4.6.3 mono, a GDScript Callable passed as a parameter into *any* C# method - this constructor's
/// `Callback` included - arrives with empty Method/Target/Delegate, regardless of whether it's an
/// anonymous lambda or `Callable(SomeClass, "method").bind(...)`. tween.TweenCallback(Callback) above
/// silently no-ops; nothing throws. trans()/trans2() (graphics.gd) no longer route their cleanup
/// through AnimationEntry.Action() because of this - they use a GDScript-side SceneTreeTimer instead.
/// animation.gd's wait_all/loop and audio.gd's fade_in/fade_out still call .Action(...) and are
/// therefore still silently broken the same way; fix them the same way if/when they're exercised.
/// </summary>
public class ActionAnimation : IAnimation
{
    public Callable Callback { get; init; }
    public double Duration => 0;

    public bool Execute(Tween tween)
    {
        tween.TweenCallback(Callback);
        return true;
    }
}
