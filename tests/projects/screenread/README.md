# Screen reads before the frame's first camera

Unity 6000.0.71f1, built-in pipeline, a 64x32 game view in Play mode
(`Assets/Scripts/ScreenReadProbe.cs`, Linear color space).

One camera renders to the screen for ten frames. Before it renders (`Camera.onPreRender`, the
first render of each frame) the probe reads the screen with `Texture2D.ReadPixels` (`pre.r{k}`),
and again at the end of the frame (`eof.r{k}`). The camera clears to `(k/8, 0.5, 0.75, 0.6)` except
on frames 0, 2, 3, 6 and 8, where it clears only depth, so the end-of-frame read shows what the
screen started with. At the end of frames 2 and 5 the Game view's rendering resolution changes
with `UnityEditor.PlayModeWindow.SetCustomRenderingResolution` (64x32 to 80x40 and back;
`resize.r{k}` records `Screen`'s size right after the call). Each read records the screen size and
its distinct RGBA32 colors with their pixel counts.

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* Before the first camera of a frame draws, the screen holds the previous frame.
* On the first rendered frame, and on the first frame after the rendering resolution changes
  (also back to a size used earlier), it is transparent black `(0, 0, 0, 0)`: a camera that clears
  only depth draws over that.
* A resolution set during a frame is not yet `Screen`'s size right after the call; the next
  frame's rendering has it.
* The Editor renders its first frame twice; the probe reads only the first render of a frame.

Reads in Update (outside rendering) return the Editor window's framebuffer in the Editor
(see tests/projects/screencapture) and are not part of this probe.

## Recording

```
python3 tests/projects/screenread/run.py editor  --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/screenread/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/screenread/run.py unbound --release 6000.0.71f1 --label <fresh-label>
```

`unbound` runs the engine at 64x32 and compares every record with the reference (missing and
extra keys count; no tolerance: bytes and counts are exact). Each Editor runs windowed on a private
Xvfb with the GPU through VirtualGL; `publish` requires two complete, byte-identical captures and
refuses to overwrite the reference.

`Reference/unity-6000.0.71f1.jsonl` holds the 23 records of the pair `q254-b` (both exit 0 on the
NVIDIA GeForce RTX 4060, byte-identical).
