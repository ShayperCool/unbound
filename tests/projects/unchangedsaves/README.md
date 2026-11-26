# Saves of unchanged files, and the Editor's exit

Which saves write a file whose text would not change, which leave it untouched, and what the
Editor's exit with Play mode on raises. Recorded with the Unity 6000.0.71f1 Linux Editor in batch
mode (two identical captures).

Documented (Scripting API 6000.0,
[SaveAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.SaveAssets.html),
[SaveAssetIfDirty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.SaveAssetIfDirty.html),
[EditorUtility.SetDirty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorUtility.SetDirty.html),
[EditorApplication.Exit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication.Exit.html)):
SaveAssets writes all unsaved asset changes to disk; SaveAssetIfDirty writes one asset if it is
dirty; SetDirty marks an object so that its changes are saved. Nothing says whether an unchanged
file is written again.

`Assets/P` holds eleven Standard materials and a script asset (`ProbeData`), authored by the Editor
(`run.py create`); the runner turns `Crlf.mat` into CRLF line endings in each copy.
`Assets/Editor/UnchangedSavesProbe.cs` plays a few frames per open: frame 2 marks `SetDirty` with
`EditorUtility.SetDirty`, sets `SameValue`'s `_Glossiness` to the value it holds, sets `Reverted`'s
to another value and back, changes `Changed`'s, marks `Crlf` and `Data.asset`, then calls
`AssetDatabase.SaveAssets`; frame 3 marks `IfDirty` and calls `SaveAssetIfDirty` on it; frame 4
marks `ExitDirty` and changes `ExitChanged`, left dirty; frame 6 calls `EditorApplication.Exit`.
At the first open the start's `OnPostprocessAllAssets` marks `PostDirty` and changes
`PostChanged`. `Untouched` is never touched. Each object's dirtiness before and after each save
is recorded (`dirty.*`), every dispatch listing the probe's assets (`dispatches`), and the
callbacks after the probe asks the Editor to leave (`leaving`: `Application.quitting`,
`Application.unloading`, `EditorApplication.quitting`, Play mode state changes,
`OnApplicationQuit`, dispatches).

`run.py` opens one copy of the project three times (the Editor with `-executeMethod
UnchangedSavesProbe.Record`, which enters Play mode; `unbound run`): `open1` with nothing imported,
`open2` with nothing changed, `open3` with nothing changed that does nothing (`UNCHANGEDSAVES_NOOP=1`:
it plays and leaves). Before each open every file of the project's `Assets/`, `Packages/` and
`ProjectSettings/` gets one old modification time; after it each file's time is recorded
(`<open>.time.<path>`: `kept` or `written`) with its content (`<open>.exit.<path>`,
`tests/file_writes.py`).

Measured: a dirty asset's file is written by SaveAssets, SaveAssetIfDirty, the first open's save
(after the postprocessors) and the Editor's exit even when its text is unchanged (a new
modification time, the same bytes: `SetDirty`, `Reverted`, `Data.asset`, `IfDirty`, `ExitDirty`,
`PostDirty`), and the Editor imports it again (it is listed by the next `OnPostprocessAllAssets`).
Setting a material property to the value it holds does not mark it dirty (`SameValue` is left
as it is). A CRLF file is written with LF. A session that changes nothing writes no file
(`open3`), settings and the package lock included. The Editor's exit with Play mode on raises
`EditorApplication.quitting` (Play mode still on) and then reports its save to
`OnPostprocessAllAssets` in one call; it raises no `Application.quitting`, no
`Application.unloading`, no `OnApplicationQuit` and no Play mode state change.

Unmeasured: the exit's save after the session left Play mode (in Edit mode); other asset kinds.

Create (once), record, publish and check:

    python3 tests/projects/unchangedsaves/run.py create
    python3 tests/projects/unchangedsaves/run.py native --label a
    python3 tests/projects/unchangedsaves/run.py native --label b
    python3 tests/projects/unchangedsaves/run.py publish a b
    python3 tests/projects/unchangedsaves/run.py check
