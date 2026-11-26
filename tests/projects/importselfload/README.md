# What an import gives when it loads its own asset

What `AssetDatabase.LoadAssetAtPath`, `LoadMainAssetAtPath` and `LoadAllAssetsAtPath` give when an
asset's own import loads it, and which importer imports a file whose `.meta` names a ScriptedImporter
that does not claim it. Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two identical
captures): none of this depends on the OS or the graphics device.

`Assets/Editor` holds `SelfLoadPostprocessor` (an AssetPostprocessor that loads the asset in
`OnPreprocessTexture`, `OnPostprocessTexture`, `OnPostprocessSprites` and `OnPostprocessAllAssets`,
noting what the three loads give and how many errors they log), `GenImporter` (a ScriptedImporter of
`.gen` files that loads its asset in `OnImportAsset`, then makes a texture with
`TextureGenerator.GenerateTexture`, its postprocessors run) and `OverImporter` (a ScriptedImporter of
`.over` files that names `.ovr2` as an override extension). `Assets/Textures/Sprite.png` is a Single
sprite; `Assets/Data` holds `Thing.gen`, `Mismatch.json` and `Override.ovr2` (whose `.meta` names
`GenImporter` and `OverImporter` in its importer section), `Own.over` and `Plain.ovr2` (a
DefaultImporter `.meta`).

`Assets/Scripts/ImportSelfLoadProbe.cs` records in Play mode into values.jsonl in
`$IMPORTSELFLOAD_OUT` (tests/value_probe.py):

* `first/<callback> <path>`: what the loads gave in that callback at the project's first import
  (the order of the imports is not recorded: the Editor's import queue decides it).
* `asset/<path>`: each asset's main object and importer.
* `reimport/<callback> <path>`: the same after `ImportAsset(ForceUpdate)` of the sprite and `Thing.gen`.
* `forced/<path>`: `Override.ovr2` and `Mismatch.json` imported again with `ForceUpdate`.
* `set_override/*`: `Plain.ovr2` given `OverImporter` with `AssetDatabase.SetImporterOverride`: its
  main object, importer, `GetImporterOverride` and its `.meta`.

What it shows: from an asset's own import (`OnImportAsset`, its preprocessors and postprocessors, and
those of the textures `TextureGenerator` makes for it) the loads give nothing (null, an empty list)
and log nothing, at the first import and at a reimport; from `OnPostprocessAllAssets` they give the
imported asset. A `.meta` whose importer section names a ScriptedImporter imports with it only when
the importer names the file's extension among those it imports: `Mismatch.json` is a TextAsset of
the TextScriptImporter, `Override.ovr2` (an override extension of `OverImporter`) a DefaultAsset of the
DefaultImporter (`AssetImporter`), also imported again. An override is set by an `importerOverride:`
section that `SetImporterOverride` writes into the `.meta`; the asset then imports with it.

Not covered: an `importerOverride` naming an importer that does not list the extension as an
override, two importers claiming one extension.

Record and gate:

    python3 tests/projects/importselfload/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/importselfload/run.py publish A B
    python3 tests/projects/importselfload/run.py gate

`residuals.json` names the open differences: a forced reimport of an asset already imported, and
`SetImporterOverride`.
