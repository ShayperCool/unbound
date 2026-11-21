# PropertyField (supported scalar SerializedProperties)

Measures Unity 6 `UnityEditor.UIElements.PropertyField` for already-supported scalar
`SerializedProperty` types (`bool`, `int`, `long`, `uint`, `ulong`, `float`, `double`, `string`).

## Scope

- Constructors (`PropertyField()`, `PropertyField(SerializedProperty)`,
  `PropertyField(SerializedProperty, string)`), default properties, and USS classes.
- Child scalar field creation (`Toggle`, `IntegerField`, `LongField`, `UnsignedIntegerField`,
  `UnsignedLongField`, `FloatField`, `DoubleField`, `TextField`), naming (`unity-input-<path>`),
  `displayName` and custom/empty/dynamic `label` updates, `[Tooltip]` propagation, and field USS
  classes.
- Initial synchronization through `Bind(SerializedObject)`, `BindProperty(SerializedProperty)`,
  `BindProperty(SerializedObject)`, detached-before-attach deferred binding, nested
  `BindableElement` relative paths, and missing `bindingPath`.
- Two-way property/field updates, `RegisterValueChangeCallback(EventCallback<SerializedPropertyChangeEvent>)`
  target filtering vs bubbling `SerializedPropertyChangeEvent`, and unrelated property edits.
- Rebinding (same field type instance reuse, cross-width integer field replacement, cross-type
  replacement, and second `SerializedObject`).
- `Unbind()` (element, parent, and detached cancel-before-attach), multi-object mixed state, and
  runtime `DataBinding` coexistence.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl` comes from two independent Unity `6000.0.71f1` Linux GPU
Editor captures that matched byte-for-byte.

## Commands

```bash
python3 tests/projects/propertyfield/run.py record --label native-a
python3 tests/projects/propertyfield/run.py record --label native-b
python3 tests/projects/propertyfield/run.py publish native-a native-b
python3 tests/projects/propertyfield/run.py gate
```
