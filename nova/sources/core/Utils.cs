using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Nova;

public static class Utils
{
	public static FileAccess OpenFile(string path, FileAccess.ModeFlags mode)
	{
		var fs = FileAccess.Open(path, mode);
		if (fs == null)
		{
			var err = FileAccess.GetOpenError();
			var msg = $"Open {path} failed: {err}";
			throw new SystemException(msg);
		}
		return fs;
	}

	public static string GetFileAsText(string path)
	{
		using var fs = OpenFile(path, FileAccess.ModeFlags.Read);
		return fs.GetAsText();
	}

	public static SceneTree CurrentSceneTree => Engine.GetMainLoop() as SceneTree;

	public static void Quit()
	{
		CurrentSceneTree.Quit();
	}

	public static void RuntimeAssert(bool cond, string msg)
	{
		if (!cond)
		{
			GD.PrintErr(msg);
			throw new ApplicationException($"Assert failed: {msg}");
		}
	}

	public static void Warn(string msg)
	{
		GD.PrintErr($"Nova: {msg}");
		GD.PushWarning(msg);
	}

	// Knuth's golden ratio multiplicative hashing
	public static ulong HashList(IEnumerable<ulong> hashes)
	{
		var r = 0UL;
		unchecked
		{
			foreach (var x in hashes)
			{
				r += x;
				r *= 11400714819323199563UL;
			}
		}

		return r;
	}

	public static ulong HashAdd(ulong x, ulong y)
	{
		unchecked
		{
			return x * 11400714819323199563UL + y;
		}
	}

	// Deterministic, content-based string hash - char values only, never .NET's string.GetHashCode().
	// .NET randomizes string.GetHashCode() with a per-process seed by design (hash-flood DoS
	// resistance), so two separate launches of the same process hash the same string differently. Any
	// hash that gets persisted to disk and compared against a freshly-computed value in a later
	// process run (e.g. DialogueEntry.TextHash, stored via SaveManager's reached-dialogue records and
	// looked up again by ParseDialogueEntry's MemoryTable registration on the next launch) must use
	// this instead, or the comparison silently breaks every time the game restarts.
	public static ulong HashString(string s)
	{
		var r = 0UL;
		foreach (var c in s)
		{
			r = HashAdd(r, c);
		}
		return r;
	}

	private static ulong HashValue(object value)
	{
		return value switch
		{
			string s => HashString(s),
			KeyValuePair<string, string> kv => HashAdd(HashString(kv.Key), HashString(kv.Value ?? "")),
			// Enums/other value types box to a stable GetHashCode (backed by their underlying primitive,
			// e.g. int - only string-based hashing is randomized), safe to use directly.
			_ => (ulong)value.GetHashCode()
		};
	}

	// Used for content hashes that must stay stable across process restarts (script chunk/node text
	// hashes - see HashString's note). list items are typically BlockType/string/KeyValuePair<string,
	// string> boxed as object (see ParsedBlock.ToList()).
	public static ulong HashList<T>(IEnumerable<T> list) where T : class
	{
		return HashList(list.Select(HashValue));
	}

	public static SignalAwaiter WaitForSeconds(this Node node, double seconds)
	{
		var tree = node.GetTree();
		var timer = tree.CreateTimer(seconds);
		return node.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
	}

	public static SignalAwaiter WaitForSeconds(double seconds)
	{
		return WaitForSeconds(NovaController.Instance, seconds);
	}
}
