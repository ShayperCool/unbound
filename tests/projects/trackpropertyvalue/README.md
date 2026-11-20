# trackpropertyvalue

Measures Unity 6 `UnityEditor.UIElements.BindingExtensions.TrackPropertyValue` and
`BindingExtensions.Unbind` (plus `SerializedPropertyChangeEvent`) on Unity `6000.0.71f1`
against a live UI Toolkit `Panel`.

## Covered cases

- Constant and `SerializedPropertyChangeEvent` defaults (`prefabOverrideUssClassName`, pooling, bubbling/trickling).
- Argument validation (`TrackPropertyValue` with null property or null element; `Unbind` with null element).
- Unchanged properties across panel updates produce no callbacks.
- Tracked vs unrelated property edits on the same `SerializedObject`, unapplied buffer writes, revert-before-update writes, and direct target mutations detected via panel update.
- Nested struct properties (leaf and parent), array elements, and array size changes.
- Immediate mutation right after `TrackPropertyValue` before the first panel update.
- Null-callback `TrackPropertyValue` dispatching bubbling `SerializedPropertyChangeEvent`.
- Multiple callbacks on the same element/property and `NotSupportedException` when tracking a second `SerializedObject` on the same element.
- Unsubscription via `element.Unbind()` (allowing rebinding to a different `SerializedObject`) and ancestor `container.Unbind()`.
- Panel detach (`RemoveFromHierarchy`) suppressing callbacks while detached and re-attach resuming tracking.
- Deferred registration on a detached element (and cancellation via `Unbind()` before attachment).
- Clean unbinding when the tracked `SerializedObject` is disposed.
- Multi-object `SerializedObject` `hasMultipleDifferentValues` transition.
- Coexistence control with runtime `DataBinding`: `Unbind()` clears `SerializedProperty` tracking while preserving runtime `DataBinding`.

## Running

```bash
python3 tests/projects/trackpropertyvalue/run.py record --label native-a
python3 tests/projects/trackpropertyvalue/run.py record --label native-b
python3 tests/projects/trackpropertyvalue/run.py publish native-a native-b
python3 tests/projects/trackpropertyvalue/run.py gate
```
