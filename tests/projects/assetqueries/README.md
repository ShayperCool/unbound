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

Record and gate:

    python3 tests/projects/assetqueries/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/assetqueries/run.py publish A B
    python3 tests/projects/assetqueries/run.py gate
