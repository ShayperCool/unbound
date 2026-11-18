# Compilation events with Enter Play Mode Options keeping the domain

The compileevents probe (its project, `../compileevents`) with one change in
`ProjectSettings/EditorSettings.asset`: Enter Play Mode Options on with Disable Domain Reload
(`m_EnterPlayModeOptions: 1`). Unity 6000.0.71f1, recorded with the macOS Editor in batch mode
(two byte-identical captures); the gate runs the engine with `--watch` and compares every value
exactly.

Measured: entering Play mode reloads no domain, so the domain the rounds start in is the one the
Editor loaded at its start (`start.load`: its editor scripts' load, `[DidReloadScripts]`,
afterAssemblyReload; no beforeAssemblyReload). A script change in Play mode still recompiles and
reloads the domain as with the default options (the rounds' rows are those of compileevents).

Unbound reads the options from the project's EditorSettings.asset: when they keep the domain, its
one editor domain loads as the Editor's start does (in Edit mode, ending with afterAssemblyReload),
then Play mode is entered without a reload (ExitingEditMode, `[InitializeOnEnterPlayMode]` with the
options, EnteredPlayMode). Recorded residuals (`residuals.json`): `round1.playMode` and
`round3.playMode`, the restarted session's Play mode entry (see compileevents).
