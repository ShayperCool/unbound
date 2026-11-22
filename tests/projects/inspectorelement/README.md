# InspectorElement probe

Records a Unity 6000.0.71f1 `InspectorElement` and the panel's visual tree updater:

- `uss`: the element's USS class names.
- `editor.*`: a generic inspector's `CreateInspectorGUI` and `UseDefaultMargins`, and the base
  `Editor`'s for a custom editor with only `OnInspectorGUI`.
- `default.*`: the default inspector of an object, a `SerializedObject`, a component and several
  objects: its containers, classes, the `PropertyField` of each visible property (the script field
  disabled) and the editor it makes.
- `custom.*`: custom editors with `CreateInspectorGUI`, with only `OnInspectorGUI`, with a null UI
  Toolkit GUI and without default margins; the editor's `OnEnable`/`OnDisable` and
  `CreateInspectorGUI` counts, the editor destroyed when the element leaves its panel and the
  inspector built again when it comes back.
- `given.*`: an element made for a given editor, which it does not destroy.
- `empty*`: an element without an object, then bound, bound to another object of the same type
  (the GUI is kept, the editor is new), then to an object of another type.
- `nested.own`: an element bound to its own object inside a container bound to another one.
- `fill.*`: `InspectorElement.FillDefaultInspector` without an object, without and with an editor.
- `vtu.*`: the panel's `m_VisualTreeUpdater` field (read by reflection, as editor extensions do)
  and the element's layout after each phase run alone, then after the whole update; a field bound
  off its panel, after the bindings and the data bindings phases.

## Usage

```bash
python3 tests/projects/inspectorelement/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/inspectorelement/run.py publish run-a run-b
python3 tests/projects/inspectorelement/run.py gate
```

The values depend on neither the OS nor the graphics device (`macos_batch`); the panel has a
constant pixel size.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical captures.
