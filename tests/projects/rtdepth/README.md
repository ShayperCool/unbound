# Render texture sub-elements and built-in targets

Unity 6000.0.71f1, built-in pipeline, Linear color space (`Assets/Scripts/RtDepthProbe.cs`).

**Formats** (`fmt.<config>.*`): an orthographic camera (near 1, far 11) draws four flat quads at
view depths 3, 5, 7 and 9 with stencil references 1, 7, 128 and 255, and a ramp from view depth
1.5 to 10.5 with reference 42, into 64x32 render textures: 16/24/32 depth bits, D16, D24S8, D32,
D32S8, depth-only 24 and D32S8, no depth, MSAA 2, and a perspective D24S8 camera. Each target is
bound with `Material.SetTexture` (no element, Default, Color, Depth, Stencil) and
`CommandBuffer.SetGlobalTexture` (no element, Color, Depth, Stencil), then sampled by a filtered
`tex2D` (`SampleTex`), a float `Load` (`LoadFloat`) or a `Texture2D<uint2>` `Load` (`LoadUint`)
into an ARGBFloat target cleared to `(0.125, -1, -2, 0.5)`. The unsigned loads run last.
A 1x1 magenta texture is the global `_Src` between cases, so a binding of nothing is visible.

**Targets** (`target.<screen|texture>.<ldr|hdr>.<event>.<case>.*`): a camera drawing to the
screen (a 64x32 game view) and to a render texture runs one command buffer per frame at
AfterForwardOpaque or AfterEverything: Blit from CameraTarget and CurrentActive; SetGlobalTexture
of CameraTarget (plain, Color, Depth), Depth, CurrentActive, ResolvedDepth; SetRenderTarget back
to CameraTarget or CurrentActive after another target and a draw; Blit into CameraTarget and
CurrentActive. `.out` is the command's output, `.final` the camera's image (the screen through
`ReadPixels` at the end of the frame), `.scratch` the other target.

**API values** (`api.*`): format support per usage (StencilSampling too), depth formats for bit
counts, `RenderTargetIdentifier` strings and equality, Material getters after element bindings.
Every case records its log messages.

## Recording

```
python3 tests/projects/rtdepth/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/rtdepth/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/rtdepth/run.py unbound --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/rtdepth/compare.py tests/projects/rtdepth/Reference/unity-6000.0.71f1 \
    ~/.cache/unbound/validation/runs/rtdepth/<label>/unbound --report <cache>/report.json
```

Each Editor runs windowed on a private Xvfb with the GPU through VirtualGL (`ProbeRunner` opens
the scene, sets the game view to 64x32, enters Play mode and quits with the probe's status; a
watchdog quits after 600 s). Runs use fresh project copies under
`$XDG_CACHE_HOME/unbound/validation/runs/rtdepth/<label>`; `publish` requires two byte-identical
captures with a verified NVIDIA renderer and refuses to overwrite a reference. The comparator
keeps the whole key union and image manifest; numbers and channels use the camera MSAA probe's
float tolerance (absolute and relative 2e-4).

`Reference/unity-6000.0.71f1/` holds 1,071 values and 368 images from two byte-identical captures
(OpenGLCore 4.5, RTX 4060).

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* The Depth element samples as `(d, 0, 0, 1)`, the window depth in 0..1 with the near plane at 0
  (no reversed Z), for every depth format and both bit-count and descriptor constructors; the
  same for float loads. An MSAA 2 target's Depth element reads one sample's depth (flat areas
  equal; the ramp is a quarter pixel's slope, 0.00703, nearer than the pixel center).
* Default and no element sample the color; a depth-only target's Default samples its depth, its
  Color element binds nothing. The Depth element of a target without depth binds nothing.
* Stencil sampling is unsupported (`IsFormatSupported(..., StencilSampling)` is false for every
  format): the Stencil element binds nothing. A material binding of nothing leaves the global
  value to the shader (the magenta texture; an unsigned load reads 0). A global binding of
  nothing is Unity's gray default texture (`(0.2158, 0.2158, 0.2158, 0.502)` linear); an unsigned
  load reads the bits of its 0.2158 as the integer 1046282240 (over a 16x16 area, 0 elsewhere). No message is logged.
* `Material.GetTexture` of a name the shader does not declare logs an error and returns null even
  after `SetTexture`; `HasTexture` is true.
* Blit from CameraTarget or CurrentActive copies the camera's image, for the screen too. As a
  global texture, CameraTarget and CurrentActive bind the camera's target when it is a render
  texture, the gray default texture when the camera draws to the screen; CameraTarget's Depth
  element binds the target's depth for a render texture, gray for the screen.
  `BuiltinRenderTextureType.Depth` binds gray. ResolvedDepth warns "CommandBuffer: built-in render
  texture type 5 not found while executing <buffer> (SetGlobalTexture)" and leaves the global.
* Blit into CameraTarget or CurrentActive draws into the camera's target (the image the camera
  shows). SetRenderTarget(CameraTarget) after another target draws into the camera's target;
  SetRenderTarget(CurrentActive) after another target stays on that target. During an HDR camera's rendering `Camera.activeTexture`
  is its half-float intermediate.
* `ReadPixels` from the screen returns the game view's sRGB-encoded values (the inherited screen
  readback gap in docs/COMPATIBILITY_GAPS.md).
