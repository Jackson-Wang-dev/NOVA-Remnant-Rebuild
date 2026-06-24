# nova2 (Godot 4 / C#)

This is **nova2**, the Godot 4 + C# rewrite of **Nova1** (a Unity visual novel engine). nova2 *is* the upstream `Lunatic-Works/Nova2` repo — check their [issue #1](https://github.com/Lunatic-Works/Nova2/issues/1) roadmap before treating something as a deviation; it may already be upstream's plan.

This folder is unrelated to any Unity project that might be open elsewhere on this machine — don't mix the two up.

**Read `porting-guide.md` first.** It's the living source of truth: porting principles, per-Tier checklists, milestone history, and a decision-log table of every place nova2 intentionally diverges from Nova1. Treat it as required context before touching any subsystem, not optional background reading.

**Use `novascript-reference.md` when writing or reading `.txt` scenarios, or adding/renaming a `#@export` runtime function.** It's the script-author-facing syntax/function reference (node/block syntax, dialogue text grammar, variables, every `#@export` function grouped by module, and the hard constraints on what a GDScript-callable C# method's signature can look like). Keep it in sync whenever `nova/sources/gdscript/runtime/*.gd` gains, renames, or removes a `#@export` function.

## Working rules (condensed from porting-guide.md — read that file for full detail)

1. **Engine-API port, not a language port.** Both sides are C#; keep logic as-is, swap only the engine calls.
2. **Map onto nova2's existing architecture; don't let Nova1 override it.** Read nova2's current code for a subsystem before touching it. Nova1 is a behavior reference, not a target to copy verbatim.
3. **NovaScript compatibility is a hard constraint** — existing `.txt` scenarios (`ch1.txt`, `ch2.txt`, `test_*.txt`) are the regression suite.
4. **Godot idiom wins over literal Unity translation** when the two conflict — log the reason in the decision table.
5. Before porting any subsystem: read both the Nova1 source and nova2's current code, produce a diff-and-plan, then implement.
6. **Visual/UI changes are never self-certified.** Implement, build, then hand the user concrete verification steps. Only mark a porting-guide.md checklist item `[x]` ✅ after the user explicitly confirms in the editor — never before.
7. Every deviation from Nova1 (simplification, scope cut, Godot-idiom choice) gets a row in porting-guide.md's decision-record table: what changed, why.

## Testing

C# tests use **Chickensoft GoDotTest** (pinned to `2.0.35` — matches the project's `GodotSharp 4.6.3`; newer GoDotTest versions require `GodotSharp >= 4.7.0` and won't resolve). Tests live in `nova/sources/tests/` inside the main `Nova.csproj` (not a separate test project — gdUnit4Net was tried first and rejected; see the Tier 0 decision-log entry for why a separate test project doesn't reliably reach a live Godot runtime).

Run tests:
```
godot --headless --path . --run-tests --quit-on-finish
```
Exit code `0` on all-pass, `1` if any test fails. Debug via VS Code's "Debug Tests"/"Debug Current Test" launch configs (`.vscode/launch.json`) — requires a `GODOT` env var pointing at the Godot executable.

Test code is excluded from `ExportRelease` builds (see `Nova.csproj`'s conditioned `Compile Remove`).
