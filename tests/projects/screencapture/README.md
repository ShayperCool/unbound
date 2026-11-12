# ScreenCapture and screen readback

Unity 6000.0.71f1, built-in pipeline, a 64x32 game view in Play mode
(`Assets/Scripts/ScreenCaptureProbe.cs`), in Linear (the authored settings) and Gamma color space.

A camera clears to `(k/8, 0.5, 0.75, 0.6)`, k the frame number modulo 8, so each image shows
which frame it holds; a quad over the left half writes a ramp (red u, green v, blue 0.5, alpha
0.25 + 0.5u). A marker (magenta, alpha 0.8) can be drawn with GL over the top right corner at
the end of a frame.

* `read.*`: `Texture2D.ReadPixels` of the screen into float, byte and linear byte textures at the
  end of a frame, and in Update.
* `asTexture.*`: `CaptureScreenshotAsTexture` in Update, at the end of a frame (twice), after a
  marker drawn at the end of the frame, for superSize 0, 1, 2, 3 and -1 and each stereo mode.
* `mip.*` (last): a quad over the right half with a mipmapped trilinear texture whose levels are
  solid colours (red level / 8), captured at superSize 1, 2 or 3 and 1 again: iso (a square
  footprint), anisoMax (aniso level 16 under global limits 1 and 2), anisoForced (aniso level 1
  under ForceEnable with limits 2 and 16), anisoOne (aniso level 1 under Enable) and anisoOff
  (aniso level 16 under Disable), all but iso of a footprint of 64 by 4 texels.
* `proj.*` (last): superSize 2 and 3 of a perspective camera framing the same view, and superSize
  1 and 3 of a camera that is not orthographic but has the orthographic matrix set as its
  `projectionMatrix` (and whether it still has it afterwards).
* `rt.*`: `CaptureScreenshotIntoRenderTexture` at the end of a frame into ARGB32 sRGB and linear,
  ARGBFloat, a larger and a smaller target (cleared to `(0.125, 0.25, 0.375, 0.5)`), read right
  after the call and in the next frame; called in Update; a null target.
* `file.*`: `CaptureScreenshot` to absolute paths: called in Update with a marker drawn at the
  end of that frame and at the end of a frame before the marker (whether the file exists right
  after the call, at the end of the frame and in the next frames), superSize 2 and 0, twice in
  one frame, a `.jpg` name, no extension, each stereo mode, and a relative path (the working
  directory). Each file's first bytes, PNG header and chunk types, and the image `LoadImage`
  decodes.

Every case records its log messages and exceptions. In Gamma the Editor crashes (exit 139, in
both repeats) when `CaptureScreenshotAsTexture` is called in Update or with a superSize below one;
the probe skips those calls and the render texture capture in Update there, recording
`<case>.skipped`, and writes its values before every capture call.

## Recording

```
python3 tests/projects/screencapture/run.py editor --release 6000.0.71f1 --space Linear --label <fresh-label>
python3 tests/projects/screencapture/run.py publish --release 6000.0.71f1 --space Linear --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/screencapture/run.py unbound --release 6000.0.71f1 --space Linear --label <fresh-label>
python3 tests/projects/screencapture/compare.py tests/projects/screencapture/Reference/unity-6000.0.71f1/Linear \
    ~/.cache/unbound/validation/runs/screencapture/<label>/unbound --report <cache>/report.json
```

