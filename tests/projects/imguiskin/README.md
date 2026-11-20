# IMGUI GameSkin (Q18)

Records, in Play mode of the Unity 6000.0.71f1 Editor, the skin `GUI.skin` answers inside the first
`OnGUI` call: its name, flags, font and settings; every built-in and custom style's states (background
texture name, text colour), offsets, font, text and layout properties and line height; the styles the
skin enumerates and `FindStyle` finds; and every background texture's class, size, sampler, format and
pixels (copied through a RenderTexture, since the skin's textures are not readable). The capture is
windowed (on the private GPU display): a batch-mode Editor calls no `OnGUI`.

Contract: the Scripting API [GUISkin](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUISkin.html)
and [GUI.skin](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUI-skin.html) ("The global
skin to use"; the default when none is set) document the members; the values are this capture's.

The property hook controls run in both Layout and Repaint. The Scripting API describes
[`BeginProperty` and `EndProperty`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUI.BeginProperty.html)
as enclosing a property control and [`PropertyField`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUI.PropertyField.html)
as drawing a serialized property. The capture measures the internal subscription hook with
nested scopes and a default integer field. Each listener records the property path and rectangle.
Removing the first listener leaves the second subscribed. A null property throws before notifying either listener.

The list focus controls grab and release keyboard ownership from two lists in Layout, Repaint and
Update. The IDs stay distinct across the events, including for lists created during separate GUI
passes. Releasing from a non-owner leaves the current owner in place. In Update, `HasKeyboardControl`
throws when the list owns the ID and returns false when another control owns it.
[Unity 6000.0 `GUIUtility.keyboardControl`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIUtility-keyboardControl.html)

The property scope controls check default labels, caller text and images, tooltip fallback and
the shared temporary label returned to nested scopes. They move an array element after reading
its label to verify the new index. Two serialized targets provide equal and unequal values so the
mixed value indicator changes during nested scopes. The controls also check read-only properties,
restoration of enabled state and background color, and repeated disposal.
[Unity 6000.0 `EditorGUI.PropertyScope`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUI.PropertyScope.html)

The editor layout controls check horizontal and vertical group rectangles in Layout and Repaint,
including nested groups, width options, padding and repeated scope disposal. Group rectangles
start at zero in Layout and report the arranged bounds in Repaint. Explicit fixed style sizes
take precedence over width and height options in this capture.
[Unity 6000.0 `BeginHorizontal`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.BeginHorizontal.html)
[Unity 6000.0 `BeginVertical`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.BeginVertical.html)

The numeric `GetRect` controls record fixed sizes and size ranges with options that give different
sizes on Repaint. Layout reports the supplied maximum dimensions before options apply. A Used
event returns a 1×1 rectangle at the origin and leaves the next arranged entry available. The
content overload also returns a 1×1 rectangle at the origin during Layout.
[Unity 6000.0 `GUILayoutUtility.GetRect`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUILayoutUtility.GetRect.html)
[Unity 6000.0 `Event.Use`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Event.Use.html)

```sh
python3 tests/projects/imguiskin/run.py record --label native-a
python3 tests/projects/imguiskin/run.py record --label native-b
python3 tests/projects/imguiskin/run.py publish native-a native-b
python3 tests/projects/imguiskin/run.py gate
```

Numbers compare with an absolute tolerance of 1e-6. The platform cursor flash speed (2) is the Linux
Editor's; macOS is unmeasured.
