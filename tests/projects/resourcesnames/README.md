# Resources names probe

What `Resources.Load` and `Resources.LoadAll` give for paths that name several files of one
`Resources` folder whose extensions differ, files of a kind the Editor imports as a
`DefaultAsset`, and a folder named like a file. `Assets/Resources/pair/` holds `a.txt` + `a.png`,
`b.mtl` and `c.xyz` alone, `d.txt` + `d.mtl`, `e.png` + `e.mtl`, `f.txt` + folder `f/` (with
`inner.txt`), `g.obj` + its library `g.mtl`, `h.foo` (an extension the probe's ScriptedImporter
claims, with a `.meta` naming the DefaultImporter) and `i.qux` (a `.meta` naming that
ScriptedImporter, which does not claim `.qux`). `Assets/Resources/order/` holds `a.txt`, `B.txt`,
`c.txt`, `c/d.txt`, `Enemy.txt` and `Enemy (1).txt`. For each name the probe writes `Load` (untyped and as
TextAsset, Texture2D, GameObject, DefaultAsset), `LoadAll` in its own order and sorted, and
`LoadAll` of TextAsset and DefaultAsset, then `LoadAll("pair")`, `LoadAll("order")` and `Load` of
each `order/` name (also in other letter case).

Measured (Unity 6000.0.71f1, Linux, batch Play mode): a file the Editor imports as a
`DefaultAsset` (`.mtl`, `.xyz`, the unclaimed `.qux`) is never returned, alone or beside a file of
the same name, typed or not, while a file a ScriptedImporter takes is, whatever importer its
`.meta` names (`h.foo`); `LoadAll` returns files ordered by their names without extension, letter
case aside (`a, B, c, c/d, Enemy, Enemy (1)`), then by extension (`a.png` before `a.txt`), and
untyped `Load` the first that loads; a folder named like a file is not an asset of that name. Not
measured: several `Resources` folders, plug-ins and scenes in `Resources`.

Record twice with the Unity Editor, publish when the captures agree, and compare Unbound:

    python3 tests/projects/resourcesnames/run.py record --label a
    python3 tests/projects/resourcesnames/run.py record --label b
    python3 tests/projects/resourcesnames/run.py publish a b
    python3 tests/projects/resourcesnames/run.py gate

`ProbeRunner.Create` made the scene (run it in a scratch copy of the probe and copy `Assets/` and
`ProjectSettings/EditorBuildSettings.asset` back).
