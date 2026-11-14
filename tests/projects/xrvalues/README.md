# XR value payloads

Unity 6000.0.71f1, recorded twice on the NVIDIA GPU with no XR provider. The canonical reference has 898 byte-identical rows. The gate compares every missing, extra or unequal row exactly.

Bone, Eyes and Hand store a device identifier and feature index. Six independently initialized identities per type distinguish both components, including high bits and maximum values. Typed/object equality, both operators, hashing, HashSet and Dictionary lookups exercise these values. Copies can change without changing the original. HapticCapabilities stores all six capabilities; one-field changes test equality even where the selected component hash has collisions. Internal property writes and unmanaged round trips preserve the complete payload.

XRNodeState stores node identity, tracking state, six tracking fields and availability bits. The probe sets each field independently, then clears availability while retaining nondefault stored values. TryGet returns zero vectors or identity rotation for unavailable fields and the stored data for available fields. Setting tracked false leaves explicitly available data readable in the measured standalone value context. Raw tracked values -1 and 2 are false; only 1 is true. Copies retain their own data and flags. Nonfinite vectors remain unchanged.

Each type checks layout kind, field count, offsets, size, nondefault values and unmanaged restoration. Sizes are 16 bytes per feature handle, 24 bytes for capabilities and 96 bytes for node state. Padding bytes are not values and are not compared. Runtime default packing is the separate framework discrepancy recorded in `yieldlayouts`; this probe checks the actual offsets and payload transfers.

The no-provider control starts with a populated device list, and confirms enumeration clears it. Invalid-device calls overwrite previously nondefault Hand, Eyes, Bone and capability outputs with defaults. Physical feature-provider queries on handle types remain unsupported. This slice adds value storage and identity; it does not add XR hardware support or claim provider behavior on other systems.

| Unity 6000.0 documentation | Applicable guarantee and control |
| --- | --- |
| [XRNodeState](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/XR.XRNodeState.html) | Platforms may expose only some tracking fields. Successive setters and retained values with cleared flags distinguish each field's availability. |
| [TryGetPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/XR.XRNodeState.TryGetPosition.html) | The return value indicates whether an output position is available. Nondefault stored data with its bit cleared distinguishes availability from stored payload. Manually populated standalone values use their bits independently of tracked, as recorded here. |
| [HapticCapabilities](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/XR.HapticCapabilities.html) | The six properties describe distinct device capabilities. Individual nondefault writes, copies and equality controls distinguish them. |
| [Bone](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/XR.Bone.html) | Bone identifies part of a device hierarchy and implements value equality. Distinct device/feature pairs and dictionary lookups test identity without querying a provider. |

The exact hashes, unavailable output values and raw tracked interpretation are selected-release observations, not additional universal API promises. Other releases and actual providers remain unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/xrvalues/run.py native --label reference-a
python3 tests/projects/xrvalues/run.py native --label reference-b
python3 tests/projects/xrvalues/run.py publish reference-a reference-b
python3 tests/projects/xrvalues/run.py check
```

Captures, engine output and raw comparison reports stay in the run cache. Existing references and tolerances are unchanged.
