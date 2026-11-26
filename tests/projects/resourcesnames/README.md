# Resources names probe

What `Resources.Load` and `Resources.LoadAll` give for paths that name several files of one
`Resources` folder whose extensions differ, files of a kind the Editor imports as a
`DefaultAsset`, and a folder named like a file. `Assets/Resources/pair/` holds `a.txt` + `a.png`,
`b.mtl` and `c.xyz` alone, `d.txt` + `d.mtl`, `e.png` + `e.mtl`, `f.txt` + folder `f/` (with
`inner.txt`), and `g.obj` + its library `g.mtl`. For each name the probe writes `Load` (untyped and as
TextAsset, Texture2D, GameObject, DefaultAsset), `LoadAll` in its own order and sorted, and
`LoadAll` of TextAsset and DefaultAsset, then `LoadAll("pair")`.

Measured (Unity 6000.0.71f1, Linux, batch Play mode): a file the Editor imports as a
`DefaultAsset` (`.mtl`, `.xyz`) is never returned, alone or beside a file of the same name, typed
or not; `LoadAll` returns the files in the ordinal order of their paths (`a.png` before `a.txt`,
`f.txt` before `f/inner.txt`), and untyped `Load` the first that loads; a folder named like a file
is not an asset of that name. Not measured: several `Resources` folders, letter case in the order.

Record twice with the Unity Editor, publish when the captures agree, and compare Unbound:

    python3 tests/projects/resourcesnames/run.py record --label a
    python3 tests/projects/resourcesnames/run.py record --label b
    python3 tests/projects/resourcesnames/run.py publish a b
    python3 tests/projects/resourcesnames/run.py gate

`ProbeRunner.Create` made the scene (run it in a scratch copy of the probe and copy `Assets/` and
`ProjectSettings/EditorBuildSettings.asset` back).
