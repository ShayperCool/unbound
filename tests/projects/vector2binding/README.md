# Vector2 binding probe (`vector2binding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Vector2` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.Vector2Field`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default and non-zero values), no-op whole-vector and per-component writes,
  and detached-then-attached deferred binding.
- Whole-vector and individual component (`x`, `y`) edits in both directions (`UI -> SO` via
  `Vector2Field.value` and sub-`FloatField` values; `SO -> UI` via `vector2Value`, component
  `FindPropertyRelative("x"|"y").floatValue`, and direct target mutation polling).
- Multi-object mixed values on `Vector2Field` and `PropertyField`: per-component `showMixedValue`
  and text on `unity-x-input`/`unity-y-input` vs whole-field `showMixedValue`, per-component edits
  preserving untouched components on each target, writing target[0]'s existing component value on a
  mixed component, whole-vector edits changing one or both axes on mixed targets, and direct
  `FloatField` binding to `multiVec.x`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Vector2` paths alongside sibling scalar, ordinary enum, `[Flags]` enum, and `Vector3`
  fields.
- `PropertyField` generated `Vector2Field` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `Vector2`
  properties (child reuse), `Vector3`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/vector2binding/run.py record --label native-a
python3 tests/projects/vector2binding/run.py record --label native-b
python3 tests/projects/vector2binding/run.py publish native-a native-b
python3 tests/projects/vector2binding/run.py gate
```
