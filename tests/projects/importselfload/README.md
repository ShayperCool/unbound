# What an import gives when it loads its own asset

What `AssetDatabase.LoadAssetAtPath`, `LoadMainAssetAtPath` and `LoadAllAssetsAtPath` give when an
asset's own import loads it, and which importer imports a file whose `.meta` names a ScriptedImporter
that does not claim it. Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two identical
captures): none of this depends on the OS or the graphics device.

`Assets/Editor` holds `SelfLoadPostprocessor` (an AssetPostprocessor that loads the asset in
`OnPreprocessTexture`, `OnPostprocessTexture`, `OnPostprocessSprites` and `OnPostprocessAllAssets`,
noting what the three loads give and how many errors they log; assets in `Assets/Quiet` are noted
without loading them), `GenImporter` (a ScriptedImporter of `.gen` files that loads its asset in
`OnImportAsset`, then makes a texture with `TextureGenerator.GenerateTexture`, its postprocessors
run; the texture is 4 pixels wider at each import of the file), `OverImporter` (a ScriptedImporter of
`.over` files that names `.ovr2` as an override extension) and `ProbeImporters.SpacedImporter`
(another importer naming `.ovr2` as an override extension, in a namespace).
`Assets/Textures/Sprite.png` is a Single sprite, `Assets/Quiet` holds copies of it and of
`Thing.gen`; `Assets/Data` holds `Thing.gen`, `Mismatch.json` and `Override.ovr2` (whose `.meta`
names `GenImporter` and `OverImporter` in its importer section), `Own.over`, `Plain.ovr2` and
`Second.ovr2` (DefaultImporter `.meta` files) and `Preset.ovr2`, whose `.meta` sets `OverImporter`
as its override from the start.

`Assets/Scripts/ImportSelfLoadProbe.cs` records in Play mode into values.jsonl in
`$IMPORTSELFLOAD_OUT` (tests/value_probe.py):

* `first/<callback> <path>`: what the loads gave in that callback at the project's first import
  (the order of the imports is not recorded: the Editor's import queue decides it).
* `asset/<path>`: each asset's main object and importer; `importers/<path>`: its
  `GetDefaultImporter`, `GetAvailableImporters` and `GetImporterOverride`; `meta/Preset.ovr2`.
* `plain/*`: the lines of `ImportAsset` without options of the sprite, `Thing.gen` and the quiet
  assets; `plain2/*` the quiet ones once more after the forced imports.
* `reimport/<callback> <path>`: the same after `ImportAsset(ForceUpdate)`; `kept/*`: each object
  loaded before it, after it (alive, `==` and the same C# object as the fresh load, its instance
  id), the generated texture's width (before, of the old reference, fresh), the sprite's `.meta`.
* `forced/<path>`: `Override.ovr2` and `Mismatch.json` imported again with `ForceUpdate`.
* `set_override/*`, `spaced/*`, `again/*`, `invalid/*`, `missing/*`, `own_default/*`, `clear*/*`:
  `SetImporterOverride` and `ClearImporterOverride` on the `.ovr2`, `.over` and `.json` assets and a
  missing path: the asset's main object, importer, `GetImporterOverride`, `.meta`, import lines and
  logs after each call.

What it shows: from an asset's own import (`OnImportAsset`, its preprocessors and postprocessors, and
those of the textures `TextureGenerator` makes for it) the loads give nothing (null, an empty list)
and log nothing, at the first import and at a reimport; from `OnPostprocessAllAssets` they give the
imported asset. `ImportAsset` imports an asset already imported again, with or without
`ForceUpdate`, whether its import loads it or not: its importer and all
its callbacks run again; the objects loaded before stay alive with the same instance ids and take
the new import's content (another C# object stands for them after it); the `.meta` is not written.
A `.meta` whose importer section names a ScriptedImporter imports with it only when
the importer names the file's extension among those it imports: `Mismatch.json` is a TextAsset of
the TextScriptImporter, `Override.ovr2` (an override extension of `OverImporter`) a DefaultAsset of the
DefaultImporter (`AssetImporter`), also imported again. An override is set by an `importerOverride:`
section that `SetImporterOverride` writes into the `.meta`, the importer's own section as the
importer loaded before the call has it; the asset then imports with it, also from a `.meta` that
has it from the start. `ClearImporterOverride` removes the section.

Not covered: `ImportAsset` in Edit mode, assets of other kinds imported again, two importers
claiming one extension, nested importer classes.

Record and gate:

    python3 tests/projects/importselfload/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/importselfload/run.py publish A B
    python3 tests/projects/importselfload/run.py gate

`residuals.json` names the open differences.
