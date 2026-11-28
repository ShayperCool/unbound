# Focused inspector opening

Records `EditorUtility.OpenPropertyEditor` in Unity 6000.0.71f1 batch Play mode.
The probe measures null and destroyed targets, preserving selection and keyboard
focus, separate windows for multiple requests, locked targets, scene and asset
labels, and attached panels. It also reads the locked target list before and after
unlocking and re-locking the tracker and checks close notifications and exceptions
from opening subscribers. The same-tick custom inspector control records no editor
creation or destruction callbacks. Visual
presentation and property editing are outside these state controls.

Two Linux Editor captures agree and record a (30, 30) position offset for the
second window and zero focus notifications on close. Two macOS Editor captures
agree and record zero offset and one notification on close. The runner selects the
recorded profile for the host platform.

[Unity 6.0 Manual on focused inspectors](https://docs.unity.com/en-us/engine/6000.0/manual/unity-editor/editor-windows-views-reference/using-the-inspector/inspector-focused),
[windowFocusChanged API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorWindow-windowFocusChanged.html),
[Unity 6000.0.71f1 PropertyEditor source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0.71f1/Editor/Mono/Inspector/Core/PropertyEditor.cs#L2666-L2694)

```sh
python3 tests/projects/propertyeditoropen/run.py record --label a
python3 tests/projects/propertyeditoropen/run.py record --label b
python3 tests/projects/propertyeditoropen/run.py publish a b
python3 tests/projects/propertyeditoropen/run.py gate
```
