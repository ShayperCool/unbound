# Vector3Int binding probe (`vector3intbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Vector3Int` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.Vector3IntField`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default, non-zero negative/positive, and `int.MinValue`/`int.MaxValue`),
  no-op whole-vector and per-component writes, and detached-then-attached deferred binding.
- Whole-vector and individual component (`x`, `y`, `z`) edits in both directions (`UI -> SO` via
  `Vector3IntField.value` and sub-`IntegerField` values; `SO -> UI` via `vector3IntValue`,
  component `FindPropertyRelative("x"|"y"|"z").intValue`, and direct target mutation polling).
- Multi-object mixed values on `Vector3IntField` and `PropertyField`: per-component
  `showMixedValue` and text on `unity-x-input`/`unity-y-input`/`unity-z-input` vs whole-field
  `showMixedValue`, per-component edits proving untouched components stay preserved on each target,
  writing target[0]'s existing component value on a mixed component, whole-vector edits changing
  one or all axes on partially mixed targets, and direct `IntegerField` binding to `multiVec.y`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Vector3Int` paths alongside sibling scalar, ordinary enum, `[Flags]` enum, `Vector2Int`,
  `Vector2`, `Vector3`, and `Vector4` fields.
- `PropertyField` generated `Vector3IntField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `Vector3Int`
  properties (child reuse), `Vector2Int`, `Vector3`, `Vector4`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/vector3intbinding/run.py record --label native-a
python3 tests/projects/vector3intbinding/run.py record --label native-b
python3 tests/projects/vector3intbinding/run.py publish native-a native-b
python3 tests/projects/vector3intbinding/run.py gate
```
