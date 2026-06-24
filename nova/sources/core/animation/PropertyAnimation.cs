using System.Numerics;
using System.Runtime.InteropServices;
using Godot;

namespace Nova;

public class PropertyAnimation<[MustBeVariant] T> : IAnimation
{
    public PropertyState Object { get; init; }
    public StringName Property { get; init; }
    public T To { get; init; }
    public double Duration { get; init; }
    public bool Relative { get; init; }

    private Variant _fromAbsolute;
    private Variant _toAbsolute;
    private bool _released;

    public void Init()
    {
        var fromT = Object.Get(Property).As<T>();
        var toT = To;
        if (Relative)
        {
            toT += (dynamic)fromT;
        }
        _fromAbsolute = Variant.From(fromT);
        _toAbsolute = Variant.From(toT);
        Object.Hold(Property);
        Object.Set(Property, _toAbsolute);
    }

    public bool Execute(Tween tween)
    {
        var tweener = tween.TweenProperty(Object.Binding, Property.ToString(),
            _toAbsolute, Duration);
        tweener.From(_fromAbsolute);
        return true;
    }

    // Idempotent: called from AnimationExecutor.OnFinishEntry on a natural Tween finish AND from
    // AnimationEntry.ClearChildren's discard sweep (which also walks already-finished entries) -
    // without the guard, a property held by two still-overlapping animations (e.g. anim_hold's whole
    // tree being torn down after this entry already finished on its own) would get double-released,
    // dropping a sibling's still-legitimate hold on the same key one decrement early.
    public void Finish()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        Object.Release(Property);
    }
}
