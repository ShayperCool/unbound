# RectInt binding probe (`rectintbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `RectInt` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.RectIntField`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default, non-zero, negative width/height, and extreme `int` values),
  no-op whole-rect and per-component writes, and detached-then-attached deferred binding.
- Whole-`RectInt` and individual integer component (`x`, `y`, `width`, `height`) edits in both
  directions (`UI -> SO` via `RectIntField.value` and sub-`IntegerField` values; `SO -> UI` via
  `rectIntValue`, component `FindPropertyRelative("x"|"y"|"width"|"height").intValue`, and direct
  target mutation polling).
- Multi-object mixed values on `RectIntField` and `PropertyField`: per-component `showMixedValue`
  and text on `unity-x-input`/`unity-y-input`/`unity-width-input`/`unity-height-input` vs
  whole-field `showMixedValue`, sub-field and whole-`RectInt` edits on divergent mixed targets,
  writing target[0]'s existing component value, and direct `IntegerField` binding to
  `multiRectInt.width`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `RectInt` paths alongside sibling `Rect`, `Color`, `Vector3Int`, `Vector2Int`, scalar,
  ordinary enum, and `[Flags]` enum fields.
- `PropertyField` generated `RectIntField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across `RectInt`
  properties (child reuse), `Rect`, `Color`, `Vector3Int`, `Vector4`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/rectintbinding/run.py record --label native-a
python3 tests/projects/rectintbinding/run.py record --label native-b
python3 tests/projects/rectintbinding/run.py publish native-a native-b
python3 tests/projects/rectintbinding/run.py gate
```
