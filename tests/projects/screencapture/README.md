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

Every case records its log messages and exceptions.

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
captures each (OpenGLCore, RTX 4060).

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* The game view is 8-bit RGBA, sRGB-encoded in Linear color space. `ReadPixels` of the screen at
  the end of a frame returns the stored bytes for float, byte and linear byte destinations alike,
  with the alpha written. In Update it reads the Editor window's framebuffer instead and logs
  "ReadPixels was called to read pixels from system frame buffer, while not inside drawing frame."
* `CaptureScreenshotAsTexture` reads the screen when it is called (a marker drawn before is in
  it): a readable RGBA32 texture, `R8G8B8A8_UNorm`, one mip, bilinear, repeat, with the stored
  bytes and alpha. In Update it holds the Editor window's framebuffer, without a message; in
  Gamma the Editor crashes there (the Gamma probe skips the two calls in Update).
* superSize N gives N times the size, the cameras rendered again: pixel j samples the screen at
  j / N (pixel 0 is the screen's corner, not a pixel center). superSize 0 or below asserts
  "Assertion failed on expression: 'superSize > 0'" (below 0 also "Image invalid width!" and
  "Image invalid height!"), logs "Texture must have width greater than 0." and "No texture data
  is available to upload." and returns a 0x0 ARGB32 linear texture whose `GetPixels` throws.
  Stereo modes capture the screen (no XR).
* `CaptureScreenshotIntoRenderTexture` draws the screen into the target when it is called, one
  texel per pixel from the corner: an sRGB target gets the same bytes, linear and float targets
  the decoded values, alpha kept; a larger target keeps its other texels, a smaller one is
  cropped. A null target logs "CaptureScreenshotIntoRenderTexture() The RenderTexture passed in
  argument is null. Please allocate it prior to calling this function."
* `CaptureScreenshot` writes the frame's final image after the end-of-frame callbacks (GL drawn
  in a `WaitForEndOfFrame` coroutine after the call is in it); the file exists from the next
  frame's Update. It is an 8-bit RGB PNG (IHDR, IDAT, IEND), without alpha, whatever the name's
  extension. Of two calls in one frame only the last is written. superSize 0 logs
  "CaptureScreenshot() the superSize argument must be greater than or equal to 1." and writes
  nothing. A relative name is under the working directory (the project folder).
* `LoadImage` of those PNGs gives ARGB32 textures (their `graphicsFormat` is a value the public
  enum does not name: 87, 88 for a linear texture).
