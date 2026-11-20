# Inspector IMGUI styles

Unity 6000.0 describes `EditorStyles.boldLabel` as the bold label style. The Game view
controls record the style in Layout and Repaint, its font and layout fields, and the foreground
color and background presence for all eight states. The same instance is returned in both
passes and inside a disabled group. Editing its normal state is visible through the property,
and a copy retains its original state. The implicit font line height matches a control with
the current game skin font assigned.
[Unity 6000.0 `EditorStyles.boldLabel`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorStyles-boldLabel.html)
[Unity 6000.0 GUI Style manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-GUIStyle.html)

Two independent windowed GPU Editor runs per theme are compared byte for byte before
publication. Recording uses Bubblewrap to isolate the Editor preferences while selecting
the theme. Each gate sets the preference in its own run directory. The comparator checks
numbers within 1e-6 because the capture writes color components to six decimal places.

The scope is the style returned during Game view OnGUI in Unity 6000.0. Other release
families, access between Game view events, and pixel and text extent parity are open.
The batch Editor control is in `tests/projects/editorui`.

```sh
python3 tests/projects/editorstyles/run.py record --label native-a
python3 tests/projects/editorstyles/run.py record --label native-b
python3 tests/projects/editorstyles/run.py publish native-a native-b
python3 tests/projects/editorstyles/run.py gate
```
