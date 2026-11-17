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
the second (order 1) a half-transparent red Image over the green, and between them a UI Toolkit
panel (sorting order 0.5) with a half-transparent blue square over the green image and under the
red one. It reads the screen at the end of the frame:

- `overlay`: the canvases and the panel;
- `imgui`: the same with IMGUI drawn in `OnGUI`: a white box at half opacity (over the background
  and the red image), an opaque white box tinted half grey, opaque textures of grey 128, one sRGB
  and one linear, and a half-transparent blue quad drawn with GL in the Repaint; the value
  `imgui.repaints` counts OnGUI's Repaint events in the recorded frame (one: URP's two overlay
  lists do not run OnGUI twice);
- `overlay_post`: the canvases and the panel with post-processing and FXAA on the camera (URP's
  final pass draws to the screen before the UI);
- `overlay_builtin`, `imgui_builtin`: `overlay` and `imgui` after the pipeline is set to none, the
  engine drawing the overlay UI itself (a control).

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core, NVIDIA driver; two identical
captures): the canvases blend in linear space (the white Image at 0.749 over the background
64, 76, 89 is 226, 226, 228), the same with URP and without, and the panel sorts between the
canvases. IMGUI blends in gamma space (the half-opaque white box over that background is 160,
166, 172; the GL quad too), its colors as given (a box tinted grey 0.5 is 128) and its textures in
gamma space (the sRGB grey 128 shows 128, the linear one 188), with URP and without; OnGUI gets
one Repaint a frame.

Unbound draws the canvas images as the Editor does, the panel within 2/255, and runs one Repaint a
frame. Its IMGUI (the GL quad drawn in OnGUI too) blends and tints in linear space, so `imgui` and
`imgui_builtin` differ where IMGUI draws translucent or tinted (`image_limits.json` says how much;
docs/COMPATIBILITY_GAPS.md). Other devices, GPUs and drivers are unmeasured.

    python3 tests/projects/urpoverlayui/run.py record --label A    # and B; then publish A B
    python3 tests/projects/urpoverlayui/run.py gate
