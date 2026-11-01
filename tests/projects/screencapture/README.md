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
captures each (OpenGLCore, RTX 4060): Linear 187 values and 39 images, Gamma 174 values and 35
images.

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
  j / N (pixel 0 is the screen's corner, not a pixel center). superSize 0 or below asserts
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
the screen cameras again into a target N times the size, shifted half of its pixel; overlay
canvases and OnGUI are not drawn there (a warning says so).

Against the reference: Linear 174/187 values and 33/39 images, Gamma 163/174 values and 33/35
images (Root's runtime with stubs: Linear 88/187 values and 0/39 images). Retained:

* `LoadImage` of a PNG gives RGBA32 with `R8G8B8A8_SRGB`/`B8G8R8A8_UNorm` names, not ARGB32 with
  87/88 (11 `*.image.texture` values per space, and the 0x0 texture's `graphicsFormat` in Linear):
  texture format gaps outside ScreenCapture.
* Reads in Update (`read.update.float`, and in Linear `asTexture.update`, `rt.update.*`): the
  Editor returns its window's framebuffer; Unbound returns the last frame.
* `asTexture.superSize3`: 567 (Linear) and 191 (Gamma) of 18,432 pixels differ by one 8-bit step,
  all at samples on the screen's pixel corners where the ramp's 8-bit value lies within 0.06 of
  a rounding boundary; superSize 2 matches.
