# Vector2Int binding probe (`vector2intbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Vector2Int` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.Vector2IntField`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default, non-zero negative/positive, and `int.MinValue`/`int.MaxValue`),
  no-op whole-vector and per-component writes, and detached-then-attached deferred binding.
- Whole-vector and individual component (`x`, `y`) edits in both directions (`UI -> SO` via
  `Vector2IntField.value` and sub-`IntegerField` values; `SO -> UI` via `vector2IntValue`,
  component `FindPropertyRelative("x"|"y").intValue`, and direct target mutation polling).
- Multi-object mixed values on `Vector2IntField` and `PropertyField`: per-component
  `showMixedValue` and text on `unity-x-input`/`unity-y-input` vs whole-field `showMixedValue`,
  per-component edits preserving untouched components on each target, writing target[0]'s existing
  component value on a mixed component, whole-vector edits changing one or both axes on mixed
  targets, and direct `IntegerField` binding to `multiVec.x`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Vector2Int` paths alongside sibling scalar, ordinary enum, `[Flags]` enum, `Vector2`,
  `Vector3`, and `Vector4` fields.
- `PropertyField` generated `Vector2IntField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `Vector2Int`
  properties (child reuse), `Vector2`, `Vector4`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/vector2intbinding/run.py record --label native-a
python3 tests/projects/vector2intbinding/run.py record --label native-b
python3 tests/projects/vector2intbinding/run.py publish native-a native-b
python3 tests/projects/vector2intbinding/run.py gate
```
