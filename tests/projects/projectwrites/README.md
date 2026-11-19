# What the Editor writes into a project

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two byte-identical captures):
these files depend on neither the OS nor the graphics device, apart from the installation's
platform modules, which the probe checks against what the installation holds (`*.platforms`).

`run.py` stages the project for each run: the committed probe, files without a `.meta` under
`Assets/Startup` (about forty formats: images, models, audio, fonts, text, shaders, native YAML
assets, prefabs, scenes, UI Toolkit files, an unknown extension) and no `packages-lock.json`.
`Assets/Editor/ProjectWritesProbe.cs` then runs the same steps twice, in separate runs: in Play
mode (`play.*`, `-executeMethod ProjectWritesProbe.Record` / `unbound run`) and in Edit mode
(`edit.*`, the EditMode test `ProjectWritesEditTests` / `unbound test`). Each step records the
files it may touch right after its calls; `run.py` adds every file of `Assets/`, `Packages/` and
`ProjectSettings/` that differs from the staged copy after the run (`exit.*`). Texts are compared
exactly, with GUIDs written as the asset path they name now, random file ids as their order, a
creation time as a placeholder, a file's main document first (`*.idsAscending` records the order
the file has), and the per-platform entries of the installation's platform modules left out
(`*.platforms` says whether they are the installed ones).

The steps: what opening the project wrote (the new `.meta` files, the lock); new files and folders
and `Refresh`; `ImportAsset` of a new file in a new folder; `CreateFolder` (twice, and in a missing
folder); `CreateAsset` of a ScriptableObject, `AddObjectToAsset`, `SaveAssets`; `CopyAsset`,
`MoveAsset` (also into a missing folder and onto an asset), `RenameAsset` (also onto a taken name and
an extension's), `GenerateUniqueAssetPath` (taken, free, folders, numbered names, other case, a
missing folder); `DeleteAsset` (an asset, nothing, a folder) and the GUID lookups after it,
`MoveAssetToTrash`; `CopyAsset` of a folder holding a hidden file; labels, importer settings and a
copy of assets whose `.meta` files use CRLF; moving, validating, copying, deleting and making a
folder in a read-only (built-in) package and copying out of it; imports and `CreateAsset` between `StartAssetEditing` and `StopAssetEditing`;
`TextureImporter`, `ModelImporter` and `AssetImporter` settings, `SaveAndReimport` and
`WriteImportSettingsIfDirty`; `SetLabels`/`ClearLabels`; a texture imported with an
`AssetPostprocessor` that changes its settings; `PlayerSettings.SetScriptingDefineSymbols`
(Edit mode; in Play mode after the values are written), `EditorBuildSettings.scenes`,
`EditorSettings.projectGenerationRootNamespace`, `SaveAssets`, settings changed after the last
`SaveAssets` (`PlayerSettings.companyName`), a manifest change and `Client.Resolve`.

Measured: a new file or folder gets its `.meta` when it is imported (opening the project, `Refresh`,
`ImportAsset`, a new folder's parents too): `fileFormatVersion: 2`, a new GUID of 32 lowercase hex
digits, the importer's defaults, LF endings, no creation time; a C# script's ends after its GUID
without a line break; each importer's defaults are the same for every format it imports, a font's
names come from the font, a texture lists `DefaultTexturePlatform`, `Standalone` and the installed
platform modules. `ProjectVersion.txt` gets the release's build revision and `packages-lock.json`
is written when the project has none. Between `StartAssetEditing` and `StopAssetEditing` an import
waits for the last `StopAssetEditing`, `CreateAsset` does not. `CreateAsset` names the object after
its file (file id 11400000, the `.meta`'s `mainObjectFileID`); `AddObjectToAsset` gives a random
64-bit file id and the file is written by `SaveAssets`, documents in ascending file id order.
Copies, moves and renames name a native asset's main object after its new file; a copy gets a new
GUID, a move keeps it. `GenerateUniqueAssetPath` keeps a free path, else takes the first free
`name N` (a name's own ` N` replaced), spells the folder as the project does and gives "" for a
missing folder. `DeleteAsset` deletes folders too; `AssetPathToGUID` (by default) and
`GUIDToAssetPath` still answer for a deleted asset. A folder copy takes its hidden files along,
without a `.meta`. A `.meta` written again (labels, importer settings, a copy) has LF endings, and a
texture's lists the installed platform modules. In a read-only package `ValidateMoveAsset` passes,
`MoveAsset` answers that the path is immutable and `DeleteAsset` false. Labels are written at once,
in ordinal order, repeats kept. Importer settings are written by `SaveAndReimport`, `WriteImportSettingsIfDirty` and
a new AssetBundle name (lowercased), in place in the `.meta`; settings an `AssetPostprocessor`
changes during an import are written too. Settings changed through the API are written by
`SaveAssets` and when the Editor quits in Edit mode, never in Play mode. In Edit mode the lock
follows a resolved manifest change by the time the Editor quits.

Unbound on macOS, 2026-11-19: 603/630 values, 0 failures and 27 recorded residuals
(`residuals.json`): lists of numbers written as YAML sequences instead of the Editor's hex strings
(the serialized-file writer), the lock not written again after an Edit-mode `Client.Resolve`, no
`.meta` for a file of an extension no importer claims (when the project opens, Unbound cannot tell
which extensions packages' ScriptedImporters claim), and copies and folders refused in a read-only
package (the Editor writes into its per-project copy of the package; Unbound's package files are
the installation's or the shared store's).

Unmeasured: other importers (`.asmref`, plug-ins and folder plug-ins such as `.bundle`, videos,
packages' ScriptedImporters: Unbound reports such new files and writes no `.meta`), file operations
in registry or built-in packages (Unbound refuses them with an error and writes nothing), a `.meta`
left without its asset, labels with YAML special characters, the trash's file names on a name
clash, settings changed in Play mode and then saved in Edit mode, other release families (Unbound
writes no new `.meta`, lock or version file for them).

Record (macOS, holding the machine's heavy-job lock), publish and gate:

    python3 tests/projects/projectwrites/run.py record --label native1
    python3 tests/projects/projectwrites/run.py record --label native2
    python3 tests/projects/projectwrites/run.py publish native1 native2
    python3 tests/projects/projectwrites/run.py gate
