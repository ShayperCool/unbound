# EditorGUI rendering layer mask fields

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`, with three named rendering layers (`Default`, `Lights`,
`Shadows`, this probe's `TagManager.asset`):

- the defined rendering layers' names, values and combined mask;
- `EditorGUI` and `EditorGUILayout` `RenderingLayerMaskField` (string and `GUIContent` labels,
  no label, explicit styles, `uint` and `RenderingLayerMask` values, a `SerializedProperty`,
  masks of no, some, all and undefined layers, a mixed value) outside `OnGUI` (the exception and
  its message) and in Layout and Repaint: the returned mask, the control ids taken,
  `GUI.changed`, the property's value and the layout forms' rects (the forms that forward drop
  their layout options, as Unity's C# does);
- the log messages of the run.

No clicks are scripted: a click opens the mask's menu.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorrenderinglayers/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorrenderinglayers/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorrenderinglayers/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorrenderinglayers/run.py gate
```
