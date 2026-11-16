# AssetBundle asset names

`Assets/Scripts/BundleNameProbe.cs` loads four bundles from `Assets/StreamingAssets` and looks up
their assets by many spellings of their names: the name as stored, lower and upper case,
backslashes, a partial path, the file name with and without its extension, and names beyond ASCII.
For each bundle and query it records `Contains`, `LoadAsset` (untyped, as `BundleNameData`, as
`Sprite`), `LoadAssetAsync` and `LoadAssetWithSubAssets`, and each bundle's `GetAllAssetNames`.
Values compare exactly.

The bundles were built once by `ProbeRunner.Create` in the Unity 6000.0.71f1 Editor for
StandaloneLinux64 and are committed: `sbp/mixed` through the Scriptable Build Pipeline
(`ContentPipeline.BuildAssetBundles`) with mixed-case addressable names, which it keeps as given,
as Addressables builds do; `legacy/names`, `nofile/names` and `noext/names` through
`BuildPipeline.BuildAssetBundles`, which stores lower-case asset paths, with no option,
`DisableLoadAssetByFileName` and `DisableLoadAssetByFileNameWithExtension`.

Measured in the Unity 6000.0.71f1 Editor (two identical captures): a query's backslashes read as
`/`; a bundle with lower-case names lower-cases the query, the Scriptable Build Pipeline bundle
does not, so its names are found only as stored; the stored names then compare exactly. Lower case
covers ASCII letters only, when the build stores a name and when a query is looked up:
`Assets/Bundled/Ünïcode.txt` is stored as `assets/bundled/Ünïcode.txt` and found as that and as
`Ünïcode`, not as `assets/bundled/ünïcode.txt`, `ASSETS/BUNDLED/ÜNÏCODE.TXT` or `ünïcode`. Only a
query without `/` may be a file name: without its extension unless the bundle was built with
`DisableLoadAssetByFileName`, with it unless built with `DisableLoadAssetByFileNameWithExtension`;
the Scriptable Build Pipeline bundle takes neither. A partial path (`bundled/data.asset`,
`/data.asset`) finds nothing. Unbound reads these choices from the bundle's serialized `m_PathFlags`.

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath <copy of this probe> \
        -executeMethod ProbeRunner.Create -logFile create.log   # once; copy Assets/ back
    python3 tests/projects/bundlenames/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/bundlenames/run.py publish A B        # two identical captures
    python3 tests/projects/bundlenames/run.py gate               # the engine against Reference/
