# AssetDatabase queries about paths, folders, types and AssetBundle names

`Assets/Scripts/AssetQueriesProbe.cs` records in Play mode into values.jsonl in `$ASSETQUERIES_OUT`
(tests/value_probe.py) what AssetDatabase answers for the assets of this project and for paths that
are no asset: `GetTextMetaFilePathFromAssetPath`, `AssetPathExists` and `TryGetAssetFolderInfo` for
assets, folders, `Assets`, `Packages`, an embedded package (`Packages/com.probe.local`) and a
built-in one, spellings of an asset's path (case, trailing `/`, `\`, `./`, a leading space, absolute)
and paths outside the database; `GetMainAssetTypeFromGUID`; `GetAssetOrScenePath` beside
`GetAssetPath` for assets, an object saved in the open scene, objects made at run time and null;
AssetBundle names (`GetAllAssetBundleNames`, `GetUnusedAssetBundleNames`,
`GetAssetPathsFromAssetBundle`, `GetAssetPathsFromAssetBundleAndAssetName`,
`GetImplicitAssetBundleName`/`VariantName`, `GetAssetBundleDependencies`) for names and variants
given in `.meta` files (one in mixed case, one on a folder, one in the package), then a name set
and cleared through an importer, `RemoveUnusedAssetBundleNames` and `RemoveAssetBundleName` (the
`.meta` files after each); the import worker counts (`AssetDatabase.DesiredWorkerCount`,
`EditorUserSettings`), set and forced; `DisallowAutoRefresh`/`AllowAutoRefresh`, balanced and not;
and the internal `IsV1Enabled`, `IsV2Enabled` and `AssetPathToGUID_Internal` packages call.
Logs are recorded with each row.

What it shows (Unity 6000.0.71f1, two identical macOS batch captures): the `.meta` path is the path
with a trailing `/` left out and `.meta` added, nothing else checked; `AssetPathExists` is true for
an asset's path as the database names it, a backslash as `/` and case aside (on macOS), not with a
trailing `/`, `./` or a leading space, for `ProjectSettings` assets too, and an absolute path logs a
warning; `TryGetAssetFolderInfo` answers for the path's root alone (`Assets`, case aside, or a
registered package; a missing file under `Assets` is found); AssetBundle names are held in lower
case with their variants (`name.variant`), sorted, a folder's name is its assets' (the folders are
not listed), an asset name is a file name without its extension, case aside; a name set stays in
the database until removed (`GetUnusedAssetBundleNames`), and `RemoveAssetBundleName` removes it from
the database only (the `.meta` and importer keep it), one in use only when forced, else with a
warning naming the first asset, a missing one with a warning and `true`, and a reimport does not
bring it back; a name or a variant set is written into the `.meta` at once, each adding the name
with the variant the importer has then (`set_both`, `set_variant`); an importer of a `.meta` naming
`Mixed-Case` reads it in lower case; `DesiredWorkerCount` starts
as the user setting (a quarter of the logical processors, 3 of 12) and is set apart from it, a
negative one read back; an `AllowAutoRefresh` without its `DisallowAutoRefresh` asserts while the
count is below zero; V2 is enabled.

`residuals.json` names the open differences: `GetAssetBundleDependencies` (it needs
`GetDependencies`' references), and the user settings file the Editor writes.

Record and gate:

    python3 tests/projects/assetqueries/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/assetqueries/run.py publish A B
    python3 tests/projects/assetqueries/run.py gate
