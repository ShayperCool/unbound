# Bundle manifest adapters

Unity 6000.0.71f1, 913 exact checks. Two independent GPU Editor captures agree byte
for byte. The captures used the NVIDIA driver only; no graphics device behavior is
inferred from these API controls.

The runner stages the committed `assetbundles/Reference/bundles` fixtures into each
private project. It never rebuilds bundles or depends on an archived local run.
It exercises nondefault hashes and dependencies in LZ4, LZMA and uncompressed
manifest bundles, returned-array mutation, clone identity/name independence,
`Unload(false)`, reload, `Unload(true)` and clone survival. Unknown, null, mixed-case,
space and Unicode-lookalike names separate the measured lookup rules. Four distinct
source hide-flag values all produce `HideAndDontSave` on their clones.

Unity's [manifest API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetBundleManifest.html)
provides bundle names, hashes and dependencies. [Unload](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetBundle.Unload.html)
distinguishes retained loaded objects from object destruction. The probe observes
those contracts with live manifest objects before and after each operation. Clone
ownership and ASCII name matching are selected-release observations. Returned-array
order is unspecified by the API documentation; its measured order is not a guarantee
for callers.

Set `UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1` for each command:

```sh
python3 tests/projects/manifestadapters/run.py native --label native-a
python3 tests/projects/manifestadapters/run.py native --label native-b
python3 tests/projects/manifestadapters/run.py publish native-a native-b
python3 tests/projects/manifestadapters/run.py check
```

The gate runs only the engine against the committed reference. The original bundle
fixtures and all earlier probe references are unchanged. Variant payload copying
also has a nondefault managed test; the three existing bundle fixtures have no
variants. This slice does not claim parity for new bundle build targets or every
possible dependency graph.
