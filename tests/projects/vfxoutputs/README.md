# vfxoutputs probe

Visual Effect Graph (com.unity.visualeffectgraph 17.0.4, the Editor's built-in package, unmodified)
under URP 17: how the outputs of the probe's own small graphs are drawn, recorded by the same
script in the Unity Editor and in Unbound. The `vfx` probe covers the import, the API and the
simulation, and the outputs that draw every particle a system has room for; this one covers the
outputs that draw through an indirect buffer, sorted outputs, the order of an effect's
outputs, and triangle and octagon outputs.

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
- `Glow`: thirty flat quads of one colour (a white texture the authoring step writes) over each
  other, added to what is behind: the more overlap, the brighter, in any order, so a particle
  drawn twice shows.
- `Late`: `Still`'s particles, for an effect made while the others play.

Sorted outputs (`Assets/OutputGraphs/Sort*.vfx`): thirty alpha-blended quads (alpha 0.6, size
0.7) of random colours over each other, sorted by the output's sort mode, which the package sorts
with its own kernels (Shaders/Sort.compute) after the kernel that writes each particle's key:
`SortDistance` (distance to the camera, for each camera), `SortDepth` (camera depth, for each
camera), `SortReverse` (distance, reversed), `SortCustom` (a custom key of 1 for all: the order of
equal keys), `SortKeyed` (a custom key of each particle's own, drawn at random: not the order of
the particles' places), `SortYoungest` (thirty a second living 0.3 to 1.5 seconds, youngest in
front: dead particles' places are taken again, so age is not the order of places), `SortMany`
(6000 quads sorted for each camera: values only, see "Not covered") and `SortKeyedMany` (20000 quads sorted at each step by a key of their own: merged
three times, through the scratch list and the list itself).

Triangles and octagons (`Assets/OutputGraphs/Triangle*.vfx`, `Octagon*.vfx`): `TriangleStill`,
`OctagonStill` (forty opaque primitives, each with a place, a depth, a colour and a turn of its
own; octagons with a crop factor of 0.3, which pulls their odd corners in), `TriangleList`,
`OctagonList` (the same through the update kernel's list) and `TriangleMany`, `OctagonMany`
(30000 additive primitives: more than one draw's 16-bit indices reach for octagons, drawn as
instances of 1024; triangles in one draw).

`Priority`: two systems over the same particles (one seed), the first with two outputs and the
second with one, unsorted and alpha blended, each output of a colour of its own and a label of its
own, with output priorities (`sortPriority`, the asset's output render order) of 2 and 0, then 1.
`PriorityTies`: two systems of two such outputs each, all of priority 0, added to the graph one of
each system in turn.

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
- `glow` (frames 3 and 10): `front` and `part` in each frame: each camera's list is made anew
  for it (a list not emptied for the second camera would draw its particles twice).
- `SortDistance`, `SortDepth`, `SortReverse`, `SortCustom` (frames 3 and 10): `front` and
  `offset` (2 units right, 1.5 up, 6 units away: the distances and depths order the quads
  otherwise) in each frame.
- `SortKeyed` as the four above; `SortYoungest` (frames 30 and 45); `SortKeyedMany` (frame 5);
  `SortMany` (frame 5, values only).
- `TriangleStill`, `TriangleList`, `TriangleMany`, `OctagonStill`, `OctagonList`, `OctagonMany`
  (frame 3).
- `Priority` (frame 3): the outputs drawn by their priority, the lowest first, across systems;
  `PriorityTies` (frame 3): equal ones in their systems' order, then each system's.
- `Effects` (frame 3): two effects over each other (`SortKeyed`, and `SortDistance` made after it
  and two units farther from the camera): the farther drawn first, against the order they were
  made in.
- `late` (frame 10): made in frame 6, after the others' draws began: its outputs take entries of
  indirect arguments of their own (see "Not covered" for a renewal of the entries' buffer).
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
- `SortMany` is not shot either: its list is made and sorted for each camera, and two captures of
  the same project drew it differently in the Editor (3820 of 4096 pixels), while two earlier ones
  agreed; the step-sorted `SortKeyedMany` (20000) agreed in every capture.
- The `FrustumCulled` route of `many` is not shot. In the Editor (OpenGL Core, NVIDIA) the lists
  made for each camera of large systems are drawn incompletely and differently from capture to
  capture: of 20000 particles, 5 were drawn in one capture and 32 in another; in two further
  captures (outside Git) with dots of 1.7 pixels at 256x256, systems of 64, 1024, 16385 and 20000
  particles drew what the compute-culled route draws in all six shots, one of 4096 drew it in five
  shots and 2941 lit pixels instead of 10680 in the sixth, and one of 16384 drew a few particles
  elsewhere than that route in all six shots, the same way each time (32434 lit pixels instead of
  32460). The other routes draw every particle in every shot. The package's
  documentation (Visual Effect Graph 17.0, Shared Output Settings, Frustum Culling: "cull particles
  that are outside of the camera frustum in a compute pass") leaves particles inside the frustum
  drawn; Unbound draws every particle the kernel keeps. The per-camera controls here have 64
  particles at most, where every capture agreed.
- Frustum planes: URP 17 declares the planes the output-update kernel culls with
  (`_FrustumPlanes`) and never sets them (read off the pipeline's sources); Unbound gives the
  kernel none, so it culls nothing. What the Editor's kernel reads is not measured: no image
  here can tell, as a particle out of a camera's view is not seen either way. (Its shadow would
  be: an output of this kind that casts shadows is not measured, here or in
  `tests/projects/vfxlit`, whose casters draw themselves.)
- Alpha-blended outputs with sorting off are drawn but not measured here (their order shows).
- Triangles and octagons sorted, through a list made at each step or for each camera, lit, or
  through an indirect buffer beyond 8192 octagons; octagons cropped by more than one half; cull
  modes other than the output's default.
- The arguments' buffer made anew after draws took it: the stages reserve 27 entries before the
  first fill (one buffer of 32 is made for them) and `late` adds three, which fit. An effect whose
  entries outgrow the buffer once others drew (made while a camera collects, or more than fit)
  has Unbound keep the outgrown buffer until a later frame and fill every entry again; no control
  makes one.
- The order of an unsorted list: the package documents it as not deterministic (Shared Output
  Settings, Sort: an alpha-blended output with sorting off flickers), so no control blends
  overlapping quads whose order shows.
- Quads seen at an angle: crossing quads are ties of depth that the Editor's own graphs decide
  differently (the three routes of one graph in one capture).
- A system that sorts one list for all its outputs (the compiler's global sort, for outputs that
  share their sort), other primitives, lit outputs, motion vectors, strips, meshes and Shader
  Graph outputs: Unbound refuses them loudly. Outputs of one system named alike (no label: the
  graph's API makes them so; the Editor's window gives them letters) are refused too: Unbound
  finds generated shaders by name.
- Sorting on a device whose compute groups share less than 32 KB of memory; effects over each
  other at one distance.
- Several instances of one asset in one batch: Unbound runs each effect as a batch of its own.
