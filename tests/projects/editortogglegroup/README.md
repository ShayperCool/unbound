# EditorGUILayout toggle groups

Unity 6000.0 documents a vertical layout group whose header sets the enabled state of
its controls. The probe checks both constructors, paired calls and repeated disposal, and
records nested groups and their arranged child rectangles. It starts one group inside an
already disabled parent and checks restoration. The mixed group returns unchecked during
Layout and Repaint. The mixed checkbox artwork is open.
[Unity 6000.0 `BeginToggleGroup`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.BeginToggleGroup.html)
[Unity 6000.0 `ToggleGroupScope`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.ToggleGroupScope.html)

The input sequence uses the header's indicator and label, a secondary click, Space,
Control+Space, mouse releases outside the header, and clicks on a child before and after
enabling it. Each row records the event, selected value, effective and restored GUI state,
change check, consumption, focus, mouse capture and child click count. The comparable
Game view is 320×240. Recording uses X11 and `xdotool` on a private display, and the engine
replays the same input schedule with a focused window.

The two release positions separate cancellation outside the header from input outside
the entire area. Both cancel the change. Releasing inside the area consumes the event
and clears capture. On a release outside the area, the Editor leaves the event unused
and reports capture held. Unbound consumes the event and releases capture. The raw
difference remains in the comparator output.

```sh
python3 tests/projects/editortogglegroup/run.py record --label native-a
python3 tests/projects/editortogglegroup/run.py record --label native-b
python3 tests/projects/editortogglegroup/run.py publish native-a native-b
python3 tests/projects/editortogglegroup/run.py gate
```
