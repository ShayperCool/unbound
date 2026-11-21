# Drawer dispatch probe

Records how a Unity 6000.0.71f1 `PropertyField` builds the GUI of a property's drawers:

- `lookup.*`: `[CustomPropertyDrawer]` by the field's type, a base type with and without
  `useForChildren`, an interface, a generic type definition and a `PropertyAttribute`, with the
  drawer's `preferredLabel`, `fieldInfo` and `attribute`.
- `nesting.ordered`: two drawer attributes whose drawers each build a `PropertyField` of the same
  property inside: which drawer comes first, then the next, then the default field.
- `imgui`: a drawer with only `OnGUI` (the IMGUI fallback container).
- `label.*`: the label a drawer gets from `PropertyField(property, label)`.
- `collection.*`: an attribute's drawer on an array's elements, and one that applies to the
  collection itself.
- `decorator.*`: `[Header]`, `[Space]`, a custom decorator with UI Toolkit GUI and one with only IMGUI,
  on a field and on an array.
- `builtin.*`: the `[Range]`, `[Min]`, `[Delayed]`, `[Multiline]` and `[TextArea]` drawers.
- `instances`: drawer instances created for two fields of one property.
- `composite`: a drawer that builds its children's `PropertyField`s.

## Usage

```bash
python3 tests/projects/drawerdispatch/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/drawerdispatch/run.py publish run-a run-b
python3 tests/projects/drawerdispatch/run.py gate
```

The values depend on neither the OS nor the graphics device (`macos_batch`); the panel has a
constant pixel size.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical captures.
