# defaultasset

Measures what Unity 6000.0.71f1 Editor returns when loading and querying `DefaultAsset` kinds
and non-`DefaultAsset` controls through `AssetDatabase`, `AssetImporter`, and `SpriteAtlasExtensions`:

- Folders (`Assets/SampleFolder`, `Assets/Folder.WithDot`, `Assets/StreamingAssets`, `Assets/StreamingAssets/SubDir`, root `Assets`)
- Unknown / unclaimed file extensions (`Assets/Data/Blob.xyz`, `Assets/Data/Raw.bin`, `Assets/Data/Multi.part.dat`)
- Extensionless files (`Assets/Data/NoExtension`)
- Compiler response and trigger files (`Assets/Data/csc.rsp`, `Assets/Data/Trigger.modulecompilationtrigger`)
- Special `DefaultImporter` folders (`Assets/StreamingAssets/` with `.txt`, `.json`, `.png`, `.bin`, nested subfolder & `.txt`; `Assets/WebGLTemplates/Custom/index.html`; `Assets/webplayertemplates/Legacy/readme.txt`, the root spelled in lower case; plus nested non-root control `Assets/Data/StreamingAssets/nested_not_root.txt`)
- Precompiled plugin control (`Assets/Data/ProbeSame.dll` with `PluginImporter`)
- Every plug-in kind of the `PluginImporter` (`Assets/PluginKinds/`: native libraries, a native `.dll`,
  Android/iOS/WebGL sources and archives, and the `.bundle`/`.framework`/`.xcframework`/`.plugin`/`.androidlib`
  folders), and a managed plug-in declaring scripts (`Assets/Plugins/ScriptPlugin.dll`, built from
  `PluginSources/ScriptPlugin.cs` by `build_plugins.py`): its script sub-assets, their file IDs, classes and flags,
  `MonoScript.FromMonoBehaviour`/`FromScriptableObject` (`dllScripts.*`; lists sorted by name, as the order is not documented)
- `DefaultAsset`'s internal inspector `message`/`isWarning` (`inspector.*`)
- `IsNativeAsset`/`IsForeignAsset` of the main and the other objects of each native-format file kind
  (`Assets/Native/`), a prefab, a V2 sprite atlas, a project settings file, a texture, text, script, scene,
  folder, unknown file, plug-in, an object in memory and null (`native.*`, `foreign.null`)
- The root `Assets` folder: listed by `GetAllAssetPaths`, never found by `FindAssets`, and its path in lower case
- Negative / non-`DefaultAsset` controls (`.txt`, `.json`, `.png`, `.unity`, `.cs`, missing path)
- `AssetDatabase.FindAssets` (`t:DefaultAsset`, `t:Folder`, `t:TextAsset`, `t:Texture2D`, `t:SceneAsset`, name queries)
- `SpriteAtlasExtensions.Add` accepting folder `DefaultAsset`s while rejecting non-folder `DefaultAsset`s.

```sh
python3 tests/projects/defaultasset/run.py record --label a
python3 tests/projects/defaultasset/run.py record --label b
python3 tests/projects/defaultasset/run.py publish a b
python3 tests/projects/defaultasset/run.py gate
```
