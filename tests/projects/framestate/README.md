# Frame state: the active render texture and GL's matrices across frames

Unity 6000.0.71f1, built-in render pipeline, Linux Editors on the NVIDIA GPU (RTX 4060) only:
windowed and batchmode, OpenGL Core and Vulkan. `Assets/Scripts/FrameStateProbe.cs` runs in Play
mode with one screen camera (culling nothing) and records:

* **Active render texture** (`active.*`): a render texture made active in Update (also read in
  LateUpdate), in LateUpdate, in Update of a frame no camera renders, and in an end-of-frame
  coroutine, read in the next frame's EarlyUpdate (a player loop system inserted first) and Update.
* **Outside-camera command buffers** (`outside.*`, pixels of an 8x8 target): a buffer clears its
  target and draws a quad over clip space (`Hidden/Internal-Colored`, white, no culling, no depth
  test) with the identity model matrix at the start of Update (`fresh`), after a `Camera.Render`
  (`aftercamera`), after setting identity view and projection itself (`identity`); a quad over
  clip space's upper right quarter (`corner`), the same inside `GL.LoadOrtho` (`ortho`), and drawn
  with `DrawMeshNow` after a buffer that set its own matrices (`after`).
* **The matrices themselves** (`matrices.*`): the same buffers also draw
  `Assets/Resources/FrameStateMatrices.shader` into a 16x4 float target, which writes
  `UNITY_MATRIX_VP`, `UNITY_MATRIX_V`, `UNITY_MATRIX_P` and `unity_ObjectToWorld` as the draw sees
  them (`vp`, `v`, `p`, `m`, rows first); extra dumps in EarlyUpdate (`early`), LateUpdate
  (`late`) and an end-of-frame coroutine (`endofframe`). `gl.modelview.*` reads `GL.modelview`,
  `screen` the Game view's size.

```bash
python3 tests/projects/framestate/run.py editor  --release 6000.0.71f1 --context opengl-window --label L
python3 tests/projects/framestate/run.py publish --release 6000.0.71f1 --context opengl-window --label L
python3 tests/projects/framestate/run.py unbound --release 6000.0.71f1 --device opengl
python3 tests/projects/framestate/run.py unbound --release 6000.0.71f1 --device vulkan
```

Contexts `opengl-window`, `opengl-batch`, `vulkan-window`, `vulkan-batch`; each reference in
`Reference/unity-6000.0.71f1/<context>.jsonl` is one of two byte-identical captures. Unbound
behaves as the windowed Editor (`Application.isBatchMode` is false), so `unbound` compares with
the window reference of its device, at the captures' Game view size (64x32). Values compare as
`modelroot/compare.py` does (numbers within 2e-4); `residuals.json` declares the records Unbound
differs in, per device, with the reason, and every report lists them.

## What the Editors do

* `RenderTexture.active` is null again before the next frame's EarlyUpdate in all four contexts,
  wherever the script left a texture active and whether or not a camera rendered (the batchmode
  macOS Metal Editor agrees: tests/projects/nativegfx `metal.jsonl` `active.*`).
* An executed buffer draws with GL's matrices: `GL.LoadOrtho`'s inside it, identity from its own
  `SetViewProjectionMatrices`, and those do not last past it (the next buffer and `DrawMeshNow`
  take GL's again). `Camera.Render` gives the matrices before it back. In an end-of-frame
  coroutine (windowed Editors; batchmode Editors run none) GL's matrices are the frame's last
  rendered camera's view and projection.
* What GL's matrices are between frames depends on the context. Batchmode Editors start a frame
  from identity (`matrices.early`; on Vulkan with the y flip of a texture target and the
  unconverted depth row). The batchmode OpenGL Core Editor then gives back a zero projection after
  any executed buffer or `Camera.Render` (`fresh`, `aftercamera`, `late` and the pixels drawn
  with them are black); the batchmode Vulkan Editor gives back identity in the device's form.
  Windowed Editors start from the pixel matrix their own window drawing left (1280x217 in these
  captures, not the 64x32 Game view), which depends on the Editor's layout.

Unbound starts each frame from identity, leaves the frame's last camera's matrices for the
end-of-frame callbacks and gives them back after `Camera.Render`. The windowed Editors' pixel
matrix between frames is not emulated (10 declared residuals per device). A scriptable pipeline's
frame, overlay canvases and OnGUI before the end-of-frame callbacks are unmeasured. The Scripting API pages for
[Graphics.ExecuteCommandBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.ExecuteCommandBuffer.html)
and [RenderTexture.active](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-active.html)
state neither rule; both are observed in these captures.
