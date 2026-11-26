# vfxwrites

What the Editor writes into a Visual Effect Graph's file, and when (URP 17, Visual Effect Graph
17.0.4; Edit mode). The fixture holds one Editor-authored graph, `Assets/Graphs/BoundsAuto.vfx`
(the vfxbounds probe's), and the project settings it needs. `Assets/Editor/Probe/VfxWritesProbe.cs`
runs the steps below; the Editor runs it in batch mode (`-executeMethod
VfxWritesProbe.RecordAndExit`), Unbound as an Edit-mode test (`unbound test --platform EditMode`;
`run.py` adds the test entry to the run's copy only). `VfxWritesSaveLog.cs` is an
`AssetModificationProcessor` that records the paths `OnWillSaveAssets` is given.

Rows (`values.jsonl`), per step: `<step>/dirty` (`EditorUtility.IsDirty` of the graph's
`VisualEffectResource` after it), `<step>/file` (the graph file's lines removed and added since the
step before, or `unchanged`) and `<step>/will save` (the paths of each `OnWillSaveAssets` call in
the step, when there was one).

- `open`, `import` (a forced import), `import/contents` (the types `GetContents` lists, counted);
- `write unchanged`: `VisualEffectResource.WriteAsset` with nothing changed;
- `initialEventName`, `updateMode`, `preWarmDeltaTime`, `preWarmStepCount`, `cullingFlags`,
  `instancingMode`, `instancingCapacity`: each resource setting set, then `<setting> save`:
  `AssetDatabase.SaveAssets`;
- `write after saves`: `WriteAsset` again;
- `block edit`: the spawner's burst block's `Count` input set (and `block edit/slot dirty`), then
  `block edit write`: `WriteAssetWithSubAssets` (the package's save: the graph's objects set as the
  resource's contents, then `WriteAsset`);
- `reimport`: a forced import;
- `create`: `VisualEffectAssetEditorUtility.CreateNewAsset` of a new graph (`create/text` the file,
  `create/meta` its .meta without the GUID line), then `create save`: `SaveAssets`.

Measured in the Unity 6000.0.71f1 Editor (Linux, batch mode; two identical captures): an import
writes nothing; `WriteAsset` with nothing changed leaves the bytes as they were; every resource
setting marks the resource dirty, and `SaveAssets` hands `OnWillSaveAssets` the graph's path and
writes its file, where only that setting's line changes (`cullingFlags` set to its value changes
no line); with nothing dirty `OnWillSaveAssets` gets an empty list; a block's input edited marks
nothing dirty and is written by `WriteAssetWithSubAssets`, its one line changed; `GetContents`
lists the resource itself beside the graph's objects; `CreateNewAsset` writes the package's
nine-line file and its .meta.

Unbound matches every row. Not measured: a graph whose objects
change (models added or removed: Unbound refuses to write it), subgraphs (`.vfxoperator`,
`.vfxblock`: refused too), `rendererSettings`, `SaveAssetIfDirty` and the Editor's exit for a dirty graph (the
file is left as it is), several dirty assets at once (their order in `OnWillSaveAssets`), scene
saves' `OnWillSaveAssets`, and a type that a graph's models name newly (Unbound refuses a .NET class
library name, which the Editor's Mono could not read back).

```bash
python3 tests/projects/vfxwrites/run.py record --label a
python3 tests/projects/vfxwrites/run.py record --label b
python3 tests/projects/vfxwrites/run.py publish a b
python3 tests/projects/vfxwrites/run.py gate
```
