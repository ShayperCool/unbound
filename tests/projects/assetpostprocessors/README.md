# AssetPostprocessor callbacks

Which `AssetPostprocessor` callbacks run when textures, models and audio import, in which order,
what they see, and what their changes do to the imported assets. Unity 6000.0.71f1, recorded with
the macOS Editor in batch mode (two byte-identical captures); nothing depends on the OS or the
graphics device (texture import follows the Standalone build target on both hosts).

Documented (Scripting API 6000.0,
[AssetPostprocessor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.html),
[GetPostprocessOrder](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.GetPostprocessOrder.html),
[GetVersion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.GetVersion.html),
[OnPostprocessAllAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.OnPostprocessAllAssets.html),
[OnPreprocessTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.OnPreprocessTexture.html),
[OnPostprocessTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.OnPostprocessTexture.html),
[OnAssignMaterialModel](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.OnAssignMaterialModel.html),
[OnPostprocessMaterial](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.OnPostprocessMaterial.html)):
postprocessors run by increasing `GetPostprocessOrder`, which does not order `OnPostprocessAllAssets`;
OnPreprocess* change the importer's settings before the import; OnPostprocessTexture gets the texture
before compression (its pixels can be changed); a returned material from OnAssignMaterialModel is
the renderer's, null leaves the importer's own; OnPostprocessMaterial is called only for
"Standard (Legacy)" material import; a changed `GetVersion` re-imports the processor's assets;
every domain reload calls OnPostprocessAllAssets.

The probe (`Assets/Editor/Processors.cs`): `PPEarly` (order -5), `PPLate` (5), `PPTieA`/`PPTieB`
(0, declared in the other order), `PPParts` (the model's inner callbacks and the sprite callback),
`PPVersioned`, and three `OnPostprocessAllAssets` classes (`PPAllZ`, `PPAllA` with order 100,
`PPAllFour` with the four-argument overload and order -100). Each callback appends what it saw to a
file. The assets (`make_assets.py`): `Plain.png` (untouched), `Shrink.png` (filter, wrap, then
maximum size 32, no mip maps, readable, uncompressed), `Invert.png` (readable and uncompressed in its
.meta; every mip level's colours inverted in OnPostprocessTexture), `Sprite.png` (made a single
sprite, then 50 pixels per unit), `Tone.wav` (stereo; forced to mono, then PCM and Decompress On
Load), and the model probe's `Materials.fbx` (legacy materials; scale 2, then colliders and
read/write; a child with a BoxCollider added in OnPostprocessModel, `Cube_Red` given
`Override.mat`, `M_Alpha` made green) and `Animated.fbx` (defaults), with this probe's `.meta`
files. In Play mode `PostprocessorProbe` loads every asset, records the results (sizes, formats,
mip texel hashes, sprites, the model's hierarchy, meshes, materials and colliders, the clip and
its samples), what `AssetImporter.GetAtPath` gives, each `.meta`'s hash, each asset's callbacks in
calling order with what they saw, the OnPostprocessAllAssets calls made in Play mode and the order
of the Play mode domain's load.

Measured, beyond the documentation: ties of `GetPostprocessOrder` run by class name;
OnPreprocessAsset runs for every asset (folders, scenes, materials and scripts too) before the
asset type's own preprocessor; the settings one processor sets are what the next one sees, and the
Editor writes the changed settings into the asset's `.meta` file (only the changed assets' files
change), so `GetAtPath` gives them afterwards; OnPostprocessTexture gets a readable, uncompressed
texture (RGB24 without alpha, else RGBA32) at its imported size with its mip levels, and the
compression follows; OnPostprocessSprites follows every OnPostprocessTexture; the clip given to
OnPostprocessAudio has no name yet and is already mono. For the model, per node in hierarchy order:
per material slot OnPostprocessMaterial of the file's material, OnAssignMaterialModel and, when it
returned null, OnPostprocessMaterial again (with "Import via MaterialDescription",
OnPreprocessMaterialDescription instead), then OnPostprocessMeshHierarchy for that node; then
OnPreprocessAnimation (also for a file without animation), OnPostprocessAnimation per clip and
OnPostprocessModel. OnPostprocessAllAssets runs by class name; the domain reload entering Play mode
calls only the five-argument overload, with no assets and `didDomainReload` true, after
`afterAssemblyReload` and before `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`.

Measured in Edit mode before Play mode (not compared; the Editor's import of an open project):
changing `PPVersioned`'s version re-imports every texture (it has OnPreprocessTexture); changing
its code without the version re-imports nothing; a new postprocessor with OnPreprocessAudio and
OnPostprocessTexture re-imports the textures and the clip.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/assetpostprocessors/run.py record --label native-a
python3 tests/projects/assetpostprocessors/run.py record --label native-b
python3 tests/projects/assetpostprocessors/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/assetpostprocessors/run.py gate
```

Values compare exactly (tolerance 0). The folder `.meta` files are committed so that both sides
see them (an asset without one has `importSettingsMissing` set in the Editor); the context's build
target is left out (it is the Editor's platform).

Unbound on macOS, 2026-11-18: 108/211 values, 0 failures and 103 recorded residuals
(`residuals.json`, five causes): model postprocessors are not run yet (Unbound's model prefabs
expand natively in scenes; they are reported once as the editor domain loads), OnPreprocessAsset is given
only the textures, models and audio clips Unbound imports, the Editor's rewritten `.meta` files,
the compressed format a block-compressed texture has in OnPostprocessTexture, and the Play mode
domain's ExitingEditMode (as in compileevents).
