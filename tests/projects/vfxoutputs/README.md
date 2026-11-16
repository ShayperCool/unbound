# vfxoutputs probe

Visual Effect Graph (com.unity.visualeffectgraph 17.0.4, the Editor's built-in package, unmodified)
under URP 17: how the outputs of the probe's own small graphs are drawn, recorded by the same
script in the Unity Editor and in Unbound. The `vfx` probe covers the import, the API and the
simulation, and the outputs that draw every particle a system has room for; this one covers the
outputs that draw through an indirect buffer.

The Editor records in a window (not in batch mode): in batch mode no camera ever sees an effect.

## Graphs and scene

`Assets/Editor/Authoring/GraphAuthoring.cs` builds the graphs in the Editor through the package's
own models; its assembly carries the name of one of the package's friend assemblies
(`Unity.VisualEffectGraph.EditorTests`), which is what lets it reach them. Each system is a spawner,
an initialize, an update and an unlit quad output whose sort is off, in local space, its quads
facing +z (no orientation block). Every graph comes in three routes, which differ in how the
output finds the particles to draw: a list of the particles to draw, counted on the GPU, whose
count gives the draw's arguments:

- `UpdateList`: the output asks for an indirect draw: the update kernel appends each particle it
  keeps alive to the system's list, emptied before each step.
- `ComputeCulled`: the output culls in a compute pass: an output-update kernel that runs after the
  update at each step makes the output's own list.
- `FrustumCulled`: the output culls by the camera's frustum: its output-update kernel runs for
  each camera that sees the effect, before that camera's draws, and makes the list for that camera.

The graphs of each route (`Assets/OutputGraphs/<Route><Graph>.vfx`):

- `Still`: forty opaque quads at once, each with a place in a 2-unit cube, a colour of its own and
  a size of 0.4.
- `Dying`: forty opaque quads a second living between 0.2 and 1.5 seconds: dead particles lie
  between those alive in the system's buffers, and the list skips them. `Away`: the same graph as
  an asset of its own (effects of one asset share their alive-count readback in the Editor).
- `Many`: a burst of 20000 additive quads, more than the 16384 one draw's 16-bit indices reach,
  drawn as instances of 2048.
- `Blend`: thirty half-transparent quads over each other, alpha blended in the order of the list
  (unsorted).

`Assets/Scenes/Probe.unity` holds only a camera that sees none of the stages. The Editor-authored
files (graphs, scene, the package lock) are kept in `Authored/unity-<release>/default`
(`tests/authored_project.py`); the gate assembles them.

## What is recorded

`Assets/Scripts/VfxOutputsProbe.cs` (30 frames a second, captured) puts each effect far from the
others with a start seed of 11 and gives it orthographic cameras of its own, each rendering into a
64x64 texture; at the end of the frames named it saves each camera's image as
`draw-<route> <graph>-<camera>-f<frame>.png` (the colour only: see below) and writes
`draw/<route> <graph>/f<frame>` (`values.jsonl`, `{"k": key, "v": value}` per line): the effect's
`aliveParticleCount`, `culled`, `HasAnySystemAwake()` and its renderer's `enabled`/`isVisible`.

- `still` (frames 3 and 10): three cameras in each frame: `front` sees the whole cloud from z = -10;
  `part` sees a corner of it (the rest is outside its view); `inside` stands in the middle of the
  cloud (half of the particles are behind it, most of the rest outside its 1.2-unit view).
- `dying` (frames 12, 30, 45): the lists skip the dead particles as the system recycles them.
- `many` (frame 5): 20000 quads through the list, drawn as instances.
- `blend` (frame 5): the blending follows the order of the list.
- `away` (frames 10, 19, 30): the camera looks away from the effect until frame 20, then at it: the
  effect is culled (its renderer is not visible, it does not simulate and asks for no alive count),
  then drawn. Its alive count is -1 until the first count read back after it is seen.

The last row is `capture/graphs imported while playing`; it must be empty for a capture to be
complete (Assets/Editor/ProbeRunner.cs waits five editor updates before playing, as the packages
import a new project's graphs again in the first one).

Values compare exactly and the images pixel for pixel (`image_limits.json`: zero, measured on one
machine, an NVIDIA driver, the Linux Editor on OpenGL Core).

## Not covered

- The images are the cameras' colour without alpha. An opaque output writes its texture's filtered
  alpha, which the Editor's own graphs give one unit apart from each other where a texel is
  magnified (the three routes of one graph in one capture); nothing is drawn with it here.
- The `FrustumCulled` route of `many` is not shot: in the Editor (OpenGL Core, NVIDIA) its images
  show a different small part of the 20000 particles in each capture (5 and 32 in two captures of
  the same project), while the other routes draw them all. Unbound draws them all.
- URP 17 does not set the frustum planes the output-update kernel culls with (`_FrustumPlanes`):
  frustum culling culls nothing there, and no image could tell (a particle out of a camera's
  view is not seen either way). Culling by another pipeline's planes is not measured.
- Quads seen at an angle: crossing quads are ties of depth that the Editor's own graphs decide
  differently (the three routes of one graph in one capture).
- Sorted outputs (alpha blended ones are sorted by default), other primitives, lit outputs, soft
  particles, motion vectors, strips, meshes and Shader Graph outputs: Unbound refuses them loudly.
- Several instances of one asset in one batch: Unbound runs each effect as a batch of its own.
