# Inspector IMGUI styles

Unity 6000.0 describes `EditorStyles.boldLabel` as the bold label style. The Game view
controls record the style in Layout and Repaint, its font and layout fields, and the foreground
color and background presence for all eight states. The same instance is returned in both
passes and inside a disabled group. Editing its normal state is visible through the property,
and a copy retains its original state. The implicit font line height matches a control with
the current game skin font assigned.
[Unity 6000.0 `EditorStyles.boldLabel`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorStyles-boldLabel.html)
[Unity 6000.0 GUI Style manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-GUIStyle.html)

The `EditorStyles.label` control records the regular Inspector label's layout, font,
and eight visual states, including the active and focused colors. Layout and Repaint
share the same style instance, and mutations are visible through the property.
[Unity 6000.0 `EditorStyles.label`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorStyles-label.html)

The toggle controls check the same layout and state fields for `EditorStyles.toggle`,
including the theme-specific border and overflow, the background property in all eight
states, and the names and sizes of the six scaled background textures. Explicit checked
and unchecked controls retain their values during Repaint.
[Unity 6000.0 `EditorStyles.toggle`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorStyles-toggle.html)

The 320×180 Gamma Game view capture compares the checkbox pixels under normal, hovered,
pressed, focused and disabled combinations. It checks hot-control hover suppression, a
named style and renamed copy, an explicit background override, a short control, clipping,
tint, and fractional coordinates. The comparison is exact. Recording uses `xdotool` on
the private display to move the pointer inside the focused Game view. The gate scripts
both the focus and pointer position so simultaneous probes can use other windows.
[Unity 6000.0 `GUIStyle.Draw`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIStyle.Draw.html)

The mixed checkbox capture also requires exact pixels for the dash, hover and focus.
It includes the enabled and disabled parent paths through `EditorGUILayout.ToggleGroupScope`
with `EditorGUI.showMixedValue` set, and controls for pressed and checked images.
[Unity 6000.0 `EditorGUI.showMixedValue`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUI-showMixedValue.html)

Two independent windowed GPU Editor runs per theme are compared byte for byte before
publication. Recording uses Bubblewrap to isolate the Editor preferences while selecting
the theme. Each gate sets the preference in its own run directory. The comparator checks
numbers within 1e-6 because the capture writes color components to six decimal places.

The scope is Game view OnGUI in Unity 6000.0, with checkbox pixels measured at 1×.
Other release families, access between Game view events, High DPI and Linear checkbox
pixels, pointer leave, and label pixel and text extent parity are open. The batch Editor
control is in `tests/projects/editorui`.

```sh
python3 tests/projects/editorstyles/run.py record --label native-a
python3 tests/projects/editorstyles/run.py record --label native-b
python3 tests/projects/editorstyles/run.py publish native-a native-b
python3 tests/projects/editorstyles/run.py gate
```
