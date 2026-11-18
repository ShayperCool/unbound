# vfxdirect probe

Events of Visual Effect Graph wired straight to an Initialize context (com.unity.visualeffectgraph
17.0.4 under URP 17, the Editor's built-in packages, unmodified): an Event context whose output
goes to an Initialize context without a spawn context between. Such an event is the system's
spawn event: it spawns the count its attributes ask for, and the particles take what they
inherit from those attributes. Small graphs of the probe's own, each drawn in front of a camera
of its own, recorded by the same script in the Unity Editor and in Unbound. Events that start
and stop spawn contexts, event attributes and the rest of the simulation are
`tests/projects/vfx`.

The Editor records in a window (not in batch mode), after letting five editor updates pass
(`Assets/Editor/ProbeRunner.cs`): see `tests/projects/vfx/README.md`.

## Graphs

`Assets/Editor/Authoring/DirectAuthoring.cs` builds them in the Editor through the package's own
models (its assembly carries the name of a friend assembly of the package). Every system draws
opaque unlit quads that take their position and colour from the event that spawned them and
fly off with a velocity drawn at random.

- `DirectHit`: an event `Hit` wired to the Initialize context of a system of 32; the particles
  live half a second.
- `DirectKeep`: the same for a system of 8 whose particles never die.
- `DirectOnPlay`: the effect's start event (`OnPlay`) wired to the Initialize context.
- `DirectAndSpawner`: an Initialize context fed by a spawn context (five a second) and by the
  event `Hit` as well.
- `DirectTwo`: one event wired to the Initialize contexts of two systems.

## What is recorded

`Assets/Scripts/DirectProbe.cs` gives each control an effect of a graph (seed 11) and an
orthographic camera rendering into a 64x64 texture. At the frames named it saves the image
`draw-<control>-f<frame>.png`; at a fixed list of frames a row `sim/<control>/f<frame>`: the
effect's alive count, whether it is culled and awake, and each particle system's alive count,
capacity and whether it sleeps. `systems/<control>` lists the systems' names. The probe sends
its events from `Update`; each asks for a count of particles at a place and of a colour
(`spawnCount`, `position`, `color` of the event's attributes).

- `hit`: `SendEvent("Hit")` without attributes at frame 5 (one particle at the origin: the
  asset's own attributes); four at frame 10; two events in frame 15, of two and of three, each
  with a place and a colour of its own; a count of 2.7 at frame 20 (two particles); a hundred
  at frame 25 (as many as the system has room for); a count of 0.5 at frame 32 (none) and of
  -1 at frame 33 (all the room). What each of these spawned is in the images (frames 6, 11,
  16, 21, 26 and 34): the alive count of a system that kills particles is read back from the
  GPU seldom, and says little here.
- `keep`: three at frames 5 and 10, three at frame 15 of which two fit, three at frame 20 of
  which none does. The system's count is what was spawned, in the frame of the event.
- `on play`: one particle when the effect starts; `SendEvent("OnPlay")` at frame 10 (one
  more); `Play` with attributes asking for three at frame 15; `Reinit()` at frame 20 (one
  again); then, in this system whose particles never die, a count of 2.7 at frame 25 (two),
  of 0.5 at frame 28 (none) and of -1 at frame 30 (the seven left of the room). Its count is
  what was spawned, so the rows show each of these.
- `both`: four by the event at frame 5 among the spawn context's, and three more at frame 12,
  a frame the spawn context spawns in as well (it spawns one every six frames).
- `two`: three at frame 5, in both systems.
- `paused`: `DirectHit` paused from frame 8 to frame 16, sent four at frame 10: they are
  spawned in that frame, and stand still until the pause ends.

Row 1 is not recorded (the first frame's end of frame: `tests/projects/vfx/README.md`).
`capture/graphs imported while playing` must be empty.

## Image limits

Every image is the Editor's pixel for pixel on the machine both ran on (an NVIDIA driver, the
Linux Editor on OpenGL Core).

## Not covered

- An event sent to an effect that is culled, inactive or not started; an event sent from
  another place of the frame than `Update`; several steps in a frame.
- More events in a frame than a few; attributes the particles inherit other than position and
  colour; an event wired to an Initialize context through a subgraph.
- A system of strips or with automatic bounds fed by such an event.
- Other devices than OpenGL Core in the Editor.

## Running

    python3 tests/projects/vfxdirect/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfxdirect/run.py record --label b
    python3 tests/projects/vfxdirect/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfxdirect/run.py gate                 # the engine on the authored project
