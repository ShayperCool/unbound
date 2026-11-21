# Flags enum binding probe (`enumflagsbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for `[Flags]` enum `SerializedProperty`
binding through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`,
`BindingExtensions.Unbind`, `UnityEditor.UIElements.EnumFlagsField`, and
`UnityEditor.UIElements.PropertyField`:

- Zero (`0`), single-bit, named composite, multi-bit, signed high-bit (`int.MinValue`), all-bits
  (`-1` / unsigned byte `255`), and undefined-bit masks across enums with and without explicit
  zero/all-bits members, `[InspectorName]`, and `[Obsolete]` entries.
- Direct `EnumFlagsField.BindProperty` (attached and detached), tree `Bind` with nested struct
  fields, bidirectional sync (`UI -> SerializedObject` and `SerializedObject -> UI`), same-type and
  cross-type rebind, `Unbind`, and multi-object mixed value state (`showMixedValue`, `"—"` text,
  and UI resolution including writing target[0]'s existing value).
- `PropertyField` generated `EnumFlagsField` child type, name, label, USS classes, choices/masks,
  value/text, `RegisterValueChangeCallback`, dynamic `label` updates, mixed multi-object state, and
  rebind across `[Flags]` enums (child reuse), ordinary enums (`PopupField<string>`), and scalar
  fields (`IntegerField`).
- Coexistence with runtime `DataBinding` on another property of the same `EnumFlagsField`.

## Run

```bash
python3 tests/projects/enumflagsbinding/run.py record --label native-a
python3 tests/projects/enumflagsbinding/run.py record --label native-b
python3 tests/projects/enumflagsbinding/run.py publish native-a native-b
python3 tests/projects/enumflagsbinding/run.py gate
```
