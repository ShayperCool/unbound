# BoundsInt binding probe (`boundsintbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `BoundsInt` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.BoundsIntField`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default, non-zero, negative size, and extreme `int` values), no-op
  whole-`BoundsInt` and sub-component writes, and detached-then-attached deferred binding.
- Whole-`BoundsInt`, sub-vector (`m_Position`, `m_Size`), and per-axis (`x`, `y`, `z`) edits in
  both directions (`UI -> SO` via `BoundsIntField.value`, child `Vector3IntField`s, and axis
  `IntegerField`s; `SO -> UI` via `boundsIntValue`, `FindPropertyRelative("m_Position"|"m_Size")`,
  axis int properties, and direct target mutation polling).
- Multi-object mixed values on `BoundsIntField` and `PropertyField`: per-axis `showMixedValue`
  and text across nested `unity-m_Position-input` and `unity-m_Size-input` `Vector3IntField`s vs
  outer and intermediate `showMixedValue`, sub-field and whole-`BoundsInt` edits on divergent
  mixed targets, writing target[0]'s existing component value, direct `Vector3IntField` binding
  to `multiBoundsInt.m_Position`, and direct `IntegerField` binding to `multiBoundsInt.m_Size.z`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `BoundsInt` paths alongside sibling `Bounds`, `RectInt`, `Rect`, `Color`, `Vector3Int`,
  scalar, ordinary enum, and `[Flags]` enum fields.
- `PropertyField` generated `BoundsIntField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `BoundsInt`
  properties (child reuse), `Bounds`, `RectInt`, `Rect`, `Color`, `Vector3Int`, `Vector4`, and
  scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/boundsintbinding/run.py record --label native-a
python3 tests/projects/boundsintbinding/run.py record --label native-b
python3 tests/projects/boundsintbinding/run.py publish native-a native-b
python3 tests/projects/boundsintbinding/run.py gate
```
