# BindingExtensions.Bind (scalar field trees)

Measures Unity 6 `BindingExtensions.Bind(VisualElement, SerializedObject)` on visual element trees
containing supported scalar fields (`Toggle`, `IntegerField`, `LongField`, `UnsignedIntegerField`,
`UnsignedLongField`, `FloatField`, `DoubleField`, `TextField`, `Slider`, `SliderInt`).

## Scope

- Null argument handling (`null` element, `null` `SerializedObject`) and binding directly on a leaf
  `IBindable` root.
- Pre-order hierarchy traversal, valid/empty/missing `bindingPath`, and shared context updates across
  all bound scalar controls in a tree.
- Nested path resolution: `BindableElement` containers for 1-level and 2-level serializable structs,
  root-path fallback inside a scoped container, missing container path keeping outer scope, and
  array element relative paths.
- Bind-before-attach deferred binding, `Unbind()` cancelling a pending detached `Bind()`, and
  late-added children (added after attached `Bind`, added before deferred attach, and bound on
  subsequent `Bind`).
- `Unbind()` and rebind on the tree (`Bind` to a second `SerializedObject`, nested subtree bound to
  a different `SerializedObject`).
- Interaction and independence between explicit `BindProperty` registrations and tree `Bind`/`Unbind`
  (different `SerializedObject` with unique or overlapping path, `BindProperty` after tree `Bind`,
  and same `SerializedObject`).
- Multi-object mixed state through tree `Bind`, and runtime `DataBinding` coexistence across tree
  `Bind` and `Unbind`.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl` comes from two independent Unity `6000.0.71f1` Linux GPU
Editor captures that matched byte-for-byte.

## Commands

```bash
python3 tests/projects/bindtree/run.py record --label native-a
python3 tests/projects/bindtree/run.py record --label native-b
python3 tests/projects/bindtree/run.py publish native-a native-b
python3 tests/projects/bindtree/run.py gate
```
