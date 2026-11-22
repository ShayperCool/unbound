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

The Used-event group controls open nested `EditorGUILayout` and `GUILayout` groups after
consuming a mouse event. The new groups have empty rectangles. Returning to Repaint lets the
same layout pass read its arranged group, both children and the following entry.
[Unity 6000.0 `Event.Use`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Event.Use.html)

The `GetControlRect` controls check all four overloads with two label and field width settings.
Layout reports the maximum width and requested height before options apply. The unlabeled
control can shrink to the field width in a constrained row. Repaint checks default margins,
caller-supplied margins and fixed size options. The geometry agrees in both Editor themes.
A null style supplied during Layout raises `NullReferenceException`.
[Unity 6000.0 `EditorGUILayout.GetControlRect`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.GetControlRect.html)

The group margin controls use ordinary fixed-size layout entries with different margins.
In the capture an explicit group width includes the vertical child margins in its height.
An explicit group height includes the horizontal child margins in its width. The controls
record the group bounds, the children and the next entry.

The null-text and null-content label controls run in Layout and Repaint for `EditorGUI.PrefixLabel`,
`EditorGUI.HandlePrefixLabel`, `EditorGUI.LabelField`, `EditorGUILayout.PrefixLabel` and
`EditorGUILayout.LabelField`. A `GUIContent` whose text is `null` reserves the label column without
logging an error, contrasting with `GUIContent.none` and tooltip-only content. Passing a `null`
`GUIContent` to `PrefixLabel` or `HandlePrefixLabel` reserves the column and logs
`"Style.DrawPrefixLabel may not be called with GUIContent that is null."` on Repaint only.

The Used-event outside-group controls run directly inside `GUILayout.BeginArea` (with no nested
horizontal or vertical group), around a `GUILayout.BeginArea`/`EndArea` called while the event is
already `Used`, and at the root `OnGUI` layout level, checking `GetRect` (numeric and content
overloads), `GetAspectRect`, `GetLastRect`, `EditorGUILayout.LabelField` and
`EditorGUILayout.PrefixLabel` after `Event.Use()`, then restoring Repaint to confirm the arranged
cursor did not advance and that the root top-level group caps its width to `Mathf.Min(Screen.width /
pixelsPerPoint, topLevel.maxWidth)`.

The narrow-parent layout controls check stretchable, fixed, `ExpandWidth(false)`/`ExpandHeight(false)`,
`MinWidth` and `MaxWidth`/`MaxHeight` entries and nested horizontal/vertical groups under narrow
(`100`, `30`) and wide (`200`, `80`) parents, including a fixed-height (`30` and `80`) horizontal
group so `SetVertical` runs with `available < minHeight`. A stretchable child takes the parent's
available width or height without clamping up to its minimum size; a non-stretchable child clamps to
`[min, max]`; a group with `maxWidth == 0` (or `maxHeight == 0`) propagates its children's and
style's stretchability, while a non-zero `maxWidth`/`maxHeight` (including `MinWidth` raising an
unset `maxWidth` to `minWidth`) or fixed size clears stretchability.

The `GUIStyle.CalcHeight` and Linux OS fallback controls record `Font.GetOSFallbacks()` on Linux, the
`LegacyRuntime` TextCore `FontAsset` face metrics (`FreeSans`, honoring `OS/2` `USE_TYPO_METRICS`),
wrapped and unwrapped `CalcHeight` across positive, zero and negative widths, empty/null/whitespace/
multiline strings, `fixedHeight`, and 5×5 and 20×20 images under every `ImagePosition` and
`EditorGUIUtility.SetIconSize`, plus four stacked fractional-height wrapping rows verifying per-rect
rounding with unrounded cursor accumulation.
[Unity 6000.0 `GUIStyle.CalcHeight`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIStyle.CalcHeight.html)

The control ID focus-eligibility controls span two `OnGUI` passes (`Layout` + `Repaint` each):
reusing a hint across passes preserves the control ID and its initial `CanHaveKeyboardFocus` state
even when the `FocusType` changes, `PrefixLabel` clicks transfer focus only when the ID is focusable,
a new hint allocates a new ID, `EventType.Used` returns `-1` without advancing the search index, and
named `TextField`, `LabelField` and `ToggleLeft` controls keep stable IDs and focusability.
[Unity 6000.0 `GUIUtility.GetControlID`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIUtility.GetControlID.html)

```sh
python3 tests/projects/imguiskin/run.py record --label native-a
python3 tests/projects/imguiskin/run.py record --label native-b
python3 tests/projects/imguiskin/run.py publish native-a native-b
python3 tests/projects/imguiskin/run.py gate
```

Numbers compare with an absolute tolerance of 1e-6. The platform cursor flash speed (2) is the Linux
Editor's; macOS is unmeasured.
