# Editability without version control, and the processor callbacks

`Assets/Editor/OpenForEditProbe.cs` records in Play mode what `AssetDatabase.MakeEditable` (one path,
and a list with a prompt and an out list), `CanOpenForEdit` and `IsMetaFileOpenForEdit` answer, with
`IsOpenForEdit` beside them, in a project without version control (Visible Meta Files): for asset,
meta, folder, missing, built-in package (file, meta, folder), manifest, settings, Library, backslash,
absolute (also the `Assets/` folder path the Input System's check-out helper passes), outside,
empty and null paths; for a read-only imported asset and a read-only file not imported (and whether
MakeEditable makes them writable); for an asset, a package asset, a scene object, an object in memory
and none; and for lists (results, an earlier list content, empty, blank and null entries, null
arguments with their parameter names).

`tests/projects/openforedithooks` is the same project with three `AssetModificationProcessor`s
(`Assets/Editor/OpenForEditHooks.cs`) whose `IsOpenForEdit` (list and single-path forms),
`CanOpenForEdit` and `MakeEditable` callbacks record their calls and refuse paths by a marker in
the path. Each query records its answer and the callbacks it reached, with their arguments.

Measured on the Unity 6000.0.71f1 Editor (two identical macOS batch captures per probe; the values
depend on neither the OS nor the graphics device):

- Without callbacks, `MakeEditable` returns true for every path, also an immutable package's, clears
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
- The callbacks of two processors are called one processor after the other; Unity documents no
  order between processors.

```sh
python3 tests/projects/openforedit/run.py record --label A     # twice, A and B; also for openforedithooks
python3 tests/projects/openforedit/run.py publish A B
python3 tests/projects/openforedit/run.py gate
python3 tests/projects/openforedithooks/run.py gate
```
