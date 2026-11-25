# EditorGUI vector, rect and bounds fields; MultiFloatField and MultiIntField

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- `EditorGUI` and `EditorGUILayout` `Vector2Field`, `Vector3Field`, `Vector4Field`,
  `Vector2IntField`, `Vector3IntField`, `RectField`, `RectIntField`, `BoundsField`,
  `BoundsIntField`, and `EditorGUI.MultiFloatField`/`MultiIntField` outside `OnGUI` (the
  exception) and in Layout and Repaint: the returned value, the control ids taken, `GUI.changed`,
  the layout forms' rects, labelled forms in narrow and wide mode;
- scripted clicks, typing and label drags, one event per frame in place of the frame's Repaint, at
  positions inside the sub-fields of fields at fixed rects (column middles and edges, a narrow-mode
  label's line, a rect's and a bound's second row, the Center/Extent label column, sub-labels of
  different widths): which sub-field takes the edit shows where each lies.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editormultifields/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editormultifields/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editormultifields/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editormultifields/run.py gate
```

The engine run keeps the window focused (`--focus 1:on`): an editor field edits only in a focused view.
