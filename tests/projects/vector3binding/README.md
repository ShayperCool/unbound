# Vector3 binding probe (`vector3binding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Vector3` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.Vector3Field`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default and non-zero values), no-op whole-vector and per-component writes,
  and detached-then-attached deferred binding.
- Whole-vector and individual component (`x`, `y`, `z`) edits in both directions (`UI -> SO` via
  `Vector3Field.value` and sub-`FloatField` values; `SO -> UI` via `vector3Value`, component
  `FindPropertyRelative("x"|"y"|"z").floatValue`, and direct target mutation polling).
- Multi-object mixed values on `Vector3Field` and `PropertyField`: per-component `showMixedValue`
  and text on `unity-x-input`/`unity-y-input`/`unity-z-input` vs whole-field `showMixedValue`,
  per-component edits preserving untouched mixed components on each target, writing target[0]'s
  existing component value on a mixed component, whole-vector edits on mixed targets, single-axis
  unified-to-mixed transitions, and direct `FloatField` binding to `vec.x`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Vector3` paths alongside sibling scalar, ordinary enum, and `[Flags]` enum fields.
- `PropertyField` generated `Vector3Field` child type, name, nicified/custom label, USS classes,
  `RegisterValueChangeCallback`, dynamic `label` updates, mixed multi-object state, and rebind
  across `Vector3` properties (child reuse) and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/vector3binding/run.py record --label native-a
python3 tests/projects/vector3binding/run.py record --label native-b
python3 tests/projects/vector3binding/run.py publish native-a native-b
python3 tests/projects/vector3binding/run.py gate
```
