# EditorGUI progress bar, property popups and delayed fields, drop-down buttons, foldout headers

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- outside `OnGUI` (the exceptions) and in Layout and Repaint (value, control ids, `GUI.changed`,
  the layout forms' rects): `EditorGUI.ProgressBar`, `Popup` and `IntPopup` of a property, the
  delayed int, float and text fields of a property, `DropdownButton`, `BeginFoldoutHeaderGroup` /
  `EndFoldoutHeaderGroup` (also one nested in another), `BeginDisabled` / `EndDisabled` (nested
  and one end too many), `IsEditingTextField`, `LogarithmicIntSlider`; `EditorGUILayout.IntPopup`,
  `DelayedIntField`, `DropdownButton` (also with a style missing from the game skin),
  `BeginFoldoutHeaderGroup` and `LinkButton`;
- `EditorGUIUtility.CommandEvent`, `SetWantsMouseJumping`, `GetObjectPickerObject` and
  `GetObjectPickerControlID` without an open picker;
- scripted mouse and key events, one per frame in place of the frame's Repaint: a mouse down on
  the progress bar, the drop-down button pressed, a foldout header clicked, its arrow keys and its
  menu icon, a delayed int field typed into (the value changes on Return), the logarithmic
  slider's track clicked.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorcontrolsb2/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorcontrolsb2/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorcontrolsb2/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorcontrolsb2/run.py gate
```

The probe uses the imguiskin project's scene, packages and settings. `EditorGUIUtility.IsGizmosAllowedForObject`
is not measured: the Editor crashes when it is called in a game without a Scene view.
