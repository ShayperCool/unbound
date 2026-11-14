# Asset loading and clone adapters

Unity 6000.0.71f1, recorded twice with the NVIDIA GPU. Both captures contain 145 byte-identical rows. The gate compares every row exactly and assembles the committed Editor-authored Resources assets without starting the Editor.

PhysicsMaterial, PhysicsMaterial2D and TextAsset have no declared interfaces in the selected release. The engine keeps their load/copy operations in external adapters. Loading, instantiation, class diagnostics and field-coverage inspection share those adapters; other asset types retain their existing dispatch until their migrations are complete. The selected declarations come from Unbound.ApiTools.StubGenerator.

The [Object.Instantiate API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html) documents copying objects. Nondefault authored material fields and text bytes load through Resources, then clones preserve those values and have distinct identities. Changing clone friction and bounciness leaves the originals unchanged. Returned text byte arrays are independent, and a runtime text clone survives destruction of its original. Runtime materials and nonfinite values provide controls outside the authored asset setup. The unit controls also check derived reader dispatch, binary text snapshot ownership, doctor classification and an unmigrated texture clone.

The scalar controls record five properties with ten inputs each, storing exact float bits and diagnostic messages. The [3D static-friction API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsMaterial-staticFriction.html) and [2D friction API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsMaterial2D-friction.html) describe material properties; the limits below are observations for this release.

| Writes | Observed behavior |
| --- | --- |
| 3D dynamic/static friction | Clamp to zero through the largest accepted finite friction value, one representable float below float.MaxValue. Out-of-range values log an error. NaN is stored. |
| 3D bounciness | Clamp to zero through one, with the same diagnostics and NaN handling. |
| 2D friction/bounciness | Finite values clamp silently to zero through 100,000. Nonfinite inputs log an error and preserve the preceding nondefault value. |

Diagnostics are captured through both Debug.unityLogger.logHandler and the native log callback, deduplicating the same delivery. These controls measure property setters, not normalization of arbitrary YAML fields. The shared validation mechanism is enabled through Compat for 6000.0.71f1. Other releases keep their previous, unmeasured storage behavior. Rendering and contact-solver parity are outside this probe.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/assetadapters/run.py native --label reference-a
python3 tests/projects/assetadapters/run.py native --label reference-b
python3 tests/authored_project.py record assetadapters default --release "$UNBOUND_PROBE_UNITY_VERSION" --project <reference-a>/project --command 'python3 tests/projects/assetadapters/run.py native --label reference-a'
python3 tests/projects/assetadapters/run.py publish reference-a reference-b
python3 tests/projects/assetadapters/run.py check
```

Captures, logs and engine outputs stay outside Git. Existing probe references and comparator tolerances are unchanged.
