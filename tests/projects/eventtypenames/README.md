# Persistent-call target type names

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 56 rows. The values are serialized JSON and
reflected managed fields, so they do not depend on the OS or the graphics device. The gate compares
every missing, extra or unequal row exactly.

A UnityEvent's persistent calls store their target's type name. The probe reads that name from the
call's own serialized field after each operation:

- Reading JSON: ten stored names cover version, culture and key details in either order, a key
  alone, `UnityEngine.*Module` assembly names (ending in `Module` or not), names that merely contain
  `UnityEngine.` or `Module`, a plain name and an empty one. Each is tidied to the name without the
  details, with module assemblies named `UnityEngine`, or left alone.
- Writing JSON: an untidy name set on the live call is written tidied, and the live call keeps the
  tidied name afterwards.
- A nested event inside a serializable class is read the same way and its sibling field changes.
- Reading the target type name fills an empty or null stored name from the target (a script or an
  engine component, which is named as in `UnityEngine`) and keeps any other stored name; without a
  target it stays null.
- Instantiate copies the tidied name and tidies the source's. A foreign stored name does not stop
  the call from invoking its target.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [ISerializationCallbackReceiver](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ISerializationCallbackReceiver.html) | `write.*.live` changes the live call's field during serialization, and `read.*` changes it after deserialization; a call without the callbacks keeps the untidy names. |
| [UnityEvent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Events.UnityEvent.html) | `invoke.foreignName.*` invokes the persistent call whose stored name names another type. |

Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/eventtypenames/run.py native --label reference-a
python3 tests/projects/eventtypenames/run.py native --label reference-b
python3 tests/projects/eventtypenames/run.py publish reference-a reference-b
python3 tests/projects/eventtypenames/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
