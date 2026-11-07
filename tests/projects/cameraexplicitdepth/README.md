# Explicit camera target depth

Focused Q45 companion to `camerastack`, built-in forward rendering, Unity 6000.0.71f1.
All cameras are disabled and invoked sequentially with `Camera.Render()`. No automatic
camera ordering is asserted. Targets remain owned, created and assigned for both frames.

Each case initializes a 96x64 color/depth target with a direct SolidColor camera, including
a red quad at distance 4. The tested camera draws green at distance 8 and blue at distance 2,
using Nothing, Depth or SolidColor. A direct Nothing camera then draws yellow at distance 6
to expose the depth left in the explicit target. Repeat the tested and witness cameras on
the same target next frame. Capture the initialized target and every tested/witness result.

Before each tested render, an unrelated HDR camera clears and draws into another owned byte
target. Its magenta quad is either at distance 1 or 12. This exercises intermediate reuse
with unrelated depth history without asserting anything about a newly allocated target.
Modes cover direct byte/half-float targets, direct four-sample targets and HDR-to-byte
intermediates with quality AA 0/4 and target samples 1/4. Full rect only, no image effects.

## Unity contract and cause table

Documentation checked for release 6000.0 before implementation:

| Source | Applicable guarantee or limitation | Probe interpretation |
| --- | --- | --- |
| [Camera.clearFlags](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-clearFlags.html), [CameraClearFlags.Nothing](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CameraClearFlags.Nothing.html) | Nothing keeps existing color and depth. | Test initialized depth with farther and nearer geometry. |
| [Built-in clear flags Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/camera-background-birp.html) | Depth only discards depth while retaining color; Don't Clear retains both. On some GPUs screen history can be undefined. | Include clearing controls; no claim about mobile screen history. |
| [Camera.targetTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-targetTexture.html) | Rendering goes to the assigned texture, or the screen when null; texture rendering uses the entire target and ignores camera rect/pixelRect. | Only full-rect explicit targets here. |
| [Camera.Render](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.Render.html) | Uses camera settings, sends rendering callbacks and runs image filters. Disabled cameras allow explicit render ordering. Cannot recursively render the currently rendering camera. | Sequential disabled cameras, no recursion or effects. |
| [RenderTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.html), [Release](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.Release.html) | Initial contents are undefined; resources can be lost; Release frees hardware resources and later use can recreate them. | Initialize before observing; verify IsCreated and ownership; do not release until both frames finish. |
| [GetTemporary](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.GetTemporary.html) | Temporaries are pooled and discarded; their previous contents cannot be relied on. | Unrelated renders exercise engine intermediates; their old depth must not determine owned-target rendering. |

Intermediate selection, initialization and resolve depth history are measured implementation
details, not additional universal Unity guarantees. New/discarded target contents, deferred
rendering, partial viewports, image effects and other RT lifetime transitions are outside scope.

## Recording and validation

The shared `camerastack/run.py` driver creates clean project copies in the global Unbound
cache, runs two independent GPU Editor captures through `tests/editor_runner.py`, checks
completion and byte agreement, and refuses to overwrite an existing release reference.
All capture logs and engine candidates remain outside Git.

```sh
python3 tests/projects/cameraexplicitdepth/run.py editor --release 6000.0.71f1 --space Linear --label native-Linear
python3 tests/projects/cameraexplicitdepth/run.py publish --release 6000.0.71f1 --space Linear --label native-Linear
# Repeat for Gamma with a distinct label.
UNBOUND_CLI=$UNBOUND python3 tests/projects/cameraexplicitdepth/run.py gate --release 6000.0.71f1
```

`gate.json` declares an engine-only gate for both spaces with an explicit release. It uses the
unchanged `camerastack/compare.py`, retaining every missing/extra/failing value and image and
its existing 3/255 channel tolerance. No Editor runs from the gate or sheriff suite.
