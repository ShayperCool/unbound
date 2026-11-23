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
(`Assets/Editor/OpenForEditHooks.cs`): one with the single-path `IsOpenForEdit` and `MakeEditable`
callbacks, one with the list `IsOpenForEdit` and `CanOpenForEdit` callbacks, so no callback kind is on
both and no row depends on their order. The callbacks record their calls and refuse paths by a marker
in the path. Each query records its answer and the callbacks it reached, with their arguments.
`tests/projects/openforeditorder` has instead two processors with the same callbacks (list
`IsOpenForEdit` and `MakeEditable`), each refusing its own path, and the order TypeCache lists them in.

Measured on the Unity 6000.0.71f1 Editor (two identical macOS batch captures per probe; the values
depend on neither the OS nor the graphics device):

- Without callbacks, `MakeEditable` returns true for every path, also a built-in or registry package's, clears
  the out list, and leaves a read-only file read-only; a null path or path list throws
  `ArgumentNullException`. `CanOpenForEdit` and `IsMetaFileOpenForEdit` (of the object's path with
  `.meta`) answer as `IsOpenForEdit`: only paths of a package the project does not hold are refused.
- Empty paths and immutable package paths reach no callback: the first are open, the second refused
  (and listed first by the list queries, which skip blank entries).
- A single-path `IsOpenForEdit` (and the object forms) calls the list `IsOpenForEdit` callbacks with
  the one path and a new list, until one refuses, then the single-path callbacks, whose message the
  query returns. The list query calls only the list callbacks, with the out list holding the
  immutable paths.
- `CanOpenForEdit` first asks `IsOpenForEdit` (single-path callbacks included for one path, not for
  a list); for the paths not open it calls the `CanOpenForEdit` callbacks (the list holding the
  immutable paths) until one refuses. Its message stays empty.
- `MakeEditable` passes every path (one empty path as none, blank list entries as they are), the
  prompt (`""` for null) and the caller's list (a new one for none) to the callbacks until one
  refuses, and returns false with the list as they left it; when all accept it clears the list and
  returns true.
- Two processors with the same callback are called one after the other, in the order
  `TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>()` lists them, until one refuses.
  Unity documents no order between processors; in an earlier capture with three processors that
  order was neither the declaration nor the name order (it was kept out of the reference).

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
