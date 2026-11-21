# Vector4 binding probe (`vector4binding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `Vector4` `SerializedProperty` binding
through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`, `BindingExtensions.Unbind`,
`UnityEngine.UIElements.Vector4Field`, and `UnityEditor.UIElements.PropertyField`:

- Initial sync (zero default and non-zero values), no-op whole-vector and per-component writes,
  and detached-then-attached deferred binding.
- Whole-vector and individual component (`x`, `y`, `z`, `w`) edits in both directions (`UI -> SO`
  via `Vector4Field.value` and sub-`FloatField` values; `SO -> UI` via `vector4Value`, component
  `FindPropertyRelative("x"|"y"|"z"|"w").floatValue`, and direct target mutation polling).
- Multi-object mixed values on `Vector4Field` and `PropertyField`: per-component `showMixedValue`
  and text on `unity-x-input`/`unity-y-input`/`unity-z-input`/`unity-w-input` vs whole-field
  `showMixedValue`, per-component edits with `z == w` and `z != w` on target[0], whole-vector
  edits on mixed targets, and direct `FloatField` binding to `multiVec.w`.
- Tree `Bind(SerializedObject)` with top-level, nested dotted, relative parent-child, and array
  element `Vector4` paths alongside sibling `Vector2`, `Vector3`, scalar, ordinary enum, and
  `[Flags]` enum fields.
- `PropertyField` for `Vector4` creates a `Foldout` (`unity-foldout-<path>`) bound to
  `isExpanded` with lazy child `PropertyField`s (`x`, `y`, `z`, `w`) populated when expanded:
  measures collapsed vs expanded state, nicified/custom/dynamic title text, bidirectional `FloatField`
  edits, mixed multi-object per-component editing, same-type rebind, cross-type rebind to
  `Vector3`/`IntegerField`, and `Unbind`/rebind.
- `Unbind`, rebind across properties and `SerializedObject`s, runtime `DataBinding` coexistence,
  and incompatible field warning.

## Run

```bash
python3 tests/projects/vector4binding/run.py record --label native-a
python3 tests/projects/vector4binding/run.py record --label native-b
python3 tests/projects/vector4binding/run.py publish native-a native-b
python3 tests/projects/vector4binding/run.py gate
```
