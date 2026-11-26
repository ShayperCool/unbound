# GUI.depth probe

`GUI.depth` inside and outside OnGUI, with two OnGUI behaviours `A` and `B` (one component class
on two objects, made after the controller by `Editor/ProbeRunner.Create`). The controller counts
frames in Update (before A and B: `DefaultExecutionOrder(-100)`) and reads `GUI.depth` there
(`f<n>.update.depth`; on frame 6 it sets 7). Each behaviour logs the depth its OnGUI reads at the
start and end of its Layout and Repaint calls (`f<n>.<A|B>.<layout|repaint>.entry/exit`). Per frame
the controller records the order of those calls apart from which of A and B the scene enables
first (Unity leaves the Awake order of scene objects unspecified): `layoutsAsUpdates`, whether the
Layouts run in the order of A's and B's Updates; `repaints`, the Repaints by name where the two
Layouts left different depths, else `repaintsReverseLayouts`. A sets -3 in every call of frames 3
to 5, B sets 5 in its Repaint of frame 7 only and -3 in every call of frames 9 to 11.

Measured (Unity 6000.0.71f1, Linux, windowed Play mode; batch mode calls no OnGUI): `GUI.depth`
reads 1 outside OnGUI and at the start of every OnGUI call, and 1 again after the calls (a value
set in Update does not reach OnGUI); the value a behaviour leaves at the end of its latest call
(its Layout of the frame) orders that frame's Repaints: they run in the reverse order of the
Layouts, the higher depth first (a lower depth draws on top), while the Layouts keep their order
whatever the depths. Not measured: behaviours without GUILayout (no Layout call), input events.

Record twice with the Unity Editor, publish when the captures agree, and compare Unbound:

    python3 tests/projects/guidepth/run.py record --label a
    python3 tests/projects/guidepth/run.py record --label b
    python3 tests/projects/guidepth/run.py publish a b
    python3 tests/projects/guidepth/run.py gate
