# AssetDatabase.GetDependencies

`AssetDatabase.GetDependencies` and `GetAssetBundleDependencies` as the Unity 6000.0.71f1 Editor answers
them, recorded with the macOS Editor in batch mode (two identical captures; the answers depend on
neither the OS nor the graphics device).

`Assets/Deps` holds assets that reference each other through their saved data:

| asset | references |
| --- | --- |
| `Tex.png`, `Tex2.png` | none (AssetBundles `tex`, `tex.hd`) |
| `Sec.png` | a sprite whose `.meta` gives it `Tex.png` as a secondary texture |
| `Custom.shader` | `#include "DepInclude.cginc"`, a `Fallback` to a built-in shader; its `.meta` gives it `Tex.png` as a default texture and `Tex2.png` as a non-modifiable one |
| `Fall.shader`, `UsePass.shader`, `Dependency.shader` | `Custom.shader` (`Probe/Custom`) as their `Fallback`, through `UsePass`, as a `Dependency` |
| `Mat.mat` | `Custom.shader`, `Tex.png`, a GUID no asset has (bundle `mat`) |
| `BuiltinMat.mat` | the built-in Standard shader |
| `Variant.mat` | `Custom.shader`, its parent `Mat.mat` |
| `Data.asset`, `Other.asset` | ScriptableObjects (`Assets/Scripts/DepData.cs`) referencing each other: `Data` -> `Mat.mat`, `Other`; `Other` -> `Data`, `Text.txt`, `BuiltinMat.mat` (bundle `data` for `Data`) |
| `Prefab.prefab` | a built-in mesh, `Mat.mat`, a component (`Assets/Scripts/DepBehaviour.cs`, whose `.meta` gives it `Tex.png` as a default reference) referencing `Other.asset` (bundle `prefab`) |
| `Deps.unity` | an instance of `Prefab.prefab`, a component referencing `Tex.png` |

`Assets/Scripts/AssetDependenciesProbe.cs` records in Play mode into values.jsonl in
`$ASSETDEPENDENCIES_OUT` (tests/value_probe.py), each answer in its order and as `.set` (its paths
sorted): `deps/<path>/default|recursive|direct` for each asset (the one-path overloads), `many/*` and
`resolved/*` (the three shaders naming `Probe/Custom` reimported), `cycle/*` (the overloads of several paths: a path given twice, a cycle, none, null, a missing one),
`odd/*` (null, empty, a folder, `Assets`, a missing asset, another case, backslashes, a trailing slash,
an absolute path), `bundles/<name>/<recursive>` (`GetAssetBundleDependencies`), and `change/*`: a
material's texture set to none, its answers while it is dirty and after `SaveAssets`, and its saved
text.

What it shows: an asset's direct dependencies are the other assets its saved data references, not
itself, not the built-in resources and not a GUID no asset has: a native asset's object references
(a material's shader, textures and parent, a ScriptableObject's references, a prefab's and a scene's
components' references and scripts, a scene's prefab instances' sources), a shader's default and
non-modifiable textures and the project shaders its `Dependency` declarations name (once that shader is
imported: `Dependency.shader` was imported first, its first answers lack it; `resolved/*` after a
reimport), a texture's sprites' secondary textures; a script's default references, a shader's
includes and the shaders its `Fallback` and `UsePass` name, a plain texture's and a text's are none. The recursive answer (the default) holds the given assets too and everything reached through
them, once (a cycle ends); of several paths, the union. A folder's recursive answer is the folder.
Paths of another case or with backslashes are read as the asset's (answered in its own case); null,
empty, missing, absolute and trailing-slash paths answer nothing, without a log. An unsaved change is
not seen; after the save the material also holds its shader's non-modifiable texture and keeps the
reference to the missing GUID. The Editor's order is not sorted (not by path and not by GUID) and not
documented. An AssetBundle's dependencies are the bundle names, with their variants, of its assets'
dependencies (direct or recursive alike), but its own, sorted.

Not covered: the secondary textures of a texture other than a sprite, a V1 atlas's folder packables,
models, scripted importers, sprite atlases of the V2 importer, visual effects and other
imported assets that may hold references (Unbound answers none for them, warned), binary-serialized
assets (warned), packages' assets.

Record and gate:

```bash
python3 tests/projects/assetdependencies/run.py record --label <label>   # twice, then publish one
python3 tests/projects/assetdependencies/run.py gate
```

`residuals.json` names the open differences: the order of the answers (Unbound sorts them), and a
material's save in Unbound, which writes neither the shader's non-modifiable texture nor the missing
GUID's reference.
