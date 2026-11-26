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
- `rendererSettings`, `rendererSettings save`: the renderer settings set (shadows, motion vectors,
  probes), then `SaveAssets`; `compileInitialVariants`, `compileInitialVariants save`: that flag
  toggled (a control), then `SaveAssets`;
- `block edit`: the spawner's burst block's `Count` input set (and `block edit/slot dirty`), then
  `block edit write`: `WriteAssetWithSubAssets` (the package's save: the graph's objects set as the
  resource's contents, then `WriteAsset`);
- `reimport`: a forced import;
- `block add`, `block add write`: a SetAttribute block (lifetime) added to the initialize context,
  then `WriteAssetWithSubAssets` (the graph gains the block and its two slots); `block remove`,
  `block remove write`: the block removed again and written (`.../contents`: `GetContents`' count;
  `block add write/new documents`: the documents the write added, whole);
- `create`: `VisualEffectAssetEditorUtility.CreateNewAsset` of a new graph (`create/text` the file,
  `create/meta` its .meta without the GUID line), then `create save`: `SaveAssets`.
- `fresh write/text`: that new graph with an Add operator added, written with its sub-assets (the
  file held the package's skeleton);
- `subgraph create/.vfxoperator/...`, `subgraph create/.vfxblock/...`: an operator and a block subgraph
  created (`VisualEffectAssetEditorUtility.CreateNew`), their text and .meta; `subgraph write/text`:
  the operator subgraph with an Add operator added and written; `subgraph setting/dirty`,
  `subgraph setting save/...`: its `initialEventName` set, then `SaveAssets`.
- `editing write/...`: inside `AssetDatabase.StartAssetEditing`, the renderer's shadow casting set
  and `WriteAsset`; the setting read inside the block and after `StopAssetEditing` (the graph's
  import after its write gives the compile's value).
- `save if dirty resource`, `save if dirty asset`: a setting set, then `AssetDatabase.SaveAssetIfDirty`
  of the resource and of its asset (not dirty itself); `model edit`, `model save if dirty
  asset|resource|graph`, `model save assets`: a block's input set through the graph (the graph model
  dirty, the resource clean), then `SaveAssetIfDirty` of the asset, the resource and the graph model
  and `SaveAssets`, with the model's dirty flag after each; `material save if dirty/...`:
  `SaveAssetIfDirty` of a material `CreateAsset` made and then changed; `material save assets/...`:
  the same for another material through `SaveAssets`; `settings save if dirty guid/...`:
  `SaveAssetIfDirty` by GUID of `ProjectSettings/TagManager.asset` after a tag added through a
  `SerializedObject` (whether its file changed); `exit/dirty before`: a
  setting left unsaved, and `exit/initialEventName`, which `run.py` adds after the run: that setting
  as the file holds it once the Editor (or Unbound) has exited; `exit/created ... before`: an
  operator added to the created graph's model and left unsaved, and `exit/created documents`
  (`run.py`, after the run): that file's document count.

Measured in the Unity 6000.0.71f1 Editor (Linux, batch mode; two identical captures): an import
writes nothing; `WriteAsset` with nothing changed leaves the bytes as they were; every resource
setting marks the resource dirty, and `SaveAssets` hands `OnWillSaveAssets` the graph's path and
writes its file, where only that setting's line changes (`cullingFlags` set to its value changes
no line); with nothing dirty `OnWillSaveAssets` gets an empty list; a block's input edited marks
nothing dirty and is written by `WriteAssetWithSubAssets`, its one line changed; an added block and its two slots
are written after the other objects with the file ids following the file's largest, and removing
them gives the original bytes back; `GetContents`
lists the resource itself beside the graph's objects; `CreateNewAsset` writes the package's
nine-line file and its .meta; that new graph, built and written, gets its resource's fields whole and
its documents in file id order; subgraphs are created with the same file and .meta and written as
graphs are; `SaveAssetIfDirty` saves the file of the object it is given when any object of that
file is dirty, through `OnWillSaveAssets` with its path (a graph whose model alone is dirty, through
its asset; a material), and calls nothing when nothing is dirty; it does not write a project
settings file; `SaveAssets` offers a changed material `CreateAsset` made; the Editor's exit writes a graph
whose resource or model alone was left dirty.

Unbound matches every row. Not measured: a graph whose objects
change other than one block added and removed, a block subgraph's contents, several dirty assets at once (their order in `OnWillSaveAssets`), scene
saves' `OnWillSaveAssets`, and class library types other than the primitive types and string in an
added object's type names (Unbound refuses them: .NET names them otherwise than the Editor's Mono,
which could not read .NET's names back).

```bash
python3 tests/projects/vfxwrites/run.py record --label a
python3 tests/projects/vfxwrites/run.py record --label b
python3 tests/projects/vfxwrites/run.py publish a b
python3 tests/projects/vfxwrites/run.py gate
```
