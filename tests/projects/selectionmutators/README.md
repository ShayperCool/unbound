# Editor selection mutation

The Unity 6000.0.71f1 batch Play-mode probe calls both `Selection.Add` and `Selection.Remove`
overloads and records the selected IDs, resolved objects, active object, context and ordering.
The controls cover duplicate and missing entries, null and destroyed wrappers, explicit zero
and unresolved IDs, the array setters, and repeating the current active object or ID. Destroying
or unloading a selected object removes its ID. The ScriptableObject control checks that reloading
an unloaded asset reuses its instance ID without selecting it again.

Active component setters select the owner and keep the component as the active object. Array
setters retain the explicit component entry. The asset controls distinguish eager loading of the
active ID from resolved reads of other IDs and from GUID inspection. Unloading a selected material
clears its entry while its original wrapper stays usable. A separate sequence of editor updates
separates selected-entry destruction from context-only destruction and measures their notifications.

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
