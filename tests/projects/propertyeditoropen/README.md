# Focused inspector opening

Records `EditorUtility.OpenPropertyEditor` in Unity 6000.0.71f1 batch Play mode.
The probe measures null and destroyed targets, preserving selection and keyboard
focus, separate windows for multiple requests, locked targets, scene and asset
labels, and attached panels. It also reads the locked target list before and after
unlocking and re-locking the tracker and checks close notifications and exceptions
from opening subscribers. The same-tick custom inspector control records no editor
creation or destruction callbacks. Visual
presentation and property editing are outside these state controls.

Both batch hosts start at `(0, 26)`. Linux accepts a position assignment and opens
the next inspector at the previous position plus `(30, 30)`. An ordinary window
can take keyboard focus. Opening an inspector with that prior focus publishes two
notifications while retaining focus. Closing the inspector clears focus without a
notification or a same-tick `OnLostFocus` on the ordinary window, including when
focus was assigned after the inspector opened.

macOS retains the initial position when a move is requested, and closing publishes
one notification. The ordinary `Show` and `Focus` calls left keyboard focus unset
in both macOS captures. Unbound's ordinary window model grants focus, so the two
prior-focus controls retain this raw difference and the inspector reports the
unsupported macOS case. The runner selects the recorded profile for the host.
The Linux recorder gives each launch fresh Editor preferences. Two exploratory
Linux runs using saved preferences after a resize reported three notifications
and focused the newly opened inspector in the earlier no-prior-focus control.
Edit mode, interactive window layout and additional release families are unmeasured.

[Unity 6.0 Manual on focused inspectors](https://docs.unity.com/en-us/engine/6000.0/manual/unity-editor/editor-windows-views-reference/using-the-inspector/inspector-focused),
[windowFocusChanged API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorWindow-windowFocusChanged.html),
[EditorWindow.Focus API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorWindow.Focus.html),
[Unity 6000.0.71f1 PropertyEditor source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0.71f1/Editor/Mono/Inspector/Core/PropertyEditor.cs#L2666-L2694)

```sh
python3 tests/projects/propertyeditoropen/run.py record --label a
python3 tests/projects/propertyeditoropen/run.py record --label b
python3 tests/projects/propertyeditoropen/run.py publish a b
python3 tests/projects/propertyeditoropen/run.py gate
```
