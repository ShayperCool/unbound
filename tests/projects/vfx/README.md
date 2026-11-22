# vfx probe

Visual Effect Graph (com.unity.visualeffectgraph 17.0.4, the Editor's built-in package, unmodified)
under URP 17: what the Visual Effect module answers about the probe's own small graphs, recorded by
the same script in the Unity Editor and in Unbound. Unbound imports a graph by running the
package's compiler (`managed/UnityEngine/Editor/VFX`) and runs its systems
(`managed/UnityEngine/VFX`); this probe covers the import, the API that needs no simulation and
the simulation as scripts can observe it. Outputs are not drawn yet: Unbound warns once at load
and `unbound doctor` lists the effects.

The Editor records in a window (not in batch mode): in batch mode no camera ever sees an effect,
so effects that cull their simulation (the default) never run.

## Graphs and scene

`Assets/Editor/Authoring/GraphAuthoring.cs` builds everything in the Editor through the package's
own models; its assembly carries the name of one of the package's friend assemblies
(`Unity.VisualEffectGraph.EditorTests`), which is what lets it reach them. The graphs:

- `Empty`: no contexts; it compiles to nothing.
- `Basic`: a spawner at a constant rate, initialize (lifetime, velocity), update, quad output.
- `Exposed`: one system and an exposed property of every type the blackboard offers (numbers,
  vectors, a color, spaceable positions in local and world space, a direction, a vector, a curve,
  a gradient, every texture dimension, a mesh, a skinned mesh renderer, a graphics buffer, a
  sphere, a box, a transform), with names that sort differently by culture and by code point,
  and one property that is not exposed.
- `Exposed2`: shares names with `Exposed`: `Rate` with the same type, `Count` with another.
- `Events`: two systems; custom events `Fire` and `Cease` start and stop the first spawner.
- `Attributes`: a system whose particles take `lifetime`, `velocity`, `color`, `alive` and `seed`
  from the spawn event, so the graph's event attributes have floats, vectors, a bool and an
  unsigned integer. `Attributes2`: `color`, `size` and `lifetime`, in another order.

- `SimRate`, `SimRateDelta`, `SimRateExact`, `SimRateNoCull`, `SimRateBoundsOnly`,
  `SimRatePrewarm`, `SimBurst`, `SimRandom`, `SimLoop`, `SimLoopRandom`, `SimKill`, `SimKeep`:
  `SimExpressions`, `SimTexture`, `SimChain`: the simulation controls' graphs (below), kept in
  `Assets/SimGraphs` apart from the graphs the import rows list.
- `DrawOpaque`, `DrawAdditive`, `DrawMany`, `DrawTextured`, `DrawCylinder`, `DrawCollide`, `DrawOverLife`, `DrawHdr`
  (`Assets/DrawGraphs`): the drawing controls' graphs (below).
- `CamValues`, `CamDepth`, `SoftAdditive` (`Assets/CameraGraphs`): the camera controls' graphs
  (below).
- `OutEvents` (`Assets/EventGraphs`): the output event controls' graph (below).
- `OpsValues` (`Assets/OpGraphs`): the operation controls' graph (below).
- `OpsNoise` (`Assets/OpGraphs`): the noise controls' graph (below).
- `OpsEvent` (`Assets/OpGraphs`): the graph of the controls of event attributes read in a spawn
  context (below).
- `OpsMesh` (`Assets/OpGraphs`), `DrawMesh`, `DrawMeshSurface` (`Assets/DrawGraphs`): the mesh
  controls' graphs (below), over the meshes `MeshMixed` and `MeshWide` the authoring script makes
  in `Assets/Objects`.

`Exposed`'s texture and mesh properties default to `Assets/Objects/ImageA.png` and
`ShapeA.asset`, which the authoring script also makes (with `ImageB` and `ShapeB`).

`Assets/Scenes/Probe.unity` holds effects saved by the Editor: `Plain` (Basic), `Overridden`
(Exposed with overridden numbers, vectors, a curve, a gradient, a texture and a mesh, a start
seed, an initial event), `Events`, `NoAsset`, `Expressions` (SimExpressions with one override).

`Assets/Editor/Authoring/RuntimeDataCases.cs` (the same friend assembly) hands a resource
compiled graphs made by hand, one case each: valid ones, system layers that decrease, are equal
or have a layerless system between them, expressions with an unknown operation, no result type,
a parent outside the sheet, a component outside its parent, a curve or gradient sampled from
another type, a word beyond an operation's parents outside the sheet, and an event named twice.

The Editor-authored files (graphs, scene, the package lock) are kept in
`Authored/unity-<release>/default` (`tests/authored_project.py`); the gate assembles them.

## What is recorded

`Assets/Scripts/VfxProbe.cs` writes `values.jsonl` (`{"k": key, "v": value}` per line; floats as
the hex of their bits) into `VFX_PROBE_OUT`:

- `tables/*`: the graph compiler's tables, read through reflection: the type every expression
  operation produces for each type a data word can name, the size of every value type (with the
  error logged for a type without one), its texture dimension; values outside the enums too;
  the runtime data version.
- `asset/<graph>/*`: exposed properties with their listed types (in the asset's order), each
  one's texture dimension and space, the same for an unknown name, events, the objects the import
  made with their file ids (compute shaders, the output shader and its material), the systems by
  kind.
- `scene/<effect>/*`: the serialized effects: asset, components, state, system names, and for
  every exposed property which typed `Has*` answer true, its value by the getter of its type and
  what that logged; an unknown name through several getters.
- `scripted/*`: an effect added from script: its defaults and renderer; every setter with the
  right type, a texture of another dimension, wrong types and unknown names (what is logged, what
  the value is afterwards); curves and gradients are copies; a clone carries the overrides;
  `ResetOverride`; another asset assigned and the first one again (which overrides follow), the
  same asset again, none and the first one after none (the overrides saved when the effect was
  cloned return, later ones do not), object overrides through an asset without those
  properties; state set from script and the initial event with and without an asset.
- `attributes/*`: event attributes per graph: which names and types the layout has, starting
  values, writes and reads with the right and a wrong type, copies, null arguments, use after
  `Dispose`; `attributes/layouts/*`: the two graphs with several typed attributes, written and
  read by type, and `CopyValuesFrom` in both directions between their layouts (shared names
  take the other set's values, the rest keep theirs).
- `runtime data/*`: what each hand-made compiled graph logs and throws when a resource takes
  it, and whether the resource holds the shader source handed over with it afterwards.
- `find/*`: which of the project's assets `AssetDatabase.FindAssets` lists for the graph types
  and for the types of the objects a compiled graph has (a shader, compute shaders, a material).
- `manager/*`: the fixed time step and the maximum delta time of the project's settings.
- `spawner state/*`: a `VFXSpawnerState` made with `new` (it loops without end; its setters,
  and how setting a stage passes the stages of no duration), and a copy of a system's state
  (its own event attributes; changing it leaves the system's alone).
- `sim/*`: the simulation, see below.

Values compare exactly (tolerance 0).

## Simulation

The probe makes its own effects at the first frame (`sim/<control>/f<frame>`), in front of the
scene's camera unless said otherwise, with a start seed and without a new seed on play, at
`Time.captureFramerate = 30`. Each frame it records, before that frame's effect update, the
effect's `aliveParticleCount`, `culled` and every spawn system's state through
`GetSpawnSystemInfo` (stage, `playing`, `newLoop`, spawn count, delta and total time, the loop's
durations, index and count). The graphs of these controls spawn particles that never die, so
the alive count is the particles spawned so far, exactly, each frame:

- `rate`, `rate delta`, `rate exact`: 7.3 particles a second into a capacity of 20, with each
  update mode (one step of the frame's fixed steps, the frame's delta time, each fixed step on
  its own). From frame 37 the frames are a tenth of a second (longer than the longest step),
  from 41 the time scale is one half.
- `rate prewarm`: five prewarm steps of a tenth of a second.
- `burst`: one burst after a delay, and a periodic one.
- `random`, `random other seed`: counts, delays, periods and a variable rate drawn from ranges
  (the effect's random sequence, per seed).
- `loop`, `loop random`: a spawn context that loops twice with a delay before and after, and
  one whose durations are drawn.
- `behind`, `behind no cull`, `behind bounds only`: effects behind the camera, with each
  culling flag.
- `control`: one effect stopped, played, paused, advanced a frame, played at another rate,
  stepped by hand (`Simulate`, with zero steps too), started over (`Reinit`), slowed to rate 0.
- `events`: custom events starting and stopping one spawner, several in one frame, and an
  event the graph does not have.
- `late`: an effect made at frame 20.
- `scene <name>`: the scene's own effects in their first frames.
- `kill`, `keep` (`settled/*`): ten particles that die after 0.6 s, or after 100 s, counted
  at frames 1, 100 and 170 (a system whose particles die reads its count back from the GPU, some
  frames late and once in 60 frames: the rows are taken where the count is settled), with the
  system's info and `HasAnySystemAwake`.
- `errors/*`: the system getters with names of the other kind, unknown names and a null state.
- `expressions` (`inputs/<play><update or stop>/<input>`): a spawn context with a block written
  as a script (`Assets/Callbacks/ProbeInputs.cs`, a `VFXSpawnerCallbacks`) whose inputs are fed
  by operators over exposed properties, so that nothing folds into a constant: what the block
  reads through `VFXExpressionValues` when the context plays, in the three updates after, when
  it stops and when it plays again is what the effect's expressions evaluated to. Inputs: `f*`
  functions of one and two floats, values of other types met by a float input, a branch each
  way, a number drawn once for the effect's seed and one drawn at each evaluation, the effect's
  time; `i*`, `u*` whole numbers (division by zero, wrapping, bit operations, conversions, the
  seed, the spawn state, how far the manager's update count moved); `b*` comparisons and logic;
  `v*`, `q*` vectors through a transform's matrix, its inverse, its transpose and their
  product; `m*` those matrices; `g*` whether the game's time as the graph reads it is
  `Time`'s; `b17` to `b20` a bool from an unsigned number and from a float, each from one that
  is not zero and from one that is; `c00`, `r00` an exposed curve and an exposed gradient
  sampled at an exposed time, `When` (the `others` control gives the effect another curve,
  a gradient that is fixed between its keys and a time between that gradient's keys). An input the block does not have, and an input read as
  another type, are recorded too. The same effect's rows record two event attributes a block of the context sets.
- `expressions others`: the same graph with other values (a NaN, a divisor of zero, other
  signs, a frame of no depth), set in the frame the effect is made and again at frame 4. In
  the frames between and after, the curve it samples is one with slopes at its keys, keys no
  power of two apart, a ping-pong before them and a loop after them, sampled between two
  keys (frame 2), past the last key (frame 3), on the last key (frame 6: the first key's
  value, where it loops) and before the first key (frame 8). The values tell how the curve is
  rounded: between the keys and where it loops, each segment as a cubic in the time from its
  first key; where it ping-pongs, as the sum of the segment's Hermite terms with the second
  key's value added last. Both differ in the last bits from what Unbound's
  `AnimationCurve.Evaluate` computes (whether a script's `Evaluate` rounds as the graph does
  in the Editor is not recorded here).
  `expressions scene`, `expressions copy`: the scene's effect of that graph (saved with one
  override) and a copy of it made in the first frame, each given another value at once.
  `expressions late`: one made at frame 20 and given a value at once. `override timing/*`,
  `override first frame`, `override second frame`: values set from script in the first and in
  the second frame, read back in `Update`, in `LateUpdate` and at the end of each frame, and
  what the effects then spawn with (the values stay).
- `late fresh`, `late second`: effects of a graph no other effect uses, made at frames 20 and
  24 and given a rate at once.
- `chain`: a spawn context whose burst starts another, and one whose burst stops it.
- `texture`, `texture other`, `texture later` (`settled/*`): ten particles whose lifetime is a
  hundred times the red of a texture, read by the initialize kernel on the GPU: the graph's red
  image; a green one set in the first frame; a green one set at frame 4 of an effect started
  over at frame 5.
- `toggle`, `toggle object`: an effect disabled at frame 6 and enabled at frame 10, by its
  component and by its object, with its renderer's `enabled` and `isVisible` while it is off
  and once it is on again (`renderer/f008`, `f012`); frames 7 to 9 show that it keeps its
  systems as they were. `toggle at once`, `toggle object at once`: disabled and enabled within
  frame 6. `disabled`, `inactive`, `manager/components`: which effects
  `VFXManager.GetComponents` lists (the getters of an effect that is off from its first frame
  answer a new effect's values, whether or not it has systems: these rows do not tell).
- `math/inverse affine/*`: `Matrix4x4.Inverse3DAffine` of the expressions' frame (the numbers a
  graph's inverse gives), of a matrix without an inverse, of matrices whose determinant is on
  either side of where the inverse stops (3.2e-13 and 3.1e-13, of each sign; a scale of 1e-4
  and of 5e-5 in every direction) and of one with a cell that is not a number (which
  not-a-number comes back is not compared).
- `capture/graphs imported while playing`: none. The packages import a new project's graphs
  again in the Editor's first update (their versions in the project settings, the render
  pipeline's material upgrade), and an import between two frames makes every effect anew from
  what was saved: the capture lets the Editor settle before it plays
  (`Assets/Editor/ProbeRunner.cs`), and this row shows that it did. A capture that lists a
  graph here is left without its last row, so it cannot be published.

Observed and kept: an effect counts as culled until its first update, and is not culled in
that update, whenever it is made (the first frame or frame 20); `aliveParticleCount` is -1 for a
system whose particles die until its first count arrives. A value a script gives a property
stays, in the first frame as in any other. An effect's renderer is disabled while the effect
is disabled or its object inactive; an effect enabled again starts over, and its first update
after frames it was off in takes its initial event without simulating (no camera took its
renderer in the frame before), while one disabled and enabled within a frame simulates at
once. A value no expression evaluated yet has every bit set. The smallest int divided by -1 in
a graph stops the Editor, as reading the spawn count of a `VFXSpawnerState` made with `new`
does: neither is among the controls.

Known residual (`residuals.json`): the coroutine of `override timing` that waits for the end of
the frame resumes in the first frame in this capture and from the second frame in Unbound,
which follows what `tests/projects/eofstartup` measured. What makes the two captures differ
(the render pipeline, how long the Editor settles, the Game view's focus) is not isolated.

## Drawing

Effects of the graphs kept in `Assets/DrawGraphs`, each far from everything else in front of an
orthographic camera of its own that renders into a 64x64 texture; at the frames named, at the
end of the frame, the texture is saved (`draw-<control>-f<frame>.png`) with what the effect
says of itself (`draw/<control>/f<frame>`). Their outputs are quads the output draws itself
(opaque or additive: nothing sorts them, and no buffer of indices stands between the system
and the draw), the kind Unbound draws:

- `opaque` (frames 3, 10): forty opaque quads spawned at once, each with a place, a depth and
  a colour drawn for it (the particles' random numbers, the attributes the vertex program
  reads, the depth test between quads).
- `additive` (frames 12, 30): thirty a second from places along a line, rising and living
  half a second (particles that die are left out by the vertex program; the velocity moves
  them each step).
- `many` (frame 5): twenty thousand small quads, more than one draw's 16-bit indices reach
  (16384): drawn as instances of 2048.
- `textured` (frame 5): nine opaque quads with an image of the project's as texture
  (`Assets/Objects/ImageC.png`, whose four quarters differ: a texture drawn turned or mirrored
  would show).

- `cylinder` (frame 5): forty-eight opaque quads placed by Position Shape in a cone whose two
  radii are the same constant, a cylinder: the block's program then holds, in the branch it does
  not take, a quotient of constants over zero.
- `collide` (frames 4, 12, 20, 28): forty-eight opaque quads that stay where they are, killed
  inside the sphere of a Collision Shape block whose frame is the exposed property `Ball` (half
  the size at first). The block asks the frame's axes (its columns) whether it is flat, and
  then does nothing, and whether its scale is even. At frame 6 the probe makes it flat along
  an axis turned 40 degrees (a column of the frame is zero, no row is), at frame 14 uneven
  (1.2, 0.3, 1) and moved, at frame 22 even (0.6), turned and moved to a corner.
- `over life` (frames 12, 24, 40, 55, 70): twenty a second living a second, their size from
  an exposed curve, their colour from an exposed gradient and their alpha from a curve that
  loops both ways, all sampled by the update kernel from the texture the effect bakes its
  curves and gradients into. The probe gives the effect another size curve at frame 25, one
  of a single key at frame 45 (it has no range to sample over: every particle has its value)
  and at frame 60 one whose keys start before zero and end after one (the particles' ages
  fall inside its keys, so its start and range are what is sampled with).
- `hdr` (frame 40): twenty opaque quads a second living a second, their colour a gradient
  sampled at their age: fixed between its keys, with channels brighter than a half float
  holds (131008, 65504 and 32752, in the other order after the middle). The texture keeps
  such a row scaled, with its scale; the graph divides the colour by 262016, so the image
  shows two flat colours whose three channels are all inside what its 8 bits hold. A row
  stored unscaled, a scale left out or a gradient blended between its keys would each give
  other colours.
- `loop end` (frame 20): twelve quads added to what is behind, their size and their alpha
  from two curves that loop after their last key and clamp before their first, each sampled
  at a time just below its end, where the last of the texture's samples of a curve shows.
  The first curve's keys are at zero and one: its last sample's time is its last key's,
  which on such a curve reads the first key's value. The second's keys (0.3 and 1.4) are
  such that the last sample's time, computed in single precision, falls just short of the
  last key and reads the value there.

The images are compared pixel by pixel (`image_limits.json`: no difference where both ran, one
machine with an NVIDIA driver; the quads' edges fall between pixel centres, so another GPU may
decide an edge pixel differently). Not shown by them: quads turned to face a camera (the
cameras here look straight at them), and where a draw of many quads is split into instances
(the image is the same either way; a unit test holds the split).

## Cameras

The camera controls (`camera/*`) use a camera of the probe's as the main camera (the scene's
camera gives up its tag at the start): `camera main`, perspective, rendering into a 64x48 texture
(so its size is the same in the Editor and in Unbound), and `camera ortho`, orthographic. Both
have the render pipeline's camera data, which a camera made from script gets only when asked for:
without it the pipeline keeps no history of the camera and hands an effect none of its buffers.

- `CamValues`: a spawn context with a block written as a script (`Assets/Callbacks/CameraInputs.cs`)
  whose inputs are fed by the graph's Main Camera operator; `camera/values/<update>/*` is what it
  read when the context played and in its first sixteen updates, with the probe's frame: the
  camera's transform, field of view, near and far planes, orthographic size, aspect ratio,
  whether it is orthographic, its pixel size, the size the render pipeline handed over with its
  buffers, and its lens shift. At frame 6 the camera's lens, aspect and place change; from frame
  8 no camera has the tag (the graph reads a new camera's lens and a 1920x1080 screen, and the
  module warns once: `camera/manager logs`); from frame 10 the orthographic camera is the main
  one, from frame 12 the first again.
- `CamDepth`: particles whose colour is a texel of the main camera's depth buffer, read on the GPU:
  the graph reads that buffer. `camera/needed/f<frame>` is what `VFXManager.IsCameraBufferNeeded`
  answers for the main camera, the orthographic one and the scene's, each frame: the depth
  buffer, for the main camera of the last update only.
- `camera/api/*`: the camera entry points with no camera, a destroyed one, a camera that is not
  the main one, and buffers handed over the wrong way at frame 4 (of two kinds at once, of none,
  of a kind no graph reads: logged; the inputs read the size given with the last of them at
  frame 4, until the pipeline hands over the depth again).
- `SoftAdditive` (`draw-soft*`): eight quads added to what is behind that fade over 0.6 where
  they come near what is opaque behind them (soft particles), among three boards at several
  depths, seen at frames 10 and 20 by a perspective camera with the pipeline's depth texture
  (`soft`), an orthographic one with it (`soft ortho`) and a perspective one without it (`soft no
  depth`: the texture the render pipeline leaves bound after each camera is black, so the
  particles fade away). The probe's renderer copies the depth after the opaque objects; copied
  after the transparent ones, the texture is not this camera's while the particles draw.

Known residual (`image_limits.json`): through the orthographic camera, 212 pixels inside the
particles that fade near the boards differ by 1 to 3 of 255 (the perspective images are exact);
the cause is not found.

Not shown by these: what the graph's GPU programs read in the depth and colour buffers (the
particles' colours are not recorded: in this setup the depth the pipeline hands over reads as
nothing in the Editor too, which is not explained; Unbound does not run such a graph and says
so), whether the size handed over with a value of no single kind is taken, a main camera under
a scaled parent, the scene view's camera as the main camera
(an Editor preference; Unbound has no scene view), and the work an effect does per camera.

## Output events

`OutEvents` has five spawn contexts, stepped at a fixed time step each (two steps a frame): ten
particles a second with three event attributes set by its blocks (it also spawns particles, so
that the camera sees the effect), three every 0.2 s with another attribute set, half a particle
once, another ten a second (firing in the same steps as the first) and sixty a second (in every
step). Three output event contexts named `Tick` take the first, second and fourth (one event fed
by all three: two inputs firing in one step raise it twice), one named `Burst` the second, one
named `Never` the third (a spawn count under one raises nothing) and one named `Fast` the last
(an event in each of a frame's two steps). `events/names` is what `GetOutputEventNames` lists. `events/<effect>/f<frame>/<n>` is
each event the probe's listener took in frames 1 to 20, in order: where in the frame (after the
scripts' `LateUpdate`, before the end of the frame), its name, the attributes it carries and
whether it is the same attribute set as the effect's event before. The effects: `events` in
front of the camera; `events behind`, culled (only its first update, which runs whole, raises events); `events paused`, paused from frame 5 to
frame 12; `events late`, listened to from frame 10 only (no event of the frames before is kept
for it); `events throwing`, whose first listener throws once, at the first event of frame 7
(`events/listener logs`: what is logged; the frame's next events still reach the probe's
listener); `events once`, whose only listener removes itself at the first `Tick` of frame 3,
the first of a step's three events (what the step's next events log). `events/kept attribute` reads the last attribute set handed over,
a frame later.

## Operations

`OpsValues` has a spawn context with a block written as a script (`Assets/Callbacks/OpsInputs.cs`)
whose inputs are fed by operators over exposed properties; `operations/<update>/*` is what it
read when the context played and in its first sixteen updates: matrices built from four vectors as
rows and as columns (`m00`, `m01`), rows and columns of a matrix (`q00` to `q03`), a look-at frame
(`m02`, built from axes) with its position, angles (some negative) and scale (`v00` to `v02`), the
position, angles and scale of a matrix taken as a frame (`v04` to `v06`), a colour's hue,
saturation and value (`v03`) and a colour from them (`q04`), a texture's width and height (`u00`,
`u01`), a graphics buffer's count (`u02`) and a point of a camera's viewport in the world (`v07`:
Viewport To World Point over a camera whose values are exposed properties, which takes the
inverse of the camera's projection).
At frame 3 the probe gives the effect a buffer of seven elements and another row. From frame 3,
one step a frame for twelve frames, it gives it hues past either end of the circle, saturation and
value past one and colours below zero and past one; frames mirrored, flattened, sheared, turned to
the poles, of zeros, of a zero or tiny scale and holding a NaN; and perspective and orthographic
cameras, scaled, sheared and turned, with planes and sizes at and past their limits, some of whose
projections have no inverse (a near plane of zero, equal planes, a size or an aspect of zero). The
texture is a new one of 16x4 at frame 5, a render texture at frame 9, the first again at frame 11,
destroyed while it is set at frame 12; the buffer is released while it is set at frame 9, set to
none at frame 11, another of five elements at frame 13 and one of nine released before it is set at
frame 15. The rows are written at frame 20.

## Noise

`noise/api/<function>/<call>` is the noise the graph compiler folds over constants
(`UnityEditor.VFX.VFXExpressionNoise`, listed by `noise/api/functions`): value, Perlin and cellular
noise over one, two and three coordinates with their derivatives, the curl of each over two and
three, and the Voronoi noise, each called 140 times with a fixed sequence of arguments
(`arguments = result`, floats as bits): coordinates within 8, within 300 and within 5000, then
coordinates at and around what the lattices repeat over (71 cells, 69 over three coordinates),
huge, tiny, infinite and absent; every fifth call has a frequency, a count of octaves (zero among
them), a persistence and a lacunarity of its own, negative ones among them. A NaN is written
`nan`: its sign is not compared.

`OpsNoise` has a spawn context with a block written as a script (`Assets/Callbacks/NoiseInputs.cs`)
whose inputs are fed by the package's noise operators over exposed properties;
`noise/<update>/*` is what it read when the context played and in its first sixteen updates. Per
noise (the tens digit: 0 value, 1 Perlin, 2 cellular): the noise over one coordinate and its
derivative (`f?0`, `f?1`), over two (`f?2`, `w?0`) and over three (`f?3`, `v?0`), the curl over two
and three (`w?1`, `v?1`). The Voronoi noise is not among them: the compiler refuses a graph that
reads it in a spawn context ("not valid" on the CPU), and the folded one is a NaN whatever it is
given. From frame 3, one step a frame for fourteen
frames, the probe gives the effect other coordinates and settings: cells at the lattices' period,
far coordinates, a negative frequency, a lacunarity of zero, and huge, infinite and absent
coordinates. The operators' slots clamp what they pass on (a count of octaves within one to eight,
a roughness within zero to one, a lacunarity from zero), so the steps with no octaves, a negative
count, twelve of them and a negative roughness and lacunarity record that clamp: the noise itself
reads one, one, eight, zero and zero. Persistences and lacunarities outside those ranges and
counts below one are measured by the `noise/api` rows only (their counts are -1 to 5). The rows
are written at frame 20.

## Mesh operations

`OpsMesh` has a spawn context with a block written as a script (`Assets/Callbacks/MeshInputs.cs`)
whose inputs are fed by operators over exposed properties; `mesh operations/<update>/*` is what
it read when the context played and in its first sixteen updates: a mesh's vertex, index and
triangle counts and one of its indices (`u00` to `u03`); a vertex's position, normal and tangent
(`v00` to `v02`), colour and first two texture coordinates (`q00` to `q02`) with the index
clamped, the vertex's position with the index wrapped and mirrored (`v03`, `v04`), and a point
and its colour on a triangle at barycentric coordinates (`v05`, `q03`); a volume's width, height
and depth and the counts of an array of textures and of an array of cubemaps (`u04` to `u08`).
`MeshMixed` has five vertices in two streams (positions and normals as floats, tangents as signed
16-bit norms; colours as bytes, first texture coordinates as half floats, second ones as unsigned
16-bit norms) and three triangles of 16-bit indices, `MeshLocked` the same saved without
read/write access (as a model's meshes are imported by default); `MeshWide` has four vertices in
one stream of floats without normals or tangents, and 32-bit indices in two submeshes. From frame 3, one step a
frame: the mesh is `MeshMixed`, a volume of 4x2x3, an array of five 2x2 textures and an array of
two cubemaps are set; the vertex, triangle and index read fall past the mesh's end; the mesh is
`MeshWide` and the volume a volume render texture of 8x4x6; the mesh is a copy whose data was
given up (`UploadMeshData(true)`); none can be set (`set none`, `set no texture`: both throw), so
a mesh, a volume and the arrays are destroyed while they are set; a mesh made by script, whose
vertices are then replaced and which then gains colours; `MeshLocked`. The rows are written at
frame 20, with each mesh's `vertexBufferTarget` and `indexBufferTarget` before and after
(`<mesh>/before`, `after`), also of `ShapeA` (the scene's effects' default), of a copy only held
by an effect whose graph reads nothing of it (`held`: Exposed's `Shape`) and of a copy only read
by its vertices (`vertices only`, the `mesh stride` drawing): a readable mesh's vertex buffer
gains `Structured` once it is an effect's value, its index buffer once an effect's programs read
it; a mesh without read/write access gains neither. Such a mesh (`UploadMeshData(true)` or
`MeshLocked`) keeps its counts, has no layout (its index reads 0xffffffff) and samples as zeros.
The package's documentation (17.0.4: Operator-MeshVertexCount, Operator-MeshIndexCount,
Operator-MeshTriangleCount, Operator-SampleMeshIndex, Operator-SampleMesh) says the counts and an
index are 0 for a mesh that is not readable and that Sample Mesh supports Float32 channels only
(and colours); the Editor answers as recorded here.

Drawn, at frame 5: `mesh` (`DrawMesh` over `MeshMixed`) puts a particle on each vertex in turn
(Set Position Mesh, the vertex given by the particle's id), coloured from a graphics buffer of
vectors (Sample Graphics Buffer); `mesh stride` gives it a buffer of three floats an element,
which the program does not read (black particles); `mesh wide` is `DrawMesh` over `MeshWide`;
`mesh surface` and `mesh surface wide` (`DrawMeshSurface`) put one on each triangle, at
barycentric coordinates, coloured by the mesh's colours sampled there; `mesh locked` is
`DrawMesh` over `MeshLocked`, whose buffers the programs cannot read: every vertex reads as zeros
(the particles all at the origin). The programs read the meshes' vertex and index buffers as the
effects' values hand them over.

## Event attributes read in a spawn context

`OpsEvent`'s spawn context has, in order: a block written as a script (`EventReadsFirst`,
`Assets/Callbacks/EventReads*.cs`) whose inputs are its event attributes read with Get Attribute
(Current): the lifetime (`f00`), the spawn count (`f01`), the colour (`v00`), the seed (`u00`) and
alive (`b00`); blocks
setting the lifetime (the exposed `L`), the colour (from the lifetime as read there), the seed
(`S`) and alive (`A`); and a second script block (`EventReadsSecond`) with the same inputs.
`event reads/<block>-<play or update><n>/*` is what each read when the context played and in its
first twenty updates, counted across plays. At frame 3 `L` changes; at frame 5 the effect plays
again with attributes of its own (`SendEvent("OnPlay", attributes)`); at frame 6 `S` and `A`
change; at frame 8 it stops and at frame 10 plays again, both without attributes. A block reads
what the blocks before it left, in this update or the last; an event's attributes replace the
spawn context's (one sent without them gives the asset's initial values); when the context
plays, the inputs hold what was last evaluated (nothing yet at the first play: every bit set).
A second spawn context of the graph, started by the event `Loop`, takes its loop duration from its
own lifetime read with Get Attribute (Current); a block sets that lifetime to the exposed `L2` (0.25,
0.5 from frame 7) and a script block (`LoopReads`) writes down the loop's duration, index and time
and the lifetime held, when the context plays and in its first twenty updates
(`event reads/loop-*`). `Loop` is sent at frame 4 with a lifetime of 0.1 (loops of three updates)
and at frame 12 without attributes (the asset's lifetime, 1).
The two contexts' reads of the lifetime are one expression of the graph: what the first context's
blocks hold when it plays again is what either context evaluated last (`f00` at plays 2 and 3: the
second context's lifetime).

## A built asset

The authoring step builds an AssetBundle with the asset of a graph of its own
(`Assets/BundleGraphs/Bundled.vfx`: a rate that is exposed, an output textured with one of the
probe's images, since an output left at its default would have the package's texture built in)
into `Assets/StreamingAssets/VfxProbe/vfxgraphs`: the compiled graph, without the graph file.
The probe's own script is compiled for the Editor only, since the build compiles the project's
scripts for a player. The build also rewrites the prefiltering fields of the project's pipeline
asset (`Assets/Settings/ProbeURP.asset`), which is why the authored set holds a copy of it. The probe loads it (`bundle/*`): the bundle and the asset's name in it,
the asset (an object of its own, not the project's), its events and exposed properties, and an
effect that uses it (its systems, and at frames 3 and 20 whether it is culled and awake).

Unbound reads the built asset's compiled graph from the bundle. Its programs are built for the
Editor's graphics device, so Unbound runs it with the programs of the project's graph at the
asset's path in its bundle when that graph compiles to the same graph (here `Bundled.vfx`
itself); a built asset without such a graph in the project does not run and says so (a unit test
holds that case, not this probe). The rows here show the asset's graph and that the effect
runs (culled, awake), not its particles or an image of them.

## Not covered yet

Outputs that are sorted or culled per camera (alpha blended ones are, by default: the effects
of the simulation controls), drawn through an indirect buffer, and triangles and octagons are
controlled by `tests/projects/vfxoutputs`; lit outputs, outputs that draw meshes and their
shadows by `tests/projects/vfxlit`; strips by `tests/projects/vfxstrips`; GPU events by
`tests/projects/vfxevents`; Shader Graph outputs and the 2D renderer by `tests/projects/vfxlit`.
Motion vectors, bounds computed on the GPU. Expression operations without a control: a
texture's format (no operator makes it), and the ones the runtime does not do yet:
a skinned mesh renderer's mesh, vertices and root bone. Not measured of noise: the sign of a NaN, and a
compiled graph whose noise reads a count of octaves, a persistence or a lacunarity outside what the
operators' slots keep (Unbound computes for them what it computes for the folded noise), and a
count of more than eight octaves anywhere (more than five for the folded noise): Unbound sums as
many octaves as it is given. Not measured of meshes: no mesh, and a mesh without the
channel a program reads, read by a program (Unbound gives them zeros, as for a mesh without
read/write access); a copy whose data was given up read by a program (taken to read as
`MeshLocked`); a mesh without read/write access whose buffers a script gave a target for compute
access (Unbound binds them only with the `Structured` target); one whose layout changes while
an effect reads it on the GPU, sampling with the edge placement or low-distortion surface
coordinates (both made of operations measured here), signed 8-bit and integer channels, float
colours outside zero to one, and an index read in the other index format or past the buffer's end
(the package asks neither). Not measured of textures and buffers: a released buffer's stride, and
the blocks and outputs that read a texture's depth (Position and Collision SDF, flipbooks from
arrays of textures). Of baked curves and gradients, not measured: a curve whose values are larger than a
half float holds (its scale is held by a unit test, as a gradient's is by `hdr`), a curve
that ping-pongs, one that loops before its first key and clamps after its last, one that
loops after its last key with a range whose product by 127 and quotient by 127 round below
it in single precision (keys at 0 and 0.1552: whether the last sample then reaches the last
key), one that loops both ways sampled outside zero to one, a curve or a gradient that is null, a gradient
of one key, one blended perceptually and one whose alpha is over one. A curve the graph
samples itself (`c00`) is not measured where it loops before its keys or ping-pongs after
them, with weighted or stepped keys, or with keys closer than a ten-thousandth; the curves baked into the texture are rounded the same way, which
the images show only through the texture's half floats. A curve or gradient baked among the expressions of a spawn
event is not done (Unbound says so and does not run the graph). The update modes that ignore the time scale are not
compared: the Editor's unscaled delta time under a capture frame rate is the wall clock's.
One attribute name with different
types in two graphs (a custom attribute) is not measured for `CopyValuesFrom`.
Everything was measured in the Linux Editor on the OpenGL Core device (NVIDIA driver); the
frames at which a count read back from the GPU arrives depend on the device (the rows avoid them).

## Running

    python3 tests/projects/vfx/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfx/run.py record --label b
    python3 tests/projects/vfx/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfx/run.py gate                 # the engine on the authored project

## Scene captures

`scene_capture.py` compares effects in real content, which no probe can carry: a scene of another
project seen by fixed cameras, captured once by the Unity Editor and once by Unbound, frame by
frame. It is debug tooling, not a gate: nothing of the project is kept in Git. Each run keeps, in
the run cache, a copy of the project (`cp --reflink=auto`: a reflink copy where the file system
has them, else a full one, the Editor's Library included so that its run need not import the
project again; several gigabytes for a large project, removed only with the run), the frames
and the logs.

    scene_capture.py editor PROJECT Assets/Scenes/S.unity --label ed --target NAME \
        --target-near X,Y,Z --position 0,4,18 --position=-12,10,-12 --frames 30,60,90 --seed 1
    scene_capture.py engine PROJECT Assets/Scenes/S.unity --label ub (the same settings)
    scene_capture.py compare ed ub
    scene_capture.py selftest

The same script runs in both (`SceneCapture/UnboundSceneCapture.cs`, copied into the copy's
Assets/, and `SceneCapture/Editor/UnboundSceneCaptureRunner.cs`, which opens the scene and plays it
once the Editor has settled). For each `--position` a copy of the scene's main camera (its camera
and render pipeline components only, without scripts or children; made under an inactive object,
so none of its scripts runs) renders into a texture: from that place looking at the active object
`--target` names (of several, the one nearest `--target-near`; values.jsonl lists them all), or
from that place with its `--rotation`. Time advances by a fixed step (`--step`,
Time.captureDeltaTime); the frames named are counted from the first frame the scene plays.
`--seed` gives every effect the scene has when it starts playing that seed and starts it again,
so that the two runs draw the same particles; effects made later, and without it all effects
that reset their seed on play, differ from run to run in the Editor as well. A run that cannot
go on (no camera place, a failed shot, scripts that stop compiling while the Editor settles)
ends at once without values.jsonl; scripts that do not compile when the Editor starts never
load the runner, and that run waits out its `--timeout`. `compare` names any setting in which the two captures differ and any frame only
one has.

What the comparison cannot tell apart from an effect's difference: anything else in the frame
that differs (lighting, other renderers, post-processing), work that is timed by the frame rate
rather than the fixed step, and how the project enters play mode in the Editor (with its Enter
Play Mode Options, for example without reloading the scene, the Editor plays the scene as it
was opened).

`selftest` runs the tool on a copy of this probe's project (without its probe script) with
`SceneCapture/SelfTest/`'s fixtures, in both: a main camera whose script keeps one instance of
itself (its copies must keep rendering: two shots of a moving cube differ) and a shot that fails
(the run must end at once, incomplete).