The same with `--space Gamma` (the project copies' color space set to Gamma). Each Editor runs
windowed on a private Xvfb with the GPU through VirtualGL (`ProbeRunner` opens the scene, sets the
game view to 64x32, enters Play mode and quits with the probe's status; a watchdog quits after
600 s). Runs use fresh project copies under `$XDG_CACHE_HOME/unbound/validation/runs/screencapture/<label>`;
the files the probe writes stay in the run's output directory. `publish` requires two
byte-identical complete captures and refuses to overwrite a reference. The comparator is the
render texture probe's (`tests/projects/rtdepth/compare.py`): the whole key union and every image
channel, absolute and relative tolerance 2e-4.

`Reference/unity-6000.0.71f1/{Linear,Gamma}` hold the values and images of two byte-identical
captures each (OpenGLCore, RTX 4060): Linear 286 values and 59 images, Gamma 273 values and 55
images. The `mip.*` and `proj.*` cases (last) were added later with `run.py extend`, each time
from two further byte-identical captures.

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* The game view is 8-bit RGBA, sRGB-encoded in Linear color space. `ReadPixels` of the screen at
  the end of a frame returns the stored bytes for float, byte and linear byte destinations alike,
  with the alpha written. In Update it reads the Editor window's framebuffer instead and logs
  "ReadPixels was called to read pixels from system frame buffer, while not inside drawing frame."
* `CaptureScreenshotAsTexture` reads the screen when it is called (a marker drawn before is in
  it): a readable RGBA32 texture, `R8G8B8A8_UNorm`, one mip, bilinear, repeat, with the stored
  bytes and alpha. In Update it holds the Editor window's framebuffer, without a message; in
  Gamma the Editor crashes there.
* superSize N gives N times the size, the cameras rendered again: pixel j samples the screen at
  j / N (pixel 0 is the screen's corner, not a pixel center). The screen is rendered N * N times
  at its own size, every camera's projection moved by (i / N - 0.5, j / N - 0.5) of its pixels,
  and tile (i, j)'s pixel (x, y) becomes pixel (x N + i, y N + j). During the capture every 2D
  texture with mipmaps samples 1 + log2 N levels finer (`mip.iso`: level 4 at superSize 1, 2 at
  superSize 2) and every texture filters at anisotropic level 16, whatever the quality setting and
  the global limits say (`mip.aniso*`: a footprint of 64 by 4 texels filtered at level 1 or 2
  outside the capture, also under Disable, reaches level 0 in it); after it the texture's
  `mipMapBias` reads its own value again and it samples as before. The offset follows the
  camera's `orthographic` flag, not its matrix: a perspective camera given an orthographic
  matrix gets the perspective offset, which moves its image by the view depth times the offset
  (`proj.customOrtho.superSize3` differs from the orthographic camera's capture by up to 0.87);
  the camera keeps its matrix. superSize 0 or below asserts
  "Assertion failed on expression: 'superSize > 0'" (below 0 also "Image invalid width!" and
  "Image invalid height!"), logs "Texture must have width greater than 0." and "No texture data
  is available to upload." and returns a 0x0 ARGB32 linear texture whose `GetPixels` throws
  (in Gamma the Editor crashes). Stereo modes capture the screen (no XR).
* `CaptureScreenshotIntoRenderTexture` draws the screen into the target when it is called, one
  texel per pixel from the corner: an sRGB target gets the same bytes, linear and float targets
  the decoded values (in Gamma every target gets the bytes), alpha kept; a larger target keeps its other texels, a smaller one is
  cropped. A null target logs "CaptureScreenshotIntoRenderTexture() The RenderTexture passed in
  argument is null. Please allocate it prior to calling this function."
* `CaptureScreenshot` writes the frame's final image after the end-of-frame callbacks (GL drawn
  in a `WaitForEndOfFrame` coroutine after the call is in it); the file exists from the next
  frame's Update. It is an 8-bit RGB PNG (IHDR, IDAT, IEND), without alpha, whatever the name's
  extension. Of two calls in one frame only the last is written. superSize 0 logs
  "CaptureScreenshot() the superSize argument must be greater than or equal to 1." and writes
  nothing. A relative name is under the working directory (the project folder).
* `LoadImage` of those PNGs gives ARGB32 textures (their `graphicsFormat` is a value the public
  enum does not name: 87 in Linear, 88 in Gamma and for the linear 0x0 texture above).
* In Gamma the screen holds the written values unencoded; everything above reads those bytes.

## Unbound

`Graphics/ScreenCapture.cs` implements the three calls as measured; the screen is 8-bit RGBA,
sRGB-encoded in Linear (`crates/unbound-app/src/render/executor.rs`), so readbacks, copies and
captures see its stored bytes. A frame's requested file is written in
`PostLateUpdate.GUIClearEvents`, after the end-of-frame callbacks, and `ReadPixels` of the screen
outside the frame's drawing (no camera rendering) logs the Editor's error. superSize N renders
the screen cameras N * N times at the screen's size with the Editor's projection offsets and
interleaves the tiles, with the mip bias and anisotropic limits the Editor sets for the capture;
overlay canvases and OnGUI are not drawn there (a warning says so).

Against the reference: Linear 286/286 values and 54/59 images, Gamma 273/273 values and 54/55
images. Retained: reads in Update (`read.update.float`, and in Linear `asTexture.update`,
`rt.update.*`): the Editor returns its window's framebuffer; Unbound returns the last frame. Those
images also differ from one Editor session to the next (the window's interface greys), so
`run.py extend` keeps them as recorded.
