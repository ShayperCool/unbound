# Binding controls probe

Records Unity 6000.0.71f1 Editor answers that several UI Toolkit binding paths rely on:

- `enum.*`: `SerializedProperty.enumNames`, `enumDisplayNames` and `enumValueIndex` for enums
  declared out of value order, with aliases, an unsigned value with the high bit and a negative value.
- `nicify`: `ObjectNames.NicifyVariableName` for names with digit-x-digit and digit-letter parts.
- `vec4.multi`: multi-object `Vector4Field` edits through its sub-fields, where the first target's
  parts differ from the second's.
- `usage.elements`: `[ColorUsage]` and `[GradientUsage]` on a collection, shown by a `PropertyField`
  of one element.
- `usage.events`: how many `SerializedPropertyChangeEvent`s a `PropertyField` sends, and when, for
  two writes in one frame to a usage drawer's field and to a plain field.

## Usage

```bash
python3 tests/projects/bindingcontrols/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/bindingcontrols/run.py publish run-a run-b
python3 tests/projects/bindingcontrols/run.py gate
```

The values depend on neither the OS nor the graphics device (`macos_batch`).

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical captures.
