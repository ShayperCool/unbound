# Fixed rendering resolution and host presentation

Unity 6000.0.71f1, Linux, NVIDIA OpenGL Core, one display pixel per Editor point.
Two independent windowed Editor captures agree on all 50 records. The Game view is a
floating window with its toolbar hidden and its default zoom, with real X pointer movement.
Xvfb atomically assigns each private display before a capture starts.

Six window/target pairs cover square, wide, tall, integer enlargement, fractional reduction
and odd window dimensions. Records include the available view rectangle, the image rectangle,
Screen dimensions, Camera.pixelRect and three legacy mouse positions, including bars outside
the image. Fixed targets keep their aspect ratio and are centered. When the target fits more
than once, default zoom is the whole number of times it fits. Otherwise it shrinks uniformly.
An odd margin can leave a half-pixel input offset. Screen and Camera.pixelRect describe the
rendering target, independently of the window and its bars.

A retained-frame resize control adds seven records. After an initial image, the camera is
disabled and OnDemandRendering skips screen frames while the window changes from 640x360
to 800x600. The fixed 320x320 target moves from (160,20) to (240,140); the render callback
counter stays unchanged. Both Editor repeats preserve every one of the prior 43 rows byte
for byte. The reference records centering and dimensions, not the Editor's surrounding skin.

The engine replay uses NVIDIA Vulkan, eight private project copies and actual host pointer movement. Each
host screenshot must have exactly the measured solid-red image bounds with black borders.
Two engine resize controls retain the previous image without a camera render: a fixed target
stays centered; an ordinary view fills the resized window. The latter preserves Unbound's
previous behavior and is not an additional Editor capture claim.
Every reference row is compared exactly, including missing/extra keys. One raw pointer row
remains two float32 steps apart: `1.pointer2` is `(480, -124.444443)` in the Editor and
`(480, -124.444458)` in the engine. The vertical conversion's subtraction and scale round
differently. It is recorded without changing the comparator or reference. `unbound` returns
failure for that row; `gate` accepts only that exact expected/actual pair and prints the raw
49/50 count. Any other difference, missing key or extra key fails the gate. Screenshots and logs
stay in the run directory, outside Git. Publication requires complete, identical GPU repeats
and checks every authored project file against both capture copies.

```sh
python3 tests/projects/gameview/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/gameview/run.py publish --release 6000.0.71f1 --label <same-label>
# When adding controls, --extend preserves every prior reference row byte and relative order.
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/gameview/run.py gate --release 6000.0.71f1 --label <fresh-label>
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
