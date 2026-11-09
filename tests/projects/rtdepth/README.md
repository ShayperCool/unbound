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

**Controls** (`run.py ... --mode controls`, `Reference/unity-6000.0.71f1/controls/`): the screen
camera without and with an empty command buffer reads the screen in `OnPostRender` (`ctl.postRead.*`,
an 8-bit sRGB screenshot), blits a texture to the screen in `OnPostRender` (`ctl.postBlit.*.final`)
and draws with `GL.wireframe` on until `OnPostRender` (`ctl.wireframe.*.final`) or until the end of the
frame (`ctl.wireframeKept.*.final`); a camera draws a material whose
`_MainTex` is a target's Depth element (`ctl.mainDepth`); `_Src_TexelSize` of a Depth element
(`ctl.texelSize`); a target's depth cleared through `SetRenderTarget(A, B)` after both targets'
Depth elements were sampled (`ctl.attachedDepth.*`); a `GrabPass` texture loaded as unsigned
integers, twice with different colors (`ctl.grabUint.*`); an unbound `Texture2DArray<uint4>`
(`ctl.uintArray`).

Each Editor runs windowed on a private Xvfb with the GPU through VirtualGL (`ProbeRunner` opens
the scene, sets the game view to 64x32, enters Play mode and quits with the probe's status; a
watchdog quits after 600 s). Runs use fresh project copies under
`$XDG_CACHE_HOME/unbound/validation/runs/rtdepth/<label>`; `publish` requires two byte-identical
complete captures (verified NVIDIA renderer, Editor exit status 0, the probe's last record
`complete: true` without `fatal`, exactly the recorded images at their sizes; `check` reports
this without publishing) and refuses to overwrite a reference. The comparator
keeps the whole key union and image manifest; numbers and channels use the camera MSAA probe's
float tolerance (absolute and relative 2e-4).

## Combined integration, 2026-11-01

The combined NativeApi 13 / render protocol 6 / shader cache 16 runtime passes
all 1,071 main values and 316/368 images. Controls pass all 46 values and 13/17
images. Both complete comparisons preserve the revised delivery's failures,
with no introduced or newly missing observations. The 52 main images retain
the screen-encoding gap. The four control images retain three screen-encoding
differences and the unsupported integer-array default binding.

Integer and depth-comparison `_MainTex` declarations use their reflected typed
binding layouts. Shader cache 16 invalidates the previous layout classification.
A private reproduction that previously failed GPU validation completes with the
matching combined runtime. It checks binding validity, without claiming new
Editor image parity. Sampler metadata requests reject element and unknown handle
bits before modifying sampler state.

The combined build passes 1,273/1,284 managed tests, retaining the same eleven
failures with none introduced. API checking reports zero errors in 2,290 types;
the affected generated modules match regeneration. Rust runtime 50, app 19 and
35 portable shader tests pass. The shader test requiring unavailable private
fixtures remains explicitly excluded. Graphics, resources, built-in effects,
UI Toolkit, focused MSAA and clipping controls preserve their prior complete
comparator results. Representative main, bilinear, live and cache sampler
controls pass 150,053 observations without numerically worsening accepted values.

These results certify the measured RT scope. Full integration acceptance still
has the separate humanoid IK blocker recorded in `docs/PARITY_PROGRESS.md`.

`Reference/unity-6000.0.71f1/` holds 1,071 values and 368 images from two byte-identical captures
(OpenGLCore 4.5, RTX 4060).
`Reference/unity-6000.0.71f1/controls/` holds the controls' values and images, from two
byte-identical complete captures (same device).

## Screen encoding, 2026-11-01

The screen is now stored as the game view stores it: 8-bit RGBA, drawn through an sRGB view in
Linear color space, read back and copied as its bytes (`crates/unbound-app/src/render/executor.rs`;
it was a half-float target whose float reads returned linear values and whose byte reads were
re-encoded on the CPU). All 1,071 main values and 368/368 images pass (316 before, the 52
`target.screen.*.final` images resolved); controls pass 46/46 values and 16/17 images (13 before:
`ctl.postRead.none`, `ctl.wireframe.none.final` and `ctl.wireframeKept.none.final`, one 8-bit step
apart at exact halves, resolved); `ctl.uintArray` passes since integer array slots with nothing
bound read the default array texture's bits (17/17 images). No
value or image is introduced or newly missing. The camera MSAA probe's Linear screen reads now
match as its Gamma ones do (24 images and 18 records resolved, none introduced); its reads of the
Editor's system framebuffer remain different.

On Vulkan (`--device vulkan`) the main set passes 1,071 values and 368/368 images, and the controls
17/17 images: a screen camera's stand-in is stored as the screen is (top-down), so its wireframe
edges on pixel boundaries rasterize as on the Editor's screen.

## What the Editor does on Vulkan (6000.0.71f1, Linux, `-force-vulkan`)

- The default copy of a screen camera's target (`Blit` from `CameraTarget`/`CurrentActive`
  without a material) holds the screen as stored: upside down in the texture
  (`target.screen.*.blit{Cam,Active}.out` are the OpenGL Core images flipped).
- An integer `Texture2DArray` with nothing bound reads the same bits as on OpenGL Core
  (`ctl.uintArray`: one gray texel, zeros outside it).

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
* The screen (the game view) is 8-bit RGBA, sRGB-encoded in Linear color space: `ReadPixels`
  from it returns those stored bytes into a float texture too, and draws onto it are quantized
  by the GPU's encoding, a value at half a step included (the `ctl.*.none` controls draw straight
  onto it, the `.buffer` ones through a camera stand-in texture of the same format).
