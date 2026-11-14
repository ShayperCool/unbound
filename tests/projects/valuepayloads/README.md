# Location and retired animation values

Unity 6000.0.71f1, recorded twice on the NVIDIA GPU. The canonical reference contains 113 byte-identical rows. The gate compares every missing, extra or unequal value exactly.

LocationInfo contains a double timestamp and five float measurements in the selected field order, with an unmanaged size of 32 bytes. Three nondefault samples cover ordinary coordinates, values outside geographic ranges, negative zero, NaN, both infinities, large values and a small float. Getter bits, field offsets, raw unmanaged scalar writes and restored getter bits must match. Changing a copied sample leaves the original unchanged. Padding bytes are not compared.

The [LocationInfo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LocationInfo.html) properties expose a captured location sample. The distinct nondefault measurements test each getter rather than accepting constant desktop defaults. [LocationService.lastData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LocationService-lastData.html) represents the last update; this probe also records the empty desktop sample. The change adds value storage, not a GPS provider. It preserves the existing desktop service behavior.

Root JSON is empty and a nested LocationInfo field is omitted. Root and nested overwrite attempts leave the seeded measurements unchanged, while an ordinary sibling field changes from 11 to 21. These are selected-release serialization observations; enabling serialization for its internal sample fields would contradict them.

The public animation module's obsolete AnimationInfo is an empty one-byte value, with null clip and zero weight getters before and after unmanaged restoration. The probe uses the public animation module type explicitly. The legacy facade also declares a separate internal type with the same full name, and direct use of the public value is a compile error because it is obsolete. No clip or weight data is retained in this retired value.

Actual providers and other releases remain unmeasured. Runtime default packing is the shared framework discrepancy recorded by `yieldlayouts`; this probe checks declared size, actual offsets and payload transfers.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/valuepayloads/run.py native --label reference-a
python3 tests/projects/valuepayloads/run.py native --label reference-b
python3 tests/projects/valuepayloads/run.py publish reference-a reference-b
python3 tests/projects/valuepayloads/run.py check
```

Captures, engine outputs and comparisons stay in the run cache. Existing references and tolerances remain unchanged.
