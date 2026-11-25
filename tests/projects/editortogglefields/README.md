# EditorGUI toggles, property sliders and drop-shadow labels

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- `EditorGUI` and `EditorGUILayout` `Toggle` (no label, string and `GUIContent` labels, an
  explicit style, mixed values), `Slider` and `IntSlider` of a `SerializedProperty` (with and
  without labels, limits around the value) and `DropShadowLabel` (default and explicit styles)
  outside `OnGUI` (the exception and its message) and in Layout and Repaint: the returned value,
  the control ids taken, `GUI.changed`, the properties' values and the layout forms' rects;
- a scripted sequence of mouse and keyboard events, one per frame in place of the frame's
  Repaint, given to controls at fixed rects: a toggle clicked with the left and the right
  button and toggled with Space, a labelled toggle clicked on its label, its box and beside its
  box, a styled toggle, the property sliders pressed and dragged, a property field's toggle;
- the log messages of the run (the default drop-shadow style is not in the game's skin).

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortogglefields/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortogglefields/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortogglefields/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortogglefields/run.py gate
```

The engine run keeps the window focused (`--focus 1:on`).
