# defaultasset

Measures what Unity 6000.0.71f1 Editor returns when loading and querying `DefaultAsset` kinds
and non-`DefaultAsset` controls through `AssetDatabase`, `AssetImporter`, and `SpriteAtlasExtensions`:

- Folders (`Assets/SampleFolder`, `Assets/Folder.WithDot`, `Assets/StreamingAssets`, `Assets/StreamingAssets/SubDir`, root `Assets`)
- Unknown / unclaimed file extensions (`Assets/Data/Blob.xyz`, `Assets/Data/Raw.bin`, `Assets/Data/Multi.part.dat`)
- Extensionless files (`Assets/Data/NoExtension`)
- Compiler response and trigger files (`Assets/Data/csc.rsp`, `Assets/Data/Trigger.modulecompilationtrigger`)
- Special `DefaultImporter` folders (`Assets/StreamingAssets/` with `.txt`, `.json`, `.png`, `.bin`, nested subfolder & `.txt`; `Assets/WebGLTemplates/Custom/index.html`; `Assets/WebPlayerTemplates/Legacy/readme.txt`; plus nested non-root control `Assets/Data/StreamingAssets/nested_not_root.txt`)
- Precompiled plugin control (`Assets/Data/ProbeSame.dll` with `PluginImporter`)
- Negative / non-`DefaultAsset` controls (`.txt`, `.json`, `.png`, `.unity`, `.cs`, missing path)
- `AssetDatabase.FindAssets` (`t:DefaultAsset`, `t:Folder`, `t:TextAsset`, `t:Texture2D`, `t:SceneAsset`, name queries)
- `SpriteAtlasExtensions.Add` accepting folder `DefaultAsset`s while rejecting non-folder `DefaultAsset`s.

```sh
python3 tests/projects/defaultasset/run.py record --label a
python3 tests/projects/defaultasset/run.py record --label b
python3 tests/projects/defaultasset/run.py publish a b
python3 tests/projects/defaultasset/run.py gate
```
