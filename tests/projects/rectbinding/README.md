# Rect binding probe (`rectbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Rect` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.RectField`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default, non-zero, and negative width/height), no-op whole-rect and
  per-component writes, and detached-then-attached deferred binding.
- Whole-rect and individual component (`x`, `y`, `width`, `height`) edits in both directions
  (`UI -> SO` via `RectField.value` and sub-`FloatField` values; `SO -> UI` via `rectValue`,
  component `FindPropertyRelative("x"|"y"|"width"|"height").floatValue`, and direct target
  mutation polling).
- Multi-object mixed values on `RectField` and `PropertyField`: per-component `showMixedValue`
  and text on `unity-x-input`/`unity-y-input`/`unity-width-input`/`unity-height-input` vs
  whole-field `showMixedValue`, sub-field and whole-rect edits on divergent mixed targets,
  writing target[0]'s existing component value, and direct `FloatField` binding to
  `multiRect.width`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Rect` paths alongside sibling `Color`, `Vector3Int`, `Vector2`, scalar, ordinary enum,
  and `[Flags]` enum fields.
- `PropertyField` generated `RectField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `Rect`
  properties (child reuse), `Color`, `Vector3Int`, `Vector4`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/rectbinding/run.py record --label native-a
python3 tests/projects/rectbinding/run.py record --label native-b
python3 tests/projects/rectbinding/run.py publish native-a native-b
python3 tests/projects/rectbinding/run.py gate
```
