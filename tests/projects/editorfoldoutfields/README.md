# EditorGUI foldouts, int popups, min-max sliders and selectable labels

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- `EditorGUI` and `EditorGUILayout` `Foldout` (string and `GUIContent` labels, with and without
  `toggleOnLabelClick`, a style), `IntPopup` (values found and not found, no option values, labels,
  `GUIContent` options, a style), `MinMaxSlider` (inside, crossed and outside the limits, labels) and
  `SelectableLabel` outside `OnGUI` (the exception) and in Layout and Repaint: the returned value,
  the control ids taken, `GUI.changed`, and the layout forms' rects;
- a scripted sequence of mouse and keyboard events, one per frame in place of the frame's
  Repaint, given to controls at fixed rects: a foldout's arrow and label clicked (the label toggles
  only with `toggleOnLabelClick`), a click that ends off the arrow, left and right arrows on the
  focused foldout, and a min-max slider's thumb, its end and its track pressed and dragged.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorfoldoutfields/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorfoldoutfields/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorfoldoutfields/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorfoldoutfields/run.py gate
```

The engine run keeps the window focused (`--focus 1:on`). Residual (`residuals.json`): the
min-max slider's dragged end and track click differ in the last float bits.
