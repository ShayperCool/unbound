# Screen-space overlay UI drawn by URP

With URP, the pipeline draws Screen Space - Overlay canvases itself
(`SupportedRenderingFeatures.rendersUIOverlay`): URP 17's `DrawScreenSpaceUIPass` asks the
render context for the overlay UI in two renderer lists, one per `UISubset`. The Scripting API
(https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.UISubset.html) says
`UIToolkit_UGUI` includes UI Toolkit and uGUI and `LowLevel` includes IMGUI; each part of the UI
is in one of them, so each is drawn once.

`UrpOverlayUiProbe` (Linear color space, the stpmotion probe's pipeline asset and renderer, a
320x180 game view) builds two overlay canvases over the camera's solid background: on the first
(sorting order 0) a white Image at three quarters' opacity and a half-transparent green one, on
the second (order 1) a half-transparent red Image over the green. It reads the screen at the end
of the frame:

- `overlay`: the canvases alone;
- `imgui`: the same with IMGUI drawn in `OnGUI`: a white box at half opacity (over the background
  and the red image), an opaque white box tinted half grey, and opaque textures of grey 128, one
  sRGB and one linear;
- `overlay_post`: the canvases with post-processing and FXAA on the camera (URP's final pass draws
  to the screen before the UI);
- `overlay_builtin`, `imgui_builtin`: the canvases and the IMGUI after the pipeline is set to
  none, the engine drawing the overlay UI itself (a control).

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core, NVIDIA driver; two identical
captures): the canvases blend in linear space (the white Image at 0.749 over the background
64, 76, 89 is 226, 226, 228), the same with URP and without. IMGUI blends in gamma space (the
half-opaque white box over that background is 160, 166, 172), its colors as given (a box tinted
grey 0.5 is 128) and its textures in gamma space (the sRGB grey 128 shows 128, the linear one 188),
with URP and without.

Unbound draws the canvas images as the Editor does. Its IMGUI blends in linear space and tints
in linear space, so `imgui` and `imgui_builtin` differ where IMGUI draws translucent or tinted
(`image_limits.json` says how much; docs/COMPATIBILITY_GAPS.md). Other devices, GPUs and drivers
are unmeasured.

    python3 tests/projects/urpoverlayui/run.py record --label A    # and B; then publish A B
    python3 tests/projects/urpoverlayui/run.py gate
