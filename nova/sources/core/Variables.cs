using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace Nova;

/// <summary>
/// Local (<c>v_</c>) and global (<c>gv_</c>) script variables, mirroring Nova1's Variables.cs +
/// CheckpointHelper split, minus the Lua metatable trick (GDScript routes <c>self.v_x</c>/<c>self.gv_x</c>
/// here via RuntimeBlock's <c>_get</c>/<c>_set</c> overrides instead, see runtime_block.gd).
///
/// Local variables live purely in memory and are never serialized: nova2 loads a save by replaying the
/// script from the root (see SaveManager/GameState), so replaying the same Default actions naturally
/// reconstructs the same local variable state - unlike Nova1's GameStateCheckpoint.variables snapshot.
/// Global variables must survive ResetGameState, so they're persisted via GlobalSave.Data through
/// SaveManager instead.
/// </summary>
public partial class Variables : RefCounted, ISingleton
{
    public void OnEnter()
    {
        ObjectManager.Instance.BindObject("variables", this);
    }

    public void OnReady() { }

    public void OnExit() { }

    private readonly SortedDictionary<string, Variant> _local = [];
    private ulong? _hash;

    public Variant Get(string name)
    {
        if (name.StartsWith("gv_"))
        {
            return FromStorable(SaveManager.Instance.GetGlobalVariable(name));
        }

        return _local.GetValueOrDefault(name);
    }

    public void Set(string name, Variant value)
    {
        if (name.StartsWith("gv_"))
        {
            SaveManager.Instance.SetGlobalVariable(name, ToStorable(value));
            return;
        }

        if (value.VariantType == Variant.Type.Nil)
        {
            if (_local.Remove(name))
            {
                _hash = null;
            }
        }
        else if (!_local.TryGetValue(name, out var old) || !old.Equals(value))
        {
            _local[name] = value;
            _hash = null;
        }
    }

    public void ClearLocal()
    {
        _local.Clear();
        _hash = null;
    }

    public ulong Hash => _hash ??= ComputeHash();

    private ulong ComputeHash()
    {
        var s = string.Join(", ", _local.Select(pair => $"{pair.Key}={Format(pair.Value)}"));
        return Utils.HashString(s);
    }

    private static string Format(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Float => value.AsDouble().ToString(CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static object ToStorable(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Nil => null,
            Variant.Type.Bool => value.AsBool(),
            Variant.Type.Int => value.AsDouble(),
            Variant.Type.Float => value.AsDouble(),
            Variant.Type.String => value.AsString(),
            _ => throw new System.ArgumentException(
                $"Nova: variable can only be bool, number, string, or null, but found {value.VariantType}: {value}")
        };
    }

    private static Variant FromStorable(object value)
    {
        return value switch
        {
            null => default,
            bool b => Variant.From(b),
            long l => Variant.From((double)l),
            double d => Variant.From(d),
            string s => Variant.From(s),
            _ => throw new System.ArgumentException($"Nova: unexpected stored variable type {value.GetType()}")
        };
    }

    public static Variables Instance => NovaController.Instance.GetObj<Variables>();
}
