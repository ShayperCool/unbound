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

`residuals.json` lists the known differences by reason with the value Unbound gives; a listed row
with that value is printed and counted, and a listed row with another value, like every other
difference, fails. Unbound keeps the engine API in one assembly (docs/COMPATIBILITY_GAPS.md, Q694): a
type Unity keeps in another module names `UnityEngine.CoreModule` (3382 type rows, and 9 `assembly.*`
and `getType.*` rows), and a type of another module named with the core module resolves (3 rows). Six compiler-generated fixed-buffer types of
private fields are not declared. Unity's `UnityEngine.dll` defines its own obsolete `ADBannerView` and
`ADInterstitialAd` without their nested types, which `Type.GetType` through `UnityEngine` finds (16 rows).

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/enginetypemodules/run.py list --label types        # authoring: types.txt
python3 tests/projects/enginetypemodules/run.py native --label reference-a
python3 tests/projects/enginetypemodules/run.py native --label reference-b
python3 tests/projects/enginetypemodules/run.py publish reference-a reference-b
python3 tests/projects/enginetypemodules/run.py check
```
