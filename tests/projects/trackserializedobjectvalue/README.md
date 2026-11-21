# trackserializedobjectvalue

Measures Unity 6 `UnityEditor.UIElements.BindingExtensions.TrackSerializedObjectValue` and
`UnityEditor.UIElements.SerializedObjectChangeEvent` on Unity `6000.0.71f1` against a live
UI Toolkit `Panel`.

## Covered cases

- `SerializedObjectChangeEvent` defaults (`bubbles`, `tricklesDown`, `changedObject`).
- Argument validation (`TrackSerializedObjectValue` with null `SerializedObject` or null element).
- Unchanged `SerializedObject` across panel updates produces no callbacks.
- Control vs `TrackPropertyValue`: editing an unrelated property triggers `TrackSerializedObjectValue` while keeping `TrackPropertyValue` on another property silent; editing the tracked property triggers both.
- Applied edits, unapplied buffer edits, revert-before-update edits, direct target field edits, and nested struct / array edits.
- Immediate mutation right after `TrackSerializedObjectValue` before the first panel update (`ApplyModifiedProperties` and direct field mutation).
- Null-callback `TrackSerializedObjectValue` dispatching bubbling `SerializedObjectChangeEvent`.
- Multiple object callbacks and coexistence with `TrackPropertyValue` on the same element/object, plus `NotSupportedException` when tracking a second `SerializedObject` on the same element.
- Unsubscription via `element.Unbind()` (allowing rebinding to a different `SerializedObject`) and ancestor `container.Unbind()`.
- Panel detach (`RemoveFromHierarchy`) suppressing callbacks while detached, plus re-attach behavior.
- Deferred registration on a detached element and cancellation via `Unbind()` before attachment.
- Clean cleanup when the tracked `SerializedObject` is disposed.
- Two-object `SerializedObject`: secondary edit without and with `SetIsDifferentCacheDirty()`, and primary target edit.
- Coexistence control with runtime `DataBinding`: `Unbind()` stops `TrackSerializedObjectValue` while preserving runtime `DataBinding`.

## Running

```bash
python3 tests/projects/trackserializedobjectvalue/run.py record --label native-a
python3 tests/projects/trackserializedobjectvalue/run.py record --label native-b
python3 tests/projects/trackserializedobjectvalue/run.py publish native-a native-b
python3 tests/projects/trackserializedobjectvalue/run.py gate
```
