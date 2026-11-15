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
  the simulation controls' graphs (below), kept in `Assets/SimGraphs` apart from the graphs the
  import rows list.

`Exposed`'s texture and mesh properties default to `Assets/Objects/ImageA.png` and
`ShapeA.asset`, which the authoring script also makes (with `ImageB` and `ShapeB`).

`Assets/Scenes/Probe.unity` holds effects saved by the Editor: `Plain` (Basic), `Overridden`
(Exposed with overridden numbers, vectors, a curve, a gradient, a texture and a mesh, a start
seed, an initial event), `Events`, `NoAsset`.

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

Observed and kept: effects made in the first frame run their first step one frame later than
one made in frame 20 (no camera has culled before the first frame's update: effects update only
after a camera culled, or when one simulates while culled); an effect counts as culled until
its first update, and is not culled in that update; `aliveParticleCount` is -1 for a system
whose particles die until its first count arrives.

## Not covered yet

Drawing (images), output events, GPU events, strips, bounds computed on the GPU and the `VFXManager`
camera hooks. The expression evaluator is exercised through what the controls' graphs
evaluate (constants, the step's time, values drawn per spawn, the effect's matrices); a control
per operation (a custom spawner block reading its inputs) is to come, as are the update modes
that ignore the time scale: the Editor's unscaled delta time under a capture frame rate is the
wall clock's, so they cannot be compared frame by frame here. One attribute name with different
types in two graphs (a custom attribute) is not measured for `CopyValuesFrom`.
Everything was measured in the Linux Editor on the OpenGL Core device (NVIDIA driver); the
frames at which a count read back from the GPU arrives depend on the device (the rows avoid them).

## Running

    python3 tests/projects/vfx/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfx/run.py record --label b
    python3 tests/projects/vfx/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfx/run.py gate                 # the engine on the authored project
