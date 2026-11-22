# The game driver on a small UI

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two byte-identical captures;
nothing here depends on the OS or the graphics device). The probe embeds the debug driver
`tools/gamedrive` (see its README) as a game's copy does, builds a UI in Play mode
(`DriveProbe.Build`: a Button, areas that record their pointer events, a swipe area, an on-screen
stick, a Slider, a popup that appears a second after the start on the wall clock, and an overlay
drawn over everything) and runs two scripts on it:

* `step.NN`, on the wall clock: a tap on the overlay, a `click` that reaches the button below it,
  the overlay hidden, taps on the button, on an area and on nothing, a swipe that leaves an area
  without a drag handler (no click), swipes right and down, the stick pushed right, to the upper
  left and dragged down, the slider swiped to three quarters, the popup awaited and clicked, a
  press held, a target named by its path, a swipe from one half of a group to the other (the
  halves' exit and enter, the group entered once), a click on a name two buttons share (the first
  in hierarchy order takes it), the dump, and a wait for an object that never comes (the run ends
  on its timeout, code 3). A button's select and deselect are recorded too.
* `frames.NN`, on the frame clock at 50: a wait, a click, a stick and a swipe; each log line with
  the frames since the one before and the frame's `Time.deltaTime`.
* `hover.00` feeds touch samples to the Editor's uGUI 2.0 Standalone Input Module. `hover.01`
  sends the same gestures through the driver. The samples cross from parent to child and back,
  then leave the group. A second gesture releases on the child. The exit handlers record the
  reentry flag and whether the pointer moved in that sample. Unbound runs the driver for both
  rows and compares it with the two Editor sequences.
* `controls.00` checks a wait longer than the target timeout, target replacement during settling,
  deselection after an empty tap, immediate overlay hiding, a rotated stick and a world-space
  event camera. It also checks consecutive runs writing to the same log, a rejected nonfinite
  timeout and duration, and a tap with no EventSystem.
* `controls.01` checks that a warning issued by a `Finished` subscriber stays out of the completed
  run's log.

A row is what the driver logged for the step (`> ...`) and the events the UI recorded between its
lines, a run of one event (a drag's moves) once. `final` is the UI's state at the end.

The rows record the driver's own event order as much as the engine's UI: the Editor's capture is
the reference for how uGUI, its raycasts and `ExecuteEvents` answer the driver, so that the same
script drives a game the same way in both.

```
python3 tests/projects/gamedrive/run.py record --label <label>     # macOS: batch Editor
python3 tests/projects/gamedrive/run.py publish <label-a> <label-b>
UNBOUND=<unbound> python3 tests/projects/gamedrive/run.py gate
```

Values compare exactly. Nothing recorded depends on how long a frame or the run took: the
wall-clock run records no times and no frame counts, and the frame-clock run counts frames only.

The separate `check_shots.py` check runs the package's normal entry point with a rendering
window. It verifies two consecutive PNG captures, replacement of a previous file and an
immediate `quit:7`. The check reads the PNG header and end marker without comparing pixels.
Use `check_shots.py editor` for Unity and `check_shots.py gate` for Unbound.

The Unity 6000.0 scripting API specifies the camera used to convert screen positions into a
rectangle's coordinates. During a pointer callback, `pressEventCamera` supplies it for a click.
The separate screenshot API writes a PNG and replaces a file of the same name.
[RectTransformUtility](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RectTransformUtility.ScreenPointToLocalPointInRectangle.html)
[ScreenCapture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScreenCapture.CaptureScreenshot.html)
