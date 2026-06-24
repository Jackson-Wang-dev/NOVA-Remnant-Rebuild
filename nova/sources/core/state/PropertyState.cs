using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Nova;

public partial class PropertyState : RefCounted, IStateObject
{
    public readonly GodotObject Binding;

    private readonly Dictionary<StringName, Variant> _properties = [];
    private readonly HashSet<StringName> _dirtyProperties = [];
    private readonly Dictionary<StringName, Variant> _holdingProperties = [];
    // How many still-live PropertyAnimations currently have this key held - see Hold()/Release().
    private readonly Dictionary<StringName, int> _holdCount = [];
    private readonly Godot.Collections.Array<Godot.Collections.Dictionary> _propertyList;
    private readonly HashSet<StringName> _propertyNames;

    // The value Binding had the first time each property was ever touched, i.e. before any script
    // ever wrote to it. Restore (see StateManager.ResetToBaseline / GameState.LoadGame) needs this:
    // RestorePath only re-applies whatever is on the replayed path, it never undoes mutations that
    // happened in an abandoned future after the save point, so without resetting to this baseline
    // first, properties touched only after the save point would keep leaking their stale live value
    // across a load.
    private readonly Dictionary<StringName, Variant> _baseline = [];

    private void EnsureBaseline(StringName key)
    {
        if (!_baseline.ContainsKey(key))
        {
            _baseline[key] = Binding.Get(key);
        }
    }

    public PropertyState(GodotObject binding)
    {
        Binding = binding;
        _propertyList = binding.GetPropertyList();
        _propertyNames = _propertyList.Select(
            entry => entry["name"].AsStringName()).ToHashSet();
    }

    private void AddProperty(StringName key, Variant value)
    {
        EnsureBaseline(key);
        if (!_propertyNames.Contains(key))
        {
            var entry = new Godot.Collections.Dictionary()
            {
                ["name"] = key,
                ["type"] = (int)value.VariantType,
                ["usage"] = (int)PropertyUsageFlags.NoEditor,
                ["hint"] = (int)PropertyHint.None,
                ["hint_string"] = "",
            };
            _propertyList.Add(entry);
            _propertyNames.Add(key);
        }
        _properties.Add(key, value);
        _dirtyProperties.Add(key);
    }

    public Variant this[StringName key] { init => AddProperty(key, value); }

    public List<StringName> InitProperties
    {
        init
        {
            foreach (var key in value)
            {
                AddProperty(key, Binding.Get(key));
            }
        }
    }

    public void Sync()
    {
        var refreshed = new List<StringName>();
        foreach (var key in _dirtyProperties)
        {
            if (!_holdingProperties.TryGetValue(key, out var value))
            {
                value = _properties[key];
                refreshed.Add(key);
            }
            Binding.Set(key, value);
        }
        _dirtyProperties.ExceptWith(refreshed);
    }

    // Used to be an unconditional "_holdingProperties.Clear(); Sync();" here, relied on by
    // StateManager.SyncImmediate to force every animated property to its final value. That's correct
    // when called for a genuinely-finished/force-stopped track, but StateManager.SyncImmediate also
    // fires whenever *either* of the two independent AnimationState tracks (anim/anim_hold) finishes -
    // including a trivial, instant completion on one track while the *other* track still has a
    // multi-second animation legitimately holding this exact same PropertyState. Blanket-clearing here
    // forced that still-running animation's property to its end value early (confirmed: a 10s
    // anim_hold-driven vfx() ramp got snapped to its target shader value every time an unrelated
    // per-dialogue "anim" step finished in between, see ch4.txt's rain reveal). Holds are now released
    // precisely instead, one PropertyAnimation at a time, via Hold()/Release() - see
    // PropertyAnimation.Finish() and AnimationEntry.ClearChildren(). By the time this runs (StateManager
    // iterates the two AnimationStates before any PropertyState - see StateManager.SyncImmediate), a
    // hold genuinely belonging to a now-finished/idle track has already been released; a hold still
    // backed by a live Tween on the other, still-running track has not, and Sync() below correctly
    // leaves it alone.
    public void SyncImmediate()
    {
        Sync();
    }

    public void SyncBackend() { }

    /// <summary>
    /// Reset Binding back to the value it had before any script ever touched these properties, and
    /// drop all pending/holding state. Called before a restore replay starts (see GameState.LoadGame)
    /// so properties that were only ever mutated after the save point don't leak into the loaded game.
    /// </summary>
    public void ResetToBaseline()
    {
        foreach (var (key, value) in _baseline)
        {
            Binding.Set(key, value);
        }
        _properties.Clear();
        _dirtyProperties.Clear();
        _holdingProperties.Clear();
        _holdCount.Clear();
    }

    public void Hold(StringName key)
    {
        var value = Get(key);
        // Indexer, not Add() - Add() throws if key is already held. A second Hold() on the same
        // key before the first was released (e.g. two PropertyAnimations queued back-to-back on
        // the same property within one animation tree) just means the held value should advance
        // to whatever's most current; it's not a logic error worth crashing over.
        _holdingProperties[key] = value;
        _holdCount[key] = _holdCount.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// Releases one Hold() on key, taken out by exactly one PropertyAnimation (see
    /// PropertyAnimation.Finish()). Reference-counted rather than a flat clear: two PropertyAnimations
    /// can legitimately hold the same key at once (e.g. one on the "anim" track, one on "anim_hold"),
    /// and the first one finishing must not stop the other's still-live Tween from being shielded by
    /// Sync() below. Only once the last holder releases does Sync() resume writing this key's real
    /// (already-final, see PropertyAnimation.Init) value to Binding.
    /// </summary>
    public void Release(StringName key)
    {
        if (!_holdCount.TryGetValue(key, out var count))
        {
            return;
        }
        if (count <= 1)
        {
            _holdCount.Remove(key);
            _holdingProperties.Remove(key);
        }
        else
        {
            _holdCount[key] = count - 1;
        }
    }

    public override Variant _Get(StringName key)
    {
        EnsureBaseline(key);
        if (_properties.TryGetValue(key, out var value))
        {
            return value;
        }
        return Binding.Get(key);
    }

    public override bool _Set(StringName key, Variant value)
    {
        EnsureBaseline(key);
        _properties[key] = value;
        _dirtyProperties.Add(key);
        return true;
    }

    public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
    {
        return _propertyList;
    }
}
