# What Refresh does with changed assets

Which import callbacks `AssetDatabase.Refresh` runs for assets whose files changed on disk, loaded
before the change or not, and what the loaded objects and later loads give; `ImportAsset` of the same
changes as the control. Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two
identical captures).

`Assets/Data` holds `Loaded`, `Unloaded` and `Imported` as a text (`.txt`), a texture (`.png`, 4x4)
and a `.count` file of `Assets/Editor/CountImporter` (a ScriptedImporter making a TextAsset of the
file's number and how many times it imported it in the domain). `Assets/Editor/RefreshPostprocessor`
notes `OnPreprocessAsset`, `OnPostprocessTexture` (with the width) and `OnPostprocessAllAssets` (its
imported paths) for them, the importer its `OnImportAsset`, into `Assets/Scripts/RefreshEvents`.
`Assets/Broken/Broken.fbx` is a model whose file does not import, changed in the same Refresh.

`Assets/Scripts/RefreshChangedProbe.cs` records in Play mode into values.jsonl in
`$REFRESHCHANGED_OUT` (tests/value_probe.py), the callbacks of each step sorted (the import order is
the Editor's queue's): the `Loaded` assets loaded (`loaded/before`, `loaded/events`), then the
`Loaded` and `Unloaded` files changed (text `v2`, a 6x5 image, number 2) and `Refresh` called
(`refresh/events`, and `refresh/events.imports` without the texts' `OnPreprocessAsset`), the loaded objects and fresh loads after it, each `Unloaded` asset loaded
(`unloaded/*` and the callbacks its load runs), a second `Refresh`, and the `Imported` files changed
and imported one by one by `ImportAsset` (`import/*`).

What it shows: `Refresh` imports every changed asset during the call, loaded or not: each one's
`OnPreprocessAsset`, its importer and postprocessors, and one `OnPostprocessAllAssets` naming them
all; the loaded objects take the new content, and a later load of one not loaded runs no callback;
a `Refresh` with nothing changed runs none; a model that does not import does not stop the others. `ImportAsset` of a changed asset does the same for it.

`residuals.json` names what Unbound does otherwise: the first import of an asset at its first load in
the session.

Record and gate:

```bash
python3 tests/projects/refreshchanged/run.py record --label <label>   # twice, then publish one
python3 tests/projects/refreshchanged/run.py gate
```
