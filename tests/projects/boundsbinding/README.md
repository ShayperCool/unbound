# Bounds binding probe (`boundsbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Bounds` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.BoundsField`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default, non-zero, and negative extents), no-op whole-`Bounds` and
  sub-component writes, and detached-then-attached deferred binding.
- Whole-`Bounds`, sub-vector (`m_Center`, `m_Extent`), and per-axis (`x`, `y`, `z`) edits in both
  directions (`UI -> SO` via `BoundsField.value`, child `Vector3Field`s, and axis `FloatField`s;
  `SO -> UI` via `boundsValue`, `FindPropertyRelative("m_Center"|"m_Extent")`, axis float
  properties, and direct target mutation polling).
- Multi-object mixed values on `BoundsField` and `PropertyField`: per-axis `showMixedValue` and
  text across nested `unity-m_Center-input` and `unity-m_Extent-input` `Vector3Field`s vs outer
  and intermediate `showMixedValue`, sub-field and whole-`Bounds` edits on divergent mixed
  targets, writing target[0]'s existing component value, direct `Vector3Field` binding to
  `multiBounds.m_Center`, and direct `FloatField` binding to `multiBounds.m_Extent.z`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Bounds` paths alongside sibling `RectInt`, `Rect`, `Color`, `Vector3`, scalar,
  ordinary enum, and `[Flags]` enum fields.
- `PropertyField` generated `BoundsField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `Bounds`
  properties (child reuse), `RectInt`, `Rect`, `Color`, `Vector3`, `Vector4`, and scalar
  properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/boundsbinding/run.py record --label native-a
python3 tests/projects/boundsbinding/run.py record --label native-b
python3 tests/projects/boundsbinding/run.py publish native-a native-b
python3 tests/projects/boundsbinding/run.py gate
```
