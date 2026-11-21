# Color binding probe (`colorbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Color` and `Color32` `SerializedProperty`
binding through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`,
`BindingExtensions.Unbind`, `UnityEditor.UIElements.ColorField`, and
`UnityEditor.UIElements.PropertyField`:

- `ColorField` constructor defaults (`value`, `showAlpha`, `hdr`, `showEyeDropper`,
  `showMixedValue`, visual child structure and display styles) and `showMixedValue` toggling.
- Initial sync (zero/clear default, non-zero RGBA, HDR > 1 channels, `Color32`), no-op writes,
  detached-then-attached deferred binding, and two-way RGBA sync (`UI -> SO`, `SO -> UI` whole
  color and per-component `r`/`g`/`b`/`a`, and direct target mutation polling).
- Multi-object mixed values on `ColorField` and `PropertyField` (divergent RGB, alpha-only
  divergence, writing target[0]'s value vs new value, `Color32` mixed values, and direct
  `FloatField` binding to `multiColor.r`).
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Color` paths alongside sibling `Vector3Int`, `Vector2`, scalar, ordinary enum, and
  `[Flags]` enum fields.
- `PropertyField` for plain `Color`, `Color32`, and `[ColorUsageAttribute]` combinations
  (`showAlpha`/`hdr`), checking generated `ColorField` properties, labels, USS classes,
  `RegisterValueChangeCallback`, child reuse vs replacement on rebind, and mixed multi-object
  editing.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/colorbinding/run.py record --label native-a
python3 tests/projects/colorbinding/run.py record --label native-b
python3 tests/projects/colorbinding/run.py publish native-a native-b
python3 tests/projects/colorbinding/run.py gate
```
