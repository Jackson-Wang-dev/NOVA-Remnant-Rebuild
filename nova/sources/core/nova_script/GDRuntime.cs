using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Nova;

public static class GDRuntime
{
    private const string RuntimeBlockScript = "res://nova/sources/gdscript/runtime_block.gd";
    private static readonly Dictionary<string, RefCounted> s_cachedScript = [];

    private static RefCounted GetScript(string path)
    {
        if (!s_cachedScript.TryGetValue(path, out var script))
        {
            script = ResourceLoader.Load<GDScript>(path).New().As<RefCounted>();
            s_cachedScript.Add(path, script);
        }
        return script;
    }

    public static RefCounted BaseRuntimeBlock => GetScript(RuntimeBlockScript);

    private static RefCounted Compile(string script)
    {
        var gdScript = new GDScript { SourceCode = script };
        gdScript.Reload();
        return gdScript.New().As<RefCounted>();
    }

    // Matches, in priority order: a quoted string (left untouched - avoids rewriting a v_/gv_-looking
    // substring inside a string literal, e.g. an asset path, since this operates on raw script text
    // rather than a real AST); a v_/gv_ identifier immediately followed by a simple "=" (an assignment
    // target, e.g. "v_flag = 1" - rewritten to a dot-assignment "self.v_flag = 1", which GDScript routes
    // through RuntimeBlock's _set with no ambiguity, see base_block.gd/condition_block.gd); any other
    // v_/gv_ identifier (a read - rewritten to self.get("v_flag") rather than bare "self.v_flag", since
    // GDScript's dot-read syntax treats a bare-null _get return as "property does not exist" and raises
    // a hard runtime error, whereas Object.get() safely returns null for an unset variable like Lua's
    // nil). The negative lookahead "(?!=)" on the assignment case excludes "==" (equality, a read).
    private static readonly Regex s_variableToken = new(
        "\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'|" +
        "\\b(?<write>g?v_\\w+)\\b(?=\\s*=(?!=))|\\b(?<read>g?v_\\w+)\\b", RegexOptions.Compiled);

    private static string RewriteVariables(string script)
    {
        return s_variableToken.Replace(script, m =>
        {
            if (m.Groups["write"].Success)
            {
                return $"self.{m.Groups["write"].Value}";
            }
            if (m.Groups["read"].Success)
            {
                return $"self.get(\"{m.Groups["read"].Value}\")";
            }
            return m.Value;
        });
    }

    private static string WrapStatements(string baseClass, string script)
    {
        script = string.IsNullOrWhiteSpace(script) ? "" : RewriteVariables(script).Trim().Replace("\n", "\n    ");
        return $"extends {baseClass}\nfunc __eval():\n    pass\n    {script}\n";
    }

    private static string WrapExpression(string baseClass, string script)
    {
        return $"extends {baseClass}\nfunc __eval():\n    return {RewriteVariables(script.Trim())}\n";
    }

    public static RefCounted CompileBaseBlock(string script)
    {
        return Compile(WrapStatements("BaseBlock", script));
    }

    public static RefCounted CompileCondition(string expression)
    {
        return Compile(WrapExpression("ConditionBlock", expression));
    }

    public static bool InvokeCondition(RefCounted script)
    {
        return script?.Call("run").AsBool() ?? true;
    }
}
