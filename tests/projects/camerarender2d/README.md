# Camera.Render into a target texture

`Assets/Editor/CameraRender2DProbe.cs` records in Play mode, on the urp2d project, what
`Camera.Render()` leaves behind when a (disabled) camera with a target texture renders a red sprite
lit by a global 2D light: the colour category at the texture's centre and corner (red, the clear
colour's blue, black, other: no pixel reference), which render texture is active after the call (none,
the one active before, the target) and whether the camera keeps its target. Cases: at once after the
objects are made, a frame later, with another texture active before the call, the clear colour alone,
an enabled camera whose global light is made in the same step, and the built-in pipeline (the render
pipeline asset taken away at run time) at once and with another texture active.

Measured on the Unity 6000.0.71f1 Editor (two identical macOS batch captures; categories and the
active texture depend on neither the OS nor the graphics device):

- The render is in the texture at once, in every case.
- The render texture active before the call is active after it (none when none was), with URP and with
  the built-in pipeline; the camera keeps its target.

The [Camera.Render](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.Render.html)
page documents rendering into the target texture; which texture is active afterwards is an
observation of these captures.

```sh
python3 tests/projects/camerarender2d/run.py record --label A     # twice, A and B
python3 tests/projects/camerarender2d/run.py publish A B
python3 tests/projects/camerarender2d/run.py gate
```
