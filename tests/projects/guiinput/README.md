# OnGUI input order probe

The order in which two OnGUI behaviours `A` and `B` (one component class on two objects, made
after the controller by `Editor/ProbeRunner.Create`) get input events. The controller
(`DefaultExecutionOrder(-100)`) counts frames in Update; `GuiInputProbe.Steps` are played as input
at given frames: in the windowed Editor by `ProbeDriver` with xdotool (the Editor's window fills
the private display, a maximized Game view shows the 320x180 game), in Unbound as `--move` and
`--keys` options (`run.py`). Each step of a phase presses and releases the left button and the A
key within one frame. Per phase the probe records every OnGUI call of the frames' input passes
(the calls after the last Repaint, before Update), naming A and B by the order of their Updates
(`U1`, `U2`; Unity leaves the scene's enable order unspecified): `tie` (equal depths), `aLower`
and `bLower` (that behaviour sets GUI.depth -3 in every call; `<phase>.lower` says which of U1
and U2 it is), and `used` (the first behaviour to get the MouseDown uses it).

Measured (Unity 6000.0.71f1, Linux, windowed Play mode): each input event runs every behaviour's
Layout first, in their order, then reaches them from the lowest depth up (equal depths in their
order); after a behaviour uses it the others get no call for it. A key press gives a KeyDown with
the key code and one with the character. Not measured: behaviours without GUILayout, events used
in Layout, other event kinds.

Record twice with the Unity Editor, publish when the captures agree, and compare Unbound:

    python3 tests/projects/guiinput/run.py record --label a
    python3 tests/projects/guiinput/run.py record --label b
    python3 tests/projects/guiinput/run.py publish a b
    python3 tests/projects/guiinput/run.py gate
