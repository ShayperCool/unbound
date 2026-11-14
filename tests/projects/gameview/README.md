# Fixed rendering resolution and host presentation

Unity 6000.0.71f1, Linux, NVIDIA OpenGL Core, one display pixel per Editor point.
Two independent windowed Editor captures agree on all 43 records. The Game view is a
floating window with its toolbar hidden and its default zoom, with real X pointer movement.
Every capture waits for its private X display to become ready before starting another.

Six window/target pairs cover square, wide, tall, integer enlargement, fractional reduction
and odd window dimensions. Records include the available view rectangle, the image rectangle,
Screen dimensions, Camera.pixelRect and three legacy mouse positions, including bars outside
the image. Fixed targets keep their aspect ratio and are centered. When the target fits more
than once, default zoom is the whole number of times it fits. Otherwise it shrinks uniformly.
An odd margin can leave a half-pixel input offset. Screen and Camera.pixelRect describe the
rendering target, independently of the window and its bars.

The engine replay uses six private project copies and actual host pointer movement. Each
host screenshot must have exactly the measured solid-red image bounds with black borders.
Every reference row is compared exactly, including missing/extra keys. Screenshots and logs
stay in the run directory, outside Git. Publication requires complete, identical GPU repeats
and checks every authored project file against both capture copies.

```sh
python3 tests/projects/gameview/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/gameview/run.py publish --release 6000.0.71f1 --label <same-label>
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/gameview/run.py unbound --release 6000.0.71f1 --label <fresh-label>
```

Documented in the [6000.0 Game view Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/GameView.html):
zoom changes how the game screen is displayed within the view.
[Input.mousePosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Input-mousePosition.html)
uses rendering pixels with a bottom-left origin and can report coordinates outside the Game view.
[Camera.pixelRect](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-pixelRect.html)
is the camera's screen rectangle in pixels. The exact default zoom and centering are measured
observations of this release. Managed controls also check the inverse conversion used by cursor
warps and an integer zoom of six outside the capture's sizes.

Unmeasured: Editor zoom/panning selected by the user, high-DPI backing scales, Input System
warp delivery, multiple Game views, other releases and other GPU drivers. Unbound has no Editor
zoom/pan control and uses the measured default at physical window pixel dimensions. The host
blit's texture filtering is unchanged and is not measured by the solid-red screenshots.
