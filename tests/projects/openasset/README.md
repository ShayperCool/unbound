# openasset

What `AssetDatabase.OpenAsset`, `AssetDatabase.CanOpenAssetInEditor` and `[OnOpenAsset]` methods do
in Unity 6000.0.71f1 Play mode (`Assets/OpenAsset/OpenAssetProbe.cs`). The methods
(`Assets/OpenAsset/OpenAssetCallbacks.cs`) have orders -5, -3, 0, the default, 3 and 100 with
one, two and three parameters, two validators (one of them with two parameters), and methods of
shapes the attribute does not name, in both modes; each writes down its call and answers, or
throws, as the row tells it. Every row has the answer, the selection afterwards (a scene object
named Selected is selected before each call), the reports of methods of another shape, the other
messages and the calls with their arguments (an instance ID is recorded as its asset path, with
the object's type and name for a sub-asset, or as a scene object's name).

- `open.*`: the ID, object and array forms with and without a line and column, for scene objects,
  an object in memory, IDs no object has, null and destroyed objects, every asset of the project
  (`open.asset:<path>`, opened by the order-0 method), a sub-asset, methods that throw, and
  `open.unhandled:<path>` for assets of the Editor's own format and a folder that no method opens.
- `can.none:<target>` and `can.validate:<target>`: `CanOpenAssetInEditor` for the same targets, a
  native sub-asset and a model's mesh and part, with no validator answering and with one; then
  which validators are asked, where they stop, and what one that throws does (`can.text.*`,
  `can.material.validateThrows`).

Not recorded: an asset of another format that no method opens (the Editor starts an application
outside for it), an unhandled scene or prefab (the Editor loads the one and enters Prefab Mode for
the other), and methods of one order (Unity leaves their order open). The reports of methods of
another shape come class by class in an order that differs between Editor runs, so the probe
writes them sorted.

The assets come from `tests/projects/defaultasset` (`Assets/Native`, `Assets/Data`),
`tests/projects/spriteatlaseditor` (`Assets/Sprites`) and `tests/projects/editorcallbacks`
(`Assets/Models`); the assembly definition and its reference are the probe's own.

```sh
python3 tests/projects/openasset/run.py record --label a
python3 tests/projects/openasset/run.py record --label b
python3 tests/projects/openasset/run.py publish a b
python3 tests/projects/openasset/run.py gate
```
