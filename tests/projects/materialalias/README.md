# Material reload wrappers

Two independent Unity 6000.0.71f1 Editor captures on the existing NVIDIA display
agree on all 62 values. The cold and touched controls unload separate saved
materials, then check their wrapper identities and shared values.

```bash
python3 tests/projects/materialalias/run.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/materialalias/run.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/path/to/immutable/unbound python3 tests/projects/materialalias/run.py check --release 6000.0.71f1
```

The runner stages private project copies. Captures and engine output stay in the
run cache; the gate compares every value exactly with the canonical reference.

Unity 6000.0 documents that
[Resources.UnloadAsset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Resources.UnloadAsset.html)
unloads an individual disk asset. These controls distinguish the measured wrapper
behavior from that contract. The API remarks describe an invalid, disconnected
old instance; the measured Editor materials instead retain connected wrappers.
Later loads give a new managed wrapper with the same
instance ID. Existing wrappers remain usable and share colors, render queue,
name and flags. Resource queries and instance-ID lookup return one current
wrapper. Copying properties between aliases preserves the values, while an
ordinary material copy has independent state. A second unload restores saved
values, name and flags across every wrapper; destroying the material invalidates
all wrappers.

The original droppedparams reference and comparator remain unchanged. The
compatibility profile selects this wrapper mechanism for 6000.0.71f1. Other
releases retain the previous wrapper behavior and remain unmeasured. Other asset
types, materials sharing a file, lazy UnloadUnusedAssets restoration, unusual GC
patterns and externally changed material files remain outside this capture.

Serialized-property controls retain the field's original wrapper when assigning
an alias with the same instance ID: hasModifiedProperties and
ApplyModifiedProperties both remain false. A different material makes both true.
This tests the documented
[ApplyModifiedProperties result](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedObject.ApplyModifiedProperties.html)
with an actual change as well as the measured same-identity case.
