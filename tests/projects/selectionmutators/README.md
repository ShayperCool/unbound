# Editor selection mutation

The Unity 6000.0.71f1 batch Play-mode probe calls both `Selection.Add` and `Selection.Remove`
overloads and records the selected IDs, resolved objects, active object, context and ordering.
The controls cover duplicate and missing entries, null and destroyed wrappers, explicit zero
and unresolved IDs, the array setters, and repeating the current active object or ID. Destroying
or unloading a selected object removes its ID. The ScriptableObject control checks that reloading
an unloaded asset reuses its instance ID without selecting it again.

The startup batch has one known callback timing difference. Unity records zero calls after the
batch, and Unbound publishes one deferred notification. The replay prints that exact difference
alongside any unexpected result. The probe measures batch operation and selection state. Other
release families keep their existing selection behavior and warn when a mutation helper is used.

```sh
python3 tests/projects/selectionmutators/run.py record --label a
python3 tests/projects/selectionmutators/run.py record --label b
python3 tests/projects/selectionmutators/run.py publish a b
python3 tests/projects/selectionmutators/run.py gate
```
