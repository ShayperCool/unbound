# vfxevents probe

GPU events of Visual Effect Graph (com.unity.visualeffectgraph 17.0.4 under URP 17, the Editor's
built-in packages, unmodified): systems whose particles are spawned by the kernels of another
system (`Trigger Event` blocks and `GPU Event` contexts). Small graphs of the probe's own, each
drawn in front of a camera of its own, recorded by the same script in the Unity Editor and in
Unbound. The outputs are opaque unlit quads that draw themselves, so the images show the
simulation alone; the import, the API and the simulation of systems spawned from the CPU are
`tests/projects/vfx`, outputs `tests/projects/vfxoutputs` and `tests/projects/vfxlit`.

The Editor records in a window (not in batch mode), after letting five editor updates pass
(`Assets/Editor/ProbeRunner.cs`): see `tests/projects/vfx/README.md`.

## Graphs

`Assets/Editor/Authoring/EventAuthoring.cs` builds them in the Editor through the package's own
models (its assembly carries the name of a friend assembly of the package). In each, the
spawned systems take their particles' position from the particle that spawned them.

- `EventOnDie`: six particles a second rising for half a second; each that dies spawns four
  sparks with velocities drawn at random.
- `EventAlways`: four particles that each spawn one a step, into a system of 32 whose
  particles live three tenths of a second: more are asked for than it has room for.
  `EventAlwaysRoom`: two particles that each spawn one a step into a system with room to spare,
  whose particles fly off with velocities drawn at random.
- `EventRate`: three particles that each spawn twenty a second (`Over Time`).
  `EventRateFast`: a hundred and fifty a second each, with the block's `Clamp To One` off:
  several events of one particle in a step.
- `EventDistance`: two particles that each spawn five for every unit they travel
  (`Over Distance`).
- `EventAtBirth`: the trigger in the initialize context: each particle spawns three as it is
  born, which take its colour too.
- `EventTwo`: two triggers in one system, each into a system of its own: on death and at every
  step.
- `EventChain`: three systems in a chain: what an event spawned spawns in turn when it dies.
- `EventKeep`, `EventKeepRoom`: what the events spawn is given no lifetime and never dies: a
  system of 24 that fills up and one of 96 that does not. (The graph compiler gives a system
  spawned by GPU events particles that can die and a list of the dead all the same.)

## What is recorded

`Assets/Scripts/EventProbe.cs` gives each control an effect of a graph (seed 11) and an
orthographic camera rendering into a 64x64 texture. At the frames named it saves the image
`draw-<control>-f<frame>.png`; at a fixed list of frames (the first ones, around what the probe
does at frames 30 to 36 and 45 to 50, and around the alive counts' second answer, frame 63) a
row `sim/<control>/f<frame>`: the effect's
alive count, whether it is culled and awake, and each particle system's alive count, capacity
and whether it sleeps. `systems/<control>` lists the systems' names.

- One control per graph, named as the graph (`on die`, `always`, `rate`, `distance`,
  `at birth`, `two`, `chain`, `keep`, `keep room`, and `unclamped` for `EventRateFast`).
- `reinit`: `EventOnDie` with `Reinit()` called at frame 30. Its sparks after that (frame 66)
  are not the ones a new effect draws: the numbers the spawned particles are given, which seed
  what they draw at random, go on counting through the reinitialisation.
- `toggled`: `EventOnDie` whose object is inactive from frame 30 to frame 34: the effect starts
  over when it is active again, and its sparks after that (frame 66) are numbered on as after
  a reinitialisation.
- `keep reinit`, `keep toggled`: `EventKeep` reinitialized at frame 45, and inactive from
  frame 45 to 48, once its spawned system has filled: the system empties and fills again.
- `stopped`: `EventRate` with `Stop()` called at frame 30: the spawner stops, the particles
  that spawn go on living and spawning.
- `paused`, `paused room`: `EventAlways` and `EventAlwaysRoom` paused from frame 20 to frame
  36. A paused effect's particles go on triggering at every frame (these graphs' update
  contexts do not skip a step of no time), and what they spawn neither ages nor moves: it lies
  on the particle that spawned it, so the image of frame 19 and those of the paused frames (21,
  30, 35) are the same here, and what was spawned while paused shows once the pause ends: frames 37 and 40 of
  `paused room` hold about twice and more the spawned particles of a frame without a pause, flying off
  from the two places the spawning particles were paused at, and are gone by frame 50. (The
  spawned system of `paused` is full when its pause begins: it shows only that nothing ages.)
  What a run of the same graph without a pause shows at those frames is not recorded.

What the rows show of systems spawned by GPU events: such a system is awake while the system
that spawns into it is; its alive count is read back from the GPU like that of any system that
kills particles (-1 in the effect's sum before the first answer, then an answer every sixty
frames), also when no block gives its particles a lifetime: its room less its dead ones (`keep`:
24 at the second answer; `keep room`: the 40 spawned by then).

Row 1 is not recorded (the first frame's end of frame: `tests/projects/vfx/README.md`).
`capture/graphs imported while playing` must be empty.

## Image limits

Every image is the Editor's pixel for pixel on the machine both ran on (an NVIDIA driver, the
Linux Editor on OpenGL Core).

## Not covered

- Systems spawned by GPU events that are strips: `tests/projects/vfxstrips`.
- `On Collide` triggers; a spawned system that reads the count of its event (`spawnCount`);
  events of a system that spawns from the CPU as well.
- An effect whose spawning system sleeps or is culled while the spawned one still has
  particles; several effects of one graph; another play rate; a time scale of zero; an update
  context set to skip steps of no time (`Skip Zero Delta Time`), under a pause.
- Several steps in a frame (the asset's exact fixed time step, a prewarm, `Simulate`).
- Events into a system whose outputs draw through an indirect buffer, are lit or are meshes.
- More events in a step than one dispatch's thread groups reach, and systems of more than a
  few dozen particles.
- Other devices than OpenGL Core in the Editor.

## Running

    python3 tests/projects/vfxevents/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfxevents/run.py record --label b
    python3 tests/projects/vfxevents/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfxevents/run.py gate                 # the engine on the authored project
