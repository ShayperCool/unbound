# AnimationCurve binding probe (`curvebinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `AnimationCurve` `SerializedProperty`
binding through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`,
`BindingExtensions.Unbind`, `UnityEditor.UIElements.CurveField`, and
`UnityEditor.UIElements.PropertyField`:

- `CurveField` standalone defaults (`ranges`, `renderMode`, USS classes, `unity-border` visual
  child), independent copy semantics on `value` get/set, equality/no-op checks across keys,
  tangents, weights, and wrap modes, `null` value handling, and `renderMode` child hierarchy.
- Direct `CurveField.BindProperty`: empty and non-empty initial sync, two-way `UI -> SO` and
  `SO -> UI` assignment (including wrap-mode-only edits and direct in-place target curve
  mutation), independent instances between `CurveField`, `SerializedObject`, and targets,
  `null` assignment while bound, and detached-then-attached deferred binding.
- Multi-object mixed values on `CurveField` and `PropertyField`: divergent keys and wrap-mode-only
  divergence, `showMixedValue` and `mixedValueLabel`, editing mixed targets to a new curve or
  target[0]'s value, and target instance independence after multi-edit.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `AnimationCurve` paths alongside sibling `BoundsInt`, `Bounds`, `RectInt`, `Color`,
  `Vector3`, scalar, ordinary enum, and `[Flags]` enum fields.
- `PropertyField` generated `CurveField` child type, name, nicified/custom/dynamic label, USS
  classes, `RegisterValueChangeCallback`, mixed multi-object state, and rebind across
  `AnimationCurve` properties (child reuse), `BoundsInt`, `Bounds`, `RectInt`, `Color`,
  `Vector3`, `Vector4`, and scalar properties.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/curvebinding/run.py record --label native-a
python3 tests/projects/curvebinding/run.py record --label native-b
python3 tests/projects/curvebinding/run.py publish native-a native-b
python3 tests/projects/curvebinding/run.py gate
```
