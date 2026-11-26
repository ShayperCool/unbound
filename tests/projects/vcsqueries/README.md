# VersionControl without a provider

`Assets/Scripts/VcsQueriesProbe.cs` records in Play mode into values.jsonl in `$VCSQUERIES_OUT`
(tests/value_probe.py) what `UnityEditor.VersionControl` answers in a project without a version
control provider (`VersionControlSettings.mode` "Visible Meta Files"): `Provider`'s state
(`enabled`, `isActive`, `onlineState`, `offlineReason`, `requiresNetwork`, `hasCheckoutSupport`,
`hasLockingSupport`, `isVersioningFolders`, `activeTask`, `GetActivePlugin`, `preCheckoutCallback`),
`GetAssetByPath` and `new Asset(path)` for an asset, a folder, a missing file, a `.meta`, a project
settings file and empty or null paths (each `Asset`'s path, names, folder and meta flags, state,
lock and read-only flags), `VersionControlUtils.IsPathVersioned` for those and for package,
Library, UserSettings, outside and absolute paths, `IsOpenForEdit`, `Checkout` of a path, an
`Asset`, an object and an `AssetList`, `CheckoutIsValid`, `Status`, `GetLatest`, `ClearCache`, a
`preCheckoutCallback` set, and `AssetList` counts and `FilterChildren`; each task waited for and
read (`success`, `resultCode`, `text`, `description`, `messages`, `assetList`). Logs are recorded with
each row.

What it shows (Unity 6000.0.71f1, two identical macOS batch captures): the provider is disabled,
inactive and `Offline` with no plug-in; a checkout or get-latest task fails (resultCode 2) with an
error message naming the files ("Cannot check out files in offline mode" and the paths, one a
line), logged as "Version Control: " and that message; a status task fails silently;
`GetAssetByPath` gives an asset of an existing path with state `Updating`, null for a missing one,
and `new Asset(path)` state `None` (`MetaFile` for a `.meta`); a folder's path ends in `/` with
empty names; paths under `Assets`, `Packages` and `ProjectSettings` are versioned, others not.

Unbound (`managed/UnityEngine/Editor/VersionControl.cs`) answers the same; a project whose settings
choose a provider is not supported (NotSupportedException).

Record and gate:

    python3 tests/projects/vcsqueries/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/vcsqueries/run.py publish A B
    python3 tests/projects/vcsqueries/run.py gate
