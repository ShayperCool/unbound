# Ordinary enum binding probe (`enumbinding`)

Measures Unity 6000.0.71f1 Editor Play-mode behavior for ordinary (non-`[Flags]`) enum
`SerializedProperty` binding through `BindingExtensions.BindProperty`, `BindingExtensions.Bind`,
`BindingExtensions.Unbind`, `UnityEngine.UIElements.EnumField`, `UnityEngine.UIElements.PopupField<string>`,
and `UnityEditor.UIElements.PropertyField`:

- Sparse/nonzero enum values (`10`, `25`, `100`), negative signed values (`-10`, `0`, `50`) with an
  `[Obsolete]` entry, unsigned `byte`-backed enum values (including `255`), duplicate numeric alias
  values, `[InspectorName]` labels, camelCase nicified display names, and `[InspectorOrder]` sorting.
- Uninitialized zero value (`(SparseNonzeroMode)0`) on a nonzero-starting enum.
- Direct `EnumField.BindProperty` (attached and detached), tree `Bind` with nested struct enum and
  `PopupField<string>`, bidirectional sync (`UI -> SerializedObject` and `SerializedObject -> UI`),
  rebind to same enum type and different enum type, `Unbind` cleanup and `PopupField<string>`
  choice/index restoration, and multi-object mixed value state (`showMixedValue`, `"—"` text,
  external convergence, and UI resolution including writing target[0]'s existing value).
- `PropertyField` generated `PopupField<string>` child type, name, label, USS classes, choices,
  index/value/text, `RegisterValueChangeCallback`, dynamic `label` updates, mixed multi-object state,
  rebind across enum properties (child reuse) and enum-to-scalar-to-enum replacement.
- Coexistence with runtime `DataBinding` on another property of the same `EnumField`.

## Run

```bash
python3 tests/projects/enumbinding/run.py record --label native-a
python3 tests/projects/enumbinding/run.py record --label native-b
python3 tests/projects/enumbinding/run.py publish native-a native-b
python3 tests/projects/enumbinding/run.py gate
```
