# Importer settings saved over an older .meta layout

What the Editor writes into an asset's `.meta` when it saves importer settings that the file holds
in another layout than the release's. Unity 6000.0.71f1, recorded with the macOS Editor in batch
mode (two identical captures): the values depend on neither the OS nor the graphics device, apart
from the installation's platform modules, whose per-platform texture entries are left out of each
text (`.platforms` says whether those left out were the installed modules).

`Assets/Textures` holds 4x4 PNGs whose `.meta` files are a new texture's settings (the layout of
`crates/unbound-assets/src/new_meta/texture_importer.yaml`, the default and Standalone entries)
with these edits:

| texture | its `.meta` |
| --- | --- |
| `Current` | the release's layout, the control |
| `Missing` | without `mipmaps.mipMapsPreserveCoverage`, `flipbookRows`, `flipbookColumns`, `swizzle`, each platform entry's `forceMaximumCompressionQuality_BC6H_BC7` and `spriteSheet.spriteCustomMetadata` |
| `Extra` | with `legacySetting`, `mipmaps.oldMipSetting` and a platform entry's `oldPlatformKey`, which the release does not have |
| `Reordered` | `isReadable` first |
| `SwitchName` | a third platform entry named `Nintendo Switch` |
| `Version12` | `serializedVersion: 12`, otherwise the release's layout |
| `GuiNoWrap`, `DefaultNoWrap` | a GUI and a Default texture without `wrapU`, `wrapV`, `wrapW` |
| `Untouched`, `Reimported`, `SameValue`, `WriteIfDirty` | as `Missing` |

`Assets/Audio` (`Current`, `Missing`: without `loadInBackground` and the default settings'
`preloadAudioData`) and `Assets/Models` (OBJ triangles; `Missing`: without
`animations.removeConstantScaleCurves` and `animations.importConstraints`) do the same for the audio
and model importers.

`Assets/Scripts/ImporterMetaProbe.cs` records in Play mode into values.jsonl in `$IMPORTERMETA_OUT`
(tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:

* `open/<path>`: every `.meta` as the project opened with it.
* `change/<texture>`: the settings a missing key reads (`isReadable`, `mipMapsPreserveCoverage`,
  the flipbook of `ReadTextureSettings`, the wrap modes) before `isReadable` is set and
  `SaveAndReimport` saves; `change/<texture>/meta`: the file then.
* `again/Assets/Textures/SwitchName.png`: `WriteImportSettingsIfDirty` once more after that save,
  nothing changed.
* `forced/...`: `ImportAsset(ForceUpdate)` with nothing changed; `same/...`: `isReadable` set to its
  value and `SaveAndReimport`; `written/...`: `isReadable` set and `WriteImportSettingsIfDirty`.
* `change/Assets/Audio/<clip>`, `change/Assets/Models/<model>`: `forceToMono` and `globalScale`
  set and saved, the values read before, the file after.
* `end/Assets/Textures/Untouched.png`: the file nothing touched.

What it shows: opening the project, a forced import, a save without a change and a second write
write nothing; a missing wrap key reads Repeat for a GUI texture too, a missing audio preload flag
off; a save of a changed setting writes the importer's whole section in the release's layout: keys the
file lacks at their defaults in the release's places, keys the release does not have left out, keys
in the release's order, the platform `Nintendo Switch` named `Switch`, `serializedVersion` 12 written
as 13, and the installation's platform entries added (texture). The same holds for the audio and
model importers.

Not covered: files of older serialized versions with the keys of those versions, or of none, files
lacking other keys than the ones above, values of another kind than the release's and a texture
without a platform list (Unbound keeps such a file's layout, patches the change in and warns), a missing wrap key or audio preload flag in older
settings (Unbound keeps the type's default and preloading), the entries of lists (sprites, clips,
external objects) in an older layout, other importers, a sprite's first import (the Editor then
writes its `.meta`: a sprite ID, no NPOT scaling, the wrap keys).

Record and gate:

    python3 tests/projects/importermeta/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/importermeta/run.py publish A B
    python3 tests/projects/importermeta/run.py gate
