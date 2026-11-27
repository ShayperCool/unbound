# packagemanager

Measures `UnityEditor.PackageManager` value and error behaviour on Unity 6000.0.71f1:
- `PackageInfo.versions` (`VersionsInfo`: `all`, `compatible`, `recommended`, `verified`,
  `deprecated`, `latest`, `latestCompatible`) and `PackageInfo.errors` for embedded, local, and
  built-in packages.
- `Request.Error` and `PackageCollection.error` on `Client.List(offlineMode: true)` and
  `Client.Search(..., offlineMode: true)`.
- Synchronous argument validation on `Client.Search`, `Client.Add`, `Client.Remove`,
  `Client.AddAndRemove`, and `Client.Embed`.
- Completed failure requests and `Error` payloads for `Client.Remove` on an uninstalled package
  and `Client.Embed` on an already-embedded package.

## Recording

```bash
python3 tests/projects/packagemanager/run.py record --label run1
python3 tests/projects/packagemanager/run.py record --label run2
diff -u ~/.cache/unbound/validation/runs/packagemanager/run1/capture/values.jsonl \
        ~/.cache/unbound/validation/runs/packagemanager/run2/capture/values.jsonl
python3 tests/projects/packagemanager/run.py publish run1 run2
```

## Checking

```bash
python3 tests/projects/packagemanager/run.py gate
```
