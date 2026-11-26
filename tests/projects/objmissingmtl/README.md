# OBJ models whose material library does not resolve their bindings

`Assets/Resources/Models` holds OBJ files of two quads, the first face bound (`usemtl`) to one
material and the second to another, each imported with its own `materialImportMode` (the digit in
its name: 0 None, 1 Standard, 2 Material Description); every mesh is readable:

| model | `mtllib` | bindings |
|---|---|---|
| `Missing0/1/2` | `Absent.mtl` (no such file) | Red, Green |
| `Present0/1/2` | `Present.mtl` (defines Red and Green) | Red, Green (a control) |
| `Derived` | `Absent.mtl`, with `Derived.mtl` beside the model | Red, Green |
| `Undefined1` | `Present.mtl` | Blue, Yellow (not defined there) |
| `Mixed1` | `Present.mtl` | Red, Blue (Blue not defined) |

`Assets/Scripts/ObjMissingMtlProbe.cs` records, in Play mode, every object each model file holds
(sorted), each renderer's mesh, vertex count, submeshes and their indices, and its materials'
names, shaders and colors.

Measured in the Unity 6000.0.71f1 Editor (batch mode, two identical captures): a missing library,
or one that leaves any bound material undefined (`Mixed1`: even with the other one defined), drops
every binding. The faces are one submesh with the mesh group's default material, `defaultMat`
(`<group>Mat`, group `default` before any `g` statement) of the class defaults (color 0.8) in the
Standard and Material Description modes, the default `Default-Material` in None. A library named
after the model (`Derived.mtl`) is not used. With the library resolving every binding each
material keeps its submesh.

Known residual (`residuals.json`): `Resources.LoadAll("Models/Derived")` in Unbound also returns
`Derived.mtl` (a DefaultAsset); the Editor returns the model's objects only.

Unmeasured: a file of several meshes (whether one mesh's undefined binding drops the others'), a
file with bindings but no `mtllib` statement, names that differ only in case, and any import log
line (imports happen before Play mode).

    python3 tests/projects/objmissingmtl/run.py record --label A   # Editor capture (batch mode)
    python3 tests/projects/objmissingmtl/run.py publish A B        # two identical captures
    python3 tests/projects/objmissingmtl/run.py gate               # the engine vs Reference/
