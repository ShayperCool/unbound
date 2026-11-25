# EditorGUI value fields: number, text, password and delayed fields, sliders

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- every `EditorGUI` and `EditorGUILayout` form of `FloatField`, `IntField`, `LongField`,
  `DoubleField`, `TextField`, `PasswordField`, `TextArea`, `DelayedTextField`,
  `DelayedFloatField`, `DelayedIntField`, `DelayedDoubleField`, `Slider` and `IntSlider` outside
  `OnGUI` (the exception) and in Layout and Repaint: the returned value, the control ids taken,
  `GUI.changed`, and the layout forms' rects;
- a scripted sequence of mouse and keyboard events, one per frame in place of the frame's
  Repaint, given to fields at fixed rects: clicking into a field, typing, an expression (`2*3`,
  `1/8`), a character a number field refuses, Escape, Return and Tab, a delayed field that commits
  only on Return, clicking away, clicking and dragging a slider, and dragging a labelled number
  field's label. Each step records every field's value, which field set `GUI.changed`, keyboard and
  hot control, `EditorGUIUtility.editingTextField` and the event's type afterwards.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorvaluefields/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorvaluefields/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorvaluefields/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorvaluefields/run.py gate
```

The event type is recorded as its number (`EventType`'s obsolete lower-case names share values).
Residual: outside `OnGUI` the sliders and the layout `TextArea` (see `residuals.json`).
