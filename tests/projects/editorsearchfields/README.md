# Search fields and the Editor skin styles EditorStyles names

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- the 63 `EditorStyles` members that are the Editor skin's style of that name (name, fixed size,
  offsets, font size and style, alignment, wrapping, clipping, stretching, content offset, image
  position, rich text, normal text colour and backgrounds), read before any GUI has run;
- `IMGUI.Controls.SearchField` (`OnGUI` and `OnToolbarGUI`, rect and layout forms, explicit
  styles) and the internal `EditorGUI.ToolbarSearchField` (with and without search modes),
  `EditorGUI.SearchField` and `EditorGUILayout.ToolbarSearchField` (through reflection) outside
  `OnGUI` (the exception and its message) and in Layout and Repaint: the returned text, the
  control ids taken, `GUI.changed`, and the layout forms' rects;
- a scripted sequence of mouse, key and command events, one per frame in place of the frame's
  Repaint, given to two search fields at fixed rects: a click into the first and typing, the
  up and down arrows' callback, its cancel button (clears it and drops the focus), the toolbar
  field's cancel button without focus, and the Find command validated and executed (the focus
  comes with the next Repaint), then typing and another command.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorsearchfields/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorsearchfields/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorsearchfields/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorsearchfields/run.py gate
```

The engine run keeps the window focused (`--focus 1:on`). The `ids/*` keys record the absolute control id a passive
control gets at points of each pass (kept apart from the controls' answers); residual
(`residuals.json`): where the counter stands (the Editor's other views take ids first; not
modelled, Q770); the steps between the ids match.
