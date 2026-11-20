# EditorGUILayout toggle groups

Unity 6000.0 documents a vertical layout group whose header sets the enabled state of
its controls. The probe checks both constructors, paired calls and repeated disposal, and
records nested groups and their arranged child rectangles. It starts one group inside an
already disabled parent and checks restoration. The mixed group returns unchecked during
Layout and Repaint. The themed mixed checkbox pixels are checked in `tests/projects/editorstyles`.
[Unity 6000.0 `BeginToggleGroup`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.BeginToggleGroup.html)
[Unity 6000.0 `ToggleGroupScope`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.ToggleGroupScope.html)

The input sequence uses the header's indicator and label, a secondary click, Space,
Control+Space, mouse releases outside the header, and clicks on a child before and after
enabling it. Each row records the event, selected value, effective and restored GUI state,
change check, consumption, focus, mouse capture and child click count. The comparable
Game view is 320×240. Recording uses X11 and `xdotool` on a private display, and the engine
replays the same input schedule with a focused window.

Both release positions cancel the change. Releasing inside the area consumes the event
and clears capture. Outside the area, the current event type is Ignore and its raw type
is MouseUp. The capture owner can still read MouseUp through GetTypeForControl. This
also holds in a nested group, and closing the clip restores the enclosing event type.
Capture stays set on the following Repaint. The control without a group confirms
that an unused MouseUp keeps an explicitly set hot control until the caller clears it.
[Unity 6000.0 `GUI.BeginGroup`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUI.BeginGroup.html)

The `ToggleLeft` controls cover fixed and automatic layout with strings, GUIContent,
and an explicit label style. The automatic controls record their arranged rectangles
with and without width and height options. The input run checks the indicator and label
hit areas, named keyboard focus and Space, a disabled control, and the mixed value
before and after a click. A pair of image labels records the icon's horizontal extent
and the pixels changed by checking the box to its left.
[Unity 6000.0 `EditorGUI.ToggleLeft`](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/editorgui/toggleleft)
[Unity 6000.0 `EditorGUILayout.ToggleLeft`](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/editorguilayout/toggleleft)

The explicit-ID `PrefixLabel` controls compare the returned field rectangle for an
indented label, empty content, tooltip-only content, and a narrow field. The pointer
sequence starts with focus on a different control. It records whether clicking changes
focus during OnGUI for a keyboard-capable control, a passive control, a disabled control,
and a click below the label. It also records secondary clicks and text editing state.
[Unity 6000.0 `EditorGUI.PrefixLabel`](https://docs.unity.cn/6000.0/Documentation/ScriptReference/EditorGUI.PrefixLabel.html)

The overloads without an ID use the next control. The probe exercises a text field,
a passive button, an image-only prefix, and a reused GUIContent whose text changes
before the next control. It also records a pending label that reaches the next pass.
The explicit style transfers focus on the first text field. In the later field's
focus comparison, Unity retains the previous control and Unbound focuses the field.
[Unity 6000.0 `GUI.GetNameOfFocusedControl`](https://docs.unity.cn/6000.0/Documentation/ScriptReference/GUI.GetNameOfFocusedControl.html)

The `HandlePrefixLabel` controls place the label outside the total rectangle and
click each region. The probe covers explicit and following control IDs and both
style overloads. Passing an explicit null style throws on Repaint for both
`HandlePrefixLabel` and `PrefixLabel`. The overloads without a style draw using
the Inspector label style.
[Unity 6000.0 `EditorGUI.HandlePrefixLabel`](https://docs.unity.cn/6000.0/Documentation/ScriptReference/EditorGUI.HandlePrefixLabel.html)

The `EditorGUILayout.PrefixLabel` controls record all six overloads, the space left
for an empty indented label, and a click inside and outside the label. An independent
horizontal group has children of different heights and vertical margins. Its
following row confirms the group's height is based on the tallest child.
[Unity 6000.0 `EditorGUILayout.PrefixLabel`](https://docs.unity.cn/6000.0/Documentation/ScriptReference/EditorGUILayout.PrefixLabel.html)

```sh
python3 tests/projects/editortogglegroup/run.py record --label native-a
python3 tests/projects/editortogglegroup/run.py record --label native-b
python3 tests/projects/editortogglegroup/run.py publish native-a native-b
python3 tests/projects/editortogglegroup/run.py gate
```
