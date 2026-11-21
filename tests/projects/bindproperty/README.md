# BindingExtensions.BindProperty (scalar fields)

Measures Unity 6 `BindingExtensions.BindProperty(IBindable, SerializedObject)` and
`BindingExtensions.BindProperty(IBindable, SerializedProperty)` for scalar UI Toolkit fields
(`Toggle`, `IntegerField`, `LongField`, `UnsignedIntegerField`, `UnsignedLongField`, `FloatField`,
`DoubleField`, `TextField`, `Slider`, `SliderInt`), plus coexistence with `TrackPropertyValue` and
runtime `DataBinding`.

## Scope

- Both overloads: null/missing/empty path handling, `bindingPath` assignment, return value, and
  incompatible field/property type warning.
- Initial sync and `ChangeEvent<T>` dispatch when initial field values differ from or match the
  bound property, detached-before-attach deferred binding, `[Tooltip]` assignment/cleanup, and
  `HideFlags.NotEditable` enabled state.
- Property-to-field updates (`ApplyModifiedProperties`, direct target mutation, unapplied + `Update`),
  field-to-property writeback (immediate apply, no duplicate feedback events, `TrackPropertyValue`
  notification), and unrelated property edits producing no `ChangeEvent`.
- Panel detach/reattach, `Unbind()` on element and parent container, detached request cancelled by
  `Unbind()` before attach, and rebinding to a second `SerializedObject`.
- Multi-object mixed state (`showMixedValue`, silent initial sync while mixed, writing the primary
  value or a new value across all targets, and diverging/converging targets via
  `SetIsDifferentCacheDirty`).
- Runtime `DataBinding` coexistence across `BindProperty` and `Unbind`.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl` comes from two independent Unity `6000.0.71f1` Linux GPU
Editor captures that matched byte-for-byte.

## Commands

```bash
python3 tests/projects/bindproperty/run.py record --label native-a
python3 tests/projects/bindproperty/run.py record --label native-b
python3 tests/projects/bindproperty/run.py publish native-a native-b
python3 tests/projects/bindproperty/run.py gate
```
