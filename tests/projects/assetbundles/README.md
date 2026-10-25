# AssetBundle probe

Bundles the Editor built for StandaloneLinux64 (`ProbeRunner.Create`, committed under
`Reference/bundles/{lz4,lzma,none}`: chunk-based LZ4, whole-file LZMA, uncompressed): `things`
(a texture, a material, a mesh asset, a prefab with a ProbeComponent and a child collider, a
ProbeData asset, a text asset) and `scene` (a scene with a cube and an instance of the prefab, which
depends on `things`), with each build's manifest bundle. `BundleProbe` loads them every way Unity
offers (LoadFromFile, LoadFromFileAsync, LoadFromMemory; LoadAsset, LoadAssetAsync, LoadAllAssets;
the manifest; the scene through SceneManager) and records what they hold, then an image of the
instantiated prefab.

Make the bundles (once; they are committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/assetbundles \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/bundles-create.log

Record Unity's reference (the Editor needs a display):

    BUNDLE_PROBE_OUT=$PWD/tests/projects/assetbundles/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/assetbundles \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/bundles.log

Run Unbound and compare:

    BUNDLE_PROBE_OUT=/tmp/bundles unbound run -p tests/projects/assetbundles --frames 600 --size 320x180 --no-watch
    python3 tests/projects/assetbundles/compare.py tests/projects/assetbundles/Reference /tmp/bundles
    python3 tests/projects/particles/compare_images.py tests/projects/assetbundles/Reference /tmp/bundles

Everything the probe records matches; the image is within 1/255 on average.
