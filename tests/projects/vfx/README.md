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
- `DrawOpaque`, `DrawAdditive`, `DrawMany`, `DrawTextured`, `DrawOverLife`, `DrawHdr`
  (`Assets/DrawGraphs`): the drawing controls' graphs (below).

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

Known residuals (`residuals.json`): Unbound loads the built asset as a live object with its
name, but does not read its compiled graph yet, so its events and exposed properties, the
effect's systems and the effect's state differ; the load says so.

## Not covered yet

Outputs that are sorted or culled per camera (alpha blended ones are, by default: the effects
of the simulation controls), drawn through an indirect buffer, lit outputs, soft particles,
motion vectors, Shader Graph outputs, shadows, strips, meshes and primitives other than quads
(Unbound draws none of these and says so); output events, GPU events, bounds computed on the GPU and the `VFXManager`
camera hooks. Expression operations without a control: the inverse of a general matrix and an
event attribute read in a spawn context (no operator of the package gives either here), and
the ones the runtime does not do yet (matrices built from or split into rows, columns and axes,
angles and scales taken from a matrix, colour spaces, meshes, the main camera, noise, texture
and buffer sizes). Of baked curves and gradients, not measured: a curve whose values are larger than a
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
