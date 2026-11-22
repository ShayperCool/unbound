# Engine type modules

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical. The values are reflected type names, so they do not depend on the OS or the graphics
device.

- `type.<name>`: the assembly-qualified name of every public type (nested ones too) of the engine's
  module assemblies (`UnityEngine.*Module`, `UnityEditor.*Module`), reached by name through the
  umbrella assembly (`UnityEngine` or `UnityEditor`). The input list, `types.txt` (4562 types), is
  written by the Editor (`run.py list`).
- `getType.*`: `Type.GetType` of a type named with its module, the umbrella, `UnityEngine.CoreModule`
  and no assembly (Rigidbody, AudioSource, Transform, EditorWindow).
- `assembly.*`: Rigidbody's assembly, whether it is Transform's, and which defines Rigidbody.
- `event.argument.*`: the type name a UnityEvent stores (as JsonUtility writes it) for an object
  argument of an engine component or a script object: the umbrella name, not the module.

A type Unbound does not implement answers `<null>`; the comparison lists such rows as missing (API
coverage) and fails on every other difference.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/enginetypemodules/run.py list --label types        # authoring: types.txt
python3 tests/projects/enginetypemodules/run.py native --label reference-a
python3 tests/projects/enginetypemodules/run.py native --label reference-b
python3 tests/projects/enginetypemodules/run.py publish reference-a reference-b
python3 tests/projects/enginetypemodules/run.py check
```
