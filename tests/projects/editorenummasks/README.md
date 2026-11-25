# Deprecated EditorGUI enum mask fields

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`, the obsolete `EditorGUI` and `EditorGUILayout` `EnumMaskField` and
`EnumMaskPopup` (no label, string and `GUIContent` labels, explicit styles; a `[Flags]` enum with
no, one, some, every and all bits set, a plain enum, a mixed value) outside `OnGUI` (the exception
and its message) and in Layout and Repaint: the returned value and its type, the control ids
taken, `GUI.changed` and the layout forms' rects; and the log messages of the run.

No clicks are scripted: a click opens the flags' menu.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorenummasks/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorenummasks/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorenummasks/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorenummasks/run.py gate
```
