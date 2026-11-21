# Gradient binding probe (`gradientbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Gradient` `SerializedProperty`
binding through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`,
`BindingExtensions.Unbind`, `UnityEditor.UIElements.GradientField`, and
`UnityEditor.UIElements.PropertyField` (including `[GradientUsageAttribute]`):

- `GradientField` standalone defaults (`hdr`, `colorSpace`, USS classes, background and content
  visual children), independent copy semantics on `value` get/set, equality/no-op checks across
  color keys, alpha keys, mode, key-time-only changes, and `null` value handling.
- Direct `GradientField.BindProperty`: default and non-default initial sync, two-way `UI -> SO`
  and `SO -> UI` assignment (including mode-only, alpha-only, same-key-times color-only, and
  HDR color edits, plus direct in-place target mutation and full replacement), independent
  instances between `GradientField`, `SerializedObject`, and targets, and detached-then-attached
  deferred binding.
- Multi-object mixed values on `GradientField` and `PropertyField`: divergent color keys,
  mode-only divergence, and alpha-only divergence, `showMixedValue` and `mixedValueLabel`, editing
  mixed targets to a new gradient or target[0]'s value, and target instance independence after
  multi-edit.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Gradient` paths alongside sibling `AnimationCurve`, `BoundsInt`, `Bounds`, `RectInt`,
  `Color`, `Vector3`, scalar, ordinary enum, and `[Flags]` enum fields.
- `PropertyField` generated `GradientField` for plain `Gradient` and `[GradientUsageAttribute]`
  (`hdr`, `colorSpace`) properties: child type, name, nicified/custom/dynamic label, USS classes,
  `RegisterValueChangeCallback`, mixed multi-object state, plain-to-plain reuse, plain-to-usage
  and usage-to-plain rebinding, and rebind across `AnimationCurve`, `BoundsInt`, `Color`,
  `Vector3`, `Vector4`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/gradientbinding/run.py record --label native-a
python3 tests/projects/gradientbinding/run.py record --label native-b
python3 tests/projects/gradientbinding/run.py publish native-a native-b
python3 tests/projects/gradientbinding/run.py gate
```
