# Custom Editor menu preparation

Unity 6000.0.71f1 in batch Play mode. Two independent Linux Editor captures agree. The
fixture calls `EditorUtility.DisplayCustomMenu` and `DisplayCustomMenuWithSeparators`
without needing menu input. It exercises empty and populated arrays, null inputs,
a null item after a valid one, the enable predicate's order and exception propagation,
and IMGUI mouse capture and scene mouse-button state around successful and failed calls.

Unity reads all option labels before evaluating the enable predicate. A successful
request clears `GUIUtility.hotControl` and the scene mouse button. If the predicate
throws, both retain their previous values. The separators overload also clears scene
mouse state with null arrays. The batch Editor invokes no selection callback in these
controls. The engine comparison is exact. On other release families, Unbound runs
option preparation and reports that completion is unmeasured. Requests with mismatched
option masks report a diagnostic and preserve mouse state. Interactive presentation,
selection and shortcut handling remain unsupported and raise a diagnostic.
[Unity 6.0 GUIUtility API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIUtility.html),
[Unity 6000.0.71f1 EditorUtility source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0.71f1/Editor/Mono/EditorUtility.cs#L271-L302)

```sh
python3 tests/projects/displaycustommenu/run.py record --label a
python3 tests/projects/displaycustommenu/run.py record --label b
python3 tests/projects/displaycustommenu/run.py publish a b
python3 tests/projects/displaycustommenu/run.py gate
```
