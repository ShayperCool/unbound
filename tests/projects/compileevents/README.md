# Compilation events when a script changes in Play mode

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode (two byte-identical
captures; "Recompile And Continue Playing", the default: the recording Editor's
`ScriptCompilationDuringPlay` preference was 0; it is a machine preference, not a compared value). None of
the values depends on the OS or the graphics device. The gate runs the engine with `--watch`, so
its hot reload sees the probe's edits, and compares every value exactly.

`Assets/Editor/CompileEventsProbe.cs` (an `[InitializeOnLoad]` editor script) subscribes to
CompilationPipeline's compilationStarted, compilationFinished, assemblyCompilationStarted,
assemblyCompilationFinished and assemblyCompilationNotRequired and to AssemblyReloadEvents, and logs
each event to a file in its output folder (it survives a domain reload). In Play mode it rewrites
`Assets/Scripts/Changing.cs` three times (a new constant, a compile error, a fixed constant) and asks
for a refresh; then it calls `CompilationPipeline.RequestScriptCompilation()` and
`RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)` and
`PlayerSettings.SetScriptingDefineSymbols` for Standalone (`PROBE_DEFINE`), for Android
(`OTHER_DEFINE`) and for Standalone again with the same value, one round each (these rounds wait
20 seconds for their compilation, which runs asynchronously). `Assets/Scripts/Defined.cs` tells
whether the scripts were compiled with `PROBE_DEFINE`; each round's `state` row records it with
both targets' scripting defines. `Assets/Scripts/Warn.cs` has a warning; `Assets/Other` is an assembly of its own that
does not depend on the changed script. Per round: the order of the events, an assembly's (compiled
or not required) being `assembly` because their order among themselves is Unity's own; each compiled
assembly's started and finished events with its messages (type, file, line, column, text); the
assemblies not required; the context's type and whether started and finished share it; Play mode's
own events in the round (state changes, `[InitializeOnEnterPlayMode]` with its options); and how the
domain the rounds start in loaded (`start.load`, its events from its editor scripts' load on, a
`[DidReloadScripts]` method of the probe's included).

Measured: compilationStarted, then each compiled assembly's started and finished events (output
path `Library/ScriptAssemblies/<name>.dll`, the warnings with them) and assemblyCompilationNotRequired
for each assembly that did not need compiling, then compilationFinished with the same context, a
`Bee.BeeDriver.ActiveBuild`. A successful round is followed by beforeAssemblyReload, the editor
scripts' load and afterAssemblyReload; a failed one compiles only the failing assembly (its error in
the messages), reports its dependents as not required and reloads nothing. Every domain load runs
the editor scripts' load, then `[DidReloadScripts]`, then afterAssemblyReload; entering Play mode
(Enter Play Mode Options on, None: the domain reloads) first sends beforeAssemblyReload. A request
with nothing changed, with or without CleanBuildCache, raises compilationStarted,
assemblyCompilationNotRequired for every assembly and compilationFinished, and then reloads the
domain all the same. New scripting defines for the active build target (Standalone) compile every
assembly with them and reload; another target's defines, or the active target's unchanged, start
no compilation. The documented contracts: [RequestScriptCompilation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Compilation.CompilationPipeline.RequestScriptCompilation.html)
("Allows you to request that the Editor recompile scripts in the project") and
[SetScriptingDefineSymbols](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerSettings.SetScriptingDefineSymbols.html)
(the defines of a named build target); the probe's Android round and unchanged round are the
controls where the call must not recompile.

Unbound raises the compilation events from its hot reload's recompilation, an assembly whose
changed inputs matched an earlier output of its compile cache (an edit undone) counting as compiled
with the warnings stored beside that output. Its one editor domain load, as Play mode starts, runs
the editor scripts' load, ExitingEditMode, beforeAssemblyReload (unless Enter Play Mode Options
disable the domain reload), `[DidReloadScripts]` and afterAssemblyReload. An edit it patches in
place reloads nothing; one it cannot patch (here a changed constant) restarts the session, which it
reports as the reload: beforeAssemblyReload before the restart, then the new session's load without
a second one. The restarted session also enters Play mode again (its Play mode callbacks run and
the scene starts over), which the Editor's reload does not.

A compilation scripts ask for (RequestScriptCompilation, new scripting defines of the active target)
runs between frames: Unbound raises its compilation events (every assembly not required when
nothing changed) and then reloads the scripts by restarting the session, with the scripting defines
set in the run kept for the restarted session.

Recorded residuals (`residuals.json`): `round1.playMode`, `round3.playMode` and rounds 4 to 6's,
the restarted session's Play mode entry; `start.load`, because Unbound's editor scripts load before
Play mode entry's pre-reload events, which the Editor sends in its Edit-mode domain (their
beforeAssemblyReload handlers run in the domain that then plays). Play mode entered from an
Edit-mode session raises no reload events. Unmeasured: the order of several messages of one assembly (Unbound
lists errors, then warnings), "Recompile After Finished Playing" and "Stop Playing And Recompile",
other releases.
