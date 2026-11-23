# Editability without version control, and the processor callbacks

`Assets/Editor/OpenForEditProbe.cs` records in Play mode what `AssetDatabase.MakeEditable` (one path,
and a list with a prompt and an out list), `CanOpenForEdit` and `IsMetaFileOpenForEdit` answer, with
`IsOpenForEdit` beside them, in a project without version control (Visible Meta Files): for asset,
meta, folder, missing, built-in package (file, meta, folder), registry package (file, folder), manifest, settings, Library, backslash,
absolute (also the `Assets/` folder path the Input System's check-out helper passes), outside,
empty and null paths; for a read-only imported asset and a read-only file not imported (and whether
MakeEditable makes them writable); for an asset, a package asset, a scene object, an object in memory
and none; and for lists (results, an earlier list content, empty, blank and null entries, null
arguments with their parameter names).

`tests/projects/openforedithooks` is the same project with two `AssetModificationProcessor`s
(`Assets/Editor/OpenForEditHooks.cs`): one with the single-path `IsOpenForEdit`, single-path
`CanOpenForEdit` and `MakeEditable` callbacks, one with the list `IsOpenForEdit` and `CanOpenForEdit`
callbacks, so no callback is on both and no row depends on their order. The callbacks record their
calls and refuse paths by a marker in the path (one with a null message). Each query, for paths in
and outside the versioned folders, for lists and for objects (also the Assets folder, which has no
.meta file), records its answer and the callbacks it reached, with their arguments.
`tests/projects/openforeditorder` has instead two processors with the same callbacks (list
`IsOpenForEdit` and `MakeEditable`), each refusing its own path: it records the answers and whether
the calls follow the order TypeCache lists the processors in up to the first that refuses, not that
order (nor the number of calls, which depends on it).

Measured on the Unity 6000.0.71f1 Editor (two identical macOS batch captures per probe; the values
depend on neither the OS nor the graphics device):

- Without callbacks, `MakeEditable` returns true for every path, also a built-in or registry package's,
  clears the out list, and leaves a read-only file read-only; a null path or path list throws
  `ArgumentNullException`. `CanOpenForEdit` and `IsMetaFileOpenForEdit` (of the object's path with
  `.meta`; the Assets folder has none and is open) answer as `IsOpenForEdit`: only paths of a package
  the project does not hold are refused.
- `IsOpenForEdit` and `CanOpenForEdit` call no callback for blank paths and paths outside the
  versioned folders `Assets`, `Packages` and `ProjectSettings` (Library, UserSettings, absolute,
  outside or backslashed paths): those are open. Immutable package paths reach none either and are
  refused (listed first by the list queries, which skip the blank and unversioned entries).
- Both queries share one flow: the list callbacks with all the paths (for one path, that path and a
  new list) until one refuses; when all accept, each processor's single-path callback with every
  path, each refused path added to the list and the message (null as given) returned. (With two
  processors having single-path callbacks Unbound stops after one that refused a path: unmeasured.)
- `CanOpenForEdit` first runs that flow for `IsOpenForEdit` (its message dropped); the paths not open
  then go through it for `CanOpenForEdit`, with the immutable paths already in the list.
- `MakeEditable` passes every path (unversioned ones too; one empty path as none, blank list entries
  as they are), the prompt (`""` for null) and the caller's list (a new one for none) to the callbacks
  until one refuses, and returns false with the list as they left it; when all accept it clears the
  list and returns true.
- Processors are called in the order `TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>()`
  lists them. Unity documents no order between processors, and in an earlier capture with three
  processors Unbound's TypeCache order differed from the Editor's (neither declaration nor name
  order there), so the probes record no such order.

```sh
python3 tests/projects/openforedit/run.py record --label A     # twice, A and B; also for openforedithooks, openforeditorder
python3 tests/projects/openforedit/run.py publish A B
python3 tests/projects/openforedit/run.py gate
python3 tests/projects/openforedithooks/run.py gate
python3 tests/projects/openforeditorder/run.py gate
```

## Contracts and observations

The release's [MakeEditable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.MakeEditable.html)
page says that without an active version control system paths inside the project are editable and
MakeEditable returns true and does nothing else, which the captures show. It also says MakeEditable
returns false for paths of non-local packages: in the 6000.0.71f1 Editor it returns true for a
built-in and a registry package's file and folder (`make.builtin*`, `make.registry*`), while
`IsOpenForEdit` and `CanOpenForEdit` refuse them; Unbound answers as the Editor does. The
[IsOpenForEdit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.IsOpenForEdit.html)
and [AssetModificationProcessor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetModificationProcessor.html)
pages document the callbacks but not the order between processors, nor how the single-path and
list forms combine: those are observations of these captures.
