# Compilation events when a script changes in Play mode

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode (two byte-identical
captures; "Recompile And Continue Playing", the default, `ScriptCompilationDuringPlay` 0). None of
the values depends on the OS or the graphics device. The gate runs the engine with `--watch`, so
its hot reload sees the probe's edits, and compares every value exactly.

`Assets/Editor/CompileEventsProbe.cs` (an `[InitializeOnLoad]` editor script) subscribes to
CompilationPipeline's compilationStarted, compilationFinished, assemblyCompilationStarted,
assemblyCompilationFinished and assemblyCompilationNotRequired and to AssemblyReloadEvents, and logs
each event to a file in its output folder (it survives a domain reload). In Play mode it rewrites
`Assets/Scripts/Changing.cs` three times (a new constant, a compile error, a fixed constant) and asks
for a refresh. `Assets/Scripts/Warn.cs` has a warning; `Assets/Other` is an assembly of its own that
does not depend on the changed script. Per round: the order of the events, an assembly's (compiled
or not required) being `assembly` because their order among themselves is Unity's own; each compiled
assembly's started and finished events with its messages (type, file, line, column, text); the
assemblies not required; the context's type and whether started and finished share it.

Measured: compilationStarted, then each compiled assembly's started and finished events (output
path `Library/ScriptAssemblies/<name>.dll`, the warnings with them) and assemblyCompilationNotRequired
for each assembly that did not need compiling, then compilationFinished with the same context, a
`Bee.BeeDriver.ActiveBuild`. A successful round is followed by beforeAssemblyReload, the editor
scripts' load and afterAssemblyReload; a failed one compiles only the failing assembly (its error in
the messages), reports its dependents as not required and reloads nothing.

Unbound raises the compilation events from its hot reload's recompilation. An edit it patches in
place reloads nothing; one it cannot patch (here a changed constant) restarts the session, which it
reports as the reload: beforeAssemblyReload before the restart, afterAssemblyReload after the new
session's editor scripts loaded. Unmeasured: the order of several messages of one assembly (Unbound
lists errors, then warnings), RequestScriptCompilation, "Recompile After Finished Playing" and
"Stop Playing And Recompile", other releases.
