# editorhooks

The internal Editor hooks package editor code (Burst's loader) calls, in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/EditorHooksProbe.cs`; two byte-identical macOS batch Editor captures; no value
depends on the OS or the graphics device):

- `EditorApplication.CallDelayed` and the `tick` event it uses, against `update`, `delayCall` and
  the player loop's Update and LateUpdate (`HooksFrameLogger`): each recorded frame's events in
  order from the probe's start (`frame0` to `frame3`), callbacks registered while `update` runs
  (`delayCallFromUpdate`, `delayedFromUpdate`), whether `tick` runs once in every frame, a call
  delayed by 0.2 s (timed with a wall clock, as CallDelayed waits on one), a cancelled call and a
  call registered from a worker thread (which thread runs it).
- `FileUtil.PathToAbsolutePath` for project, embedded-package (`Packages/com.probe.hooks`), module,
  missing-package, relative, absolute and empty paths. A null path is left out: the Editor crashes
  on it.
- `AssemblyHelper.GetDefaultAssemblySearchPaths`: its last two entries (`search.tail`) and whether
  the entries before them are the folders of the Editor's precompiled assemblies sorted by their
  full paths, UnityEditor.Graphs's (`Managed`) and each installed platform's that holds a
  `UnityEditor.*.Extensions.dll` (`search.precompiledFolders`), so the row holds on every OS and
  installation.

Paths are recorded with the project folder, the Editor's contents folder and each package's folder
replaced by a name.

Measured: an Editor update runs `tick` (CallDelayed's callbacks in it, in the order they were
registered), then `update`, then `delayCall`, once per frame after the player loop; a `delayCall`
added by an `update` handler runs in the same update, a call delayed from it in the next update's
tick; a cancelled call never runs; a call registered from a worker thread runs on the main thread.
`PathToAbsolutePath` gives a registered package's folder for `Packages/<name>/...`, an absolute
path as it is, and any other path joined under the project folder as given (no "." or ".."
resolution, backslashes kept; the empty path is the project folder). The search paths end with
the engine assemblies' folder (`Managed/UnityEngine`) and `Library/ScriptAssemblies`, relative.

Unmeasured: precompiled plug-ins of the project or its packages among the search paths (the probe
has none), other releases.

```sh
python3 tests/projects/editorhooks/run.py record --label a
python3 tests/projects/editorhooks/run.py record --label b
python3 tests/projects/editorhooks/run.py publish a b
python3 tests/projects/editorhooks/run.py gate
```
