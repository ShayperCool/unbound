# vfxstrips probe

Particle strips of Visual Effect Graph (com.unity.visualeffectgraph 17.0.4 under URP 17, the
Editor's built-in packages, unmodified): systems whose particles are kept in strips and drawn
joined from one to the next, spawned from the CPU or by GPU events; and when the alive counts
of effects that share a graph are read. Small graphs of the probe's own, each drawn in front of
a camera of its own, recorded by the same script in the Unity Editor and in Unbound. The
outputs are unlit and, but for one, opaque, so the images show the simulation and the strips'
shape alone;
the import, the API and the simulation of plain systems are `tests/projects/vfx`, outputs
`tests/projects/vfxoutputs` and `tests/projects/vfxlit`, GPU events `tests/projects/vfxevents`.

The Editor records in a window (not in batch mode), after letting five editor updates pass
(`Assets/Editor/ProbeRunner.cs`): see `tests/projects/vfx/README.md`.

## Graphs

`Assets/Editor/Authoring/StripAuthoring.cs` builds them in the Editor through the package's own
models (its assembly carries the name of a friend assembly of the package). The strip outputs
are `Output ParticleStrip Quad` contexts with an `Orient: Face Camera Position` block.

- `StripSingle`: one strip of sixteen particles; twenty a second at random places, each
  living three fifths of a second, so the strip's first particle moves on round its room.
  `StripAlone`: the same graph as an asset with instancing turned off.
- `StripKeep`: one strip of twelve whose particles never die: it fills and takes no more.
  `StripKeepTwo`: two strips of six whose particles never die, all spawned into the first.
- `StripAlpha`: a strip like `StripSingle`'s, wider, half transparent and alpha blended, each
  particle of a colour of its own and at a depth of its own (two units either side of the
  effect): such an output sorts its particles by default and draws the strip through a sorted
  indirect buffer, and where the strip crosses itself the colours show which segment was drawn
  over which.
- `StripTrails`: three particles that move, each spawning twenty a second by a GPU event into
  a system of four strips of sixteen: a particle goes into the strip numbered as the particle
  that spawned it. `StripTrailsKeep`: the same with strips of twelve whose particles never die.
- `StripTrailsAlive`: heads that die (six a second into room for four, living two fifths of a
  second), each spawning twenty particles a second by a GPU event into a system of four strips of
  twelve whose particles live half a second. A system whose particles die and spawn strips gets
  the `stripAlive` attribute: a dead head is kept, not drawn, until the strip it spawned is empty,
  and only then makes room for a new head. `StripTrailsAliveKeep`: the same with strip particles
  that never die, so a dead head is kept for good.
- `StripListed`: two strips of twelve whose particles live three fifths of a second, drawn by an
  opaque strip output told to draw through an indirect buffer (`Indirect Draw`, sort off: the
  list the system's update makes), and each particle as a small red quad from a list culled in
  a compute pass (`Compute Culling`). `StripCulled`: the same strips culled by each camera's
  frustum (`Frustum Culling`, sort off), so each camera draws a list of its own (no segment
  leaves the view here, so none is culled away).
- `StripLit`: one strip of sixteen lit by the pipeline (`Output ParticleStrip URP Lit Quad`,
  opaque, no base colour map, smoothness 0.4), its particles spread in depth too.
  `StripLitBent`: the same with its normals bent across the strip (`Normal Bending`, the
  output's default factor).
- `StripIndex`: four strips of eight; the strip a particle goes into is the initialize
  context's `stripIndex`, an exposed property `Strip` of the effect.
- `StripBoth`: a strip drawn twice: joined, and each of its particles as a quad of its own.
- `StripMeshes`, `StripMeshesLit`: a strip drawn joined, and each of its particles (each turned
  its own way) as the package's default mesh: unlit, and lit by the render pipeline.
- `StripMeshesCulled`: two strips of twelve at places spread in depth, drawn joined, and their
  particles as half transparent meshes of colours of their own (sorted, by default) from a list
  culled in a compute pass, through an indirect buffer: overlapping meshes show the order.
- `StripTaper`: each particle's size multiplied by its place in the strip
  (`particleIndexInStrip`), in the output.
- `StripImage`, `StripImageSegments`, `StripImageSwapped`: strips of eight that draw an image
  of four quarters: stretched along the strip, once for each segment (`Repeat Per Segment`),
  and stretched with `Swap UV`.
- `Dying`: no strips: quads that live three fifths of a second, of a graph that is instanced.
  `Assets/Objects/DyingApart.prefab` holds an effect of it whose component does not allow
  instancing. `Waking`: the same graph again.

## What is recorded

`Assets/Scripts/StripProbe.cs` gives each control an effect of a graph (seed 11) and an
orthographic camera rendering into a 64x64 texture. At the frames named it saves the image
`draw-<control>-f<frame>.png`; at a fixed list of frames a row `sim/<control>/f<frame>`: the
effect's alive count, whether it is culled and awake, and each particle system's alive count,
capacity and whether it sleeps. `systems/<control>` lists the systems' names.

- One control per strip graph: `single`, `keep`, `keep two`, `alpha`, `trails`,
  `trails keep`, `both`, `taper`, `image`, `image segments`, `image swapped`.
- `index`: `StripIndex` with `Strip` set to 2 at frame 20, to 7 (a strip the system has not)
  at frame 40 and to 1 at frame 50: a new strip starts at each change, and nothing is spawned
  from frame 40 to frame 50.
- `reinit`, `trails reinit`: `StripSingle` and `StripTrails` with `Reinit()` called at frame 30.
- `toggled`: `StripSingle` whose object is inactive from frame 30 to frame 34.
- `stopped`: `StripSingle` with `Stop()` at frame 30 and `Play()` at frame 55: the strip
  empties between and starts again.
- `paused`: `StripTrails` paused from frame 20 to frame 36.
- `alone`, `alone reinit`: two effects of `StripAlone`; `dying`, `dying reinit`: two of
  `Dying`; `dying apart`: the prefab's effect. Those named `reinit` are reinitialized at
  frame 30.
- `wakes`, `wakes first`, `wakes second`, `wakes third`: four effects of `Waking`, the second
  reinitialized at frame 30, the third at frame 31 and the fourth at frame 32.
- `listed`, `culled`: `StripListed` and `StripCulled`.
- `lit`, `lit bent`: `StripLit` and `StripLitBent`, each under a point light of its own; a
  directional light without shadows lights the scene (the other stages are unlit).
- `meshes`, `meshes lit`, `meshes culled`: `StripMeshes`, `StripMeshesLit` and
  `StripMeshesCulled`, each under such a point light too (frames 10, 20, 40 and 66).
- `trails alive`, `trails alive keep`: `StripTrailsAlive` and `StripTrailsAliveKeep`, images
  every four frames to frame 32, then at 40, 50 and 66. The heads' alive count includes the dead
  heads kept for their strips (room for four, full at frame 70 in both).

What the rows show of alive counts. A strip system whose particles die answers the count its
kernels keep, read back from the GPU like the count of any system that kills particles; one
whose particles never die answers what was spawned (`keep`), also what a full strip dropped
(`keep two` answers 12 with the six particles its first strip holds). The counts of the effects of one
instanced graph are read together: `single`, `reinit`, `toggled` and `stopped` are four effects
of `StripSingle`, and all of them that are awake get a new count two frames after one of them
wakes (frame 33, after `reinit` started over at frame 30 and woke at frame 31; again by frame 40,
after `toggled` came back), and none sixty frames after their first: `single` itself does nothing
and its count changes at frame 33. `dying` with `dying reinit` shows the same of a graph without
strips. An effect that is not batched with the others keeps a schedule of its own: `alone` (its
asset turns instancing off) and `dying apart` (its component does not allow it) get their second
count sixty frames after the first, at frame 64, whatever `alone reinit` and `dying reinit` do.
A wake puts the batch's question off to the next frame, also one that was due: `wakes first`
wakes at frame 31, `wakes second` at frame 32 and `wakes third` at frame 33, and all four effects
of `Waking` get their count at frame 35, two frames later than those of `Dying` (`wakes` at 34
still shows its first).
`both`, `taper`, `alpha` and the image controls are each the only effect of their graph.

Row 1 is not recorded (the first frame's end of frame: `tests/projects/vfx/README.md`).
`capture/graphs imported while playing` must be empty.

## Image limits

Every image is the Editor's pixel for pixel on the machine both ran on (an NVIDIA driver, the
Linux Editor on OpenGL Core). In `trails alive keep` from frame 8 to frame 28, while its heads
live, the Editor's pixels where a strip's newest end meets its head changed with the scene:
before the `meshes*` stages joined it, every capture showed the head there (a few pixels each,
up to 209 levels from Unbound's, which shows the strip); since, four captures show the strip, as
Unbound does. What decides which of the two the Editor shows there is not measured.

## Not covered

- Drawn but not measured: alpha blended strips with their sort off through a list (where
  overlapping segments show the order), a sorted quad output of a strip system's particles,
  `Compute Culling` on a strip output itself, several cameras.
- Lit strips blended over what is behind them or drawn from lists, through the deferred
  renderer or Forward+, with maps, the specular workflow, the six-way smoke material, both
  sides drawn, receiving the main light's shadows or not; Shader Graph strip outputs (drawn,
  not measured).
- Strips lit by 2D lights, line strips
  (`Output ParticleStrip Line`), and any output but quads and meshes of a strip system's
  particles: not drawn (the effect says so). Meshes of a strip system's particles from a
  frustum-culled list or from the list the system's update makes, several meshes of an output,
  shadows cast by them, a Shader Graph mesh output: drawn, not measured (meshes chosen by their
  size on screen are not drawn, as for any system).
- Additive strips; a sorted strip under a perspective camera or with another sort criterion
  than the default; two strips of one sorted output that cross.
- A batch in which an effect wakes in every frame for longer than three frames (as measured,
  its counts would not be asked while that lasts).
- `stripAlive` with several strip systems spawned from one system (the graph compiler reads
  the first), with sleeping or culled effects, a system with more room than its strip system
  has strips (the update kernel reads the strip of each dying particle's own index), and what
  decides whether a head or its strip shows where they meet in `trails alive keep`.
- The strip tiling mode `Custom`; `Orient` modes other than `Face Camera Position` on a
  strip; a perspective camera.
- `stripIndex` computed for each particle (linked to an attribute); the attributes
  `particleCountInStrip`, `spawnIndexInStrip` and `stripIndex` read by a block; `Ratio Over
  Strip`; `Increment Strip Index On Start`.
- Strips of a system that sleeps or is culled; several steps in a frame; strips of more than
  a few dozen particles, and more strips than one dispatch's thread groups reach.
- The alive counts of more effects of one graph than a batch holds (the asset's batch
  capacity), of effects made after their batch emptied or while it runs (every effect of the
  probe is made in its first frame), of an effect that is culled or asleep
  when its batch's counts are asked, and with instancing turned off in the Editor's preferences
  (`Instancing Enabled`).
- Other devices than OpenGL Core in the Editor.

## Running

    python3 tests/projects/vfxstrips/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfxstrips/run.py record --label b
    python3 tests/projects/vfxstrips/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfxstrips/run.py gate                 # the engine on the authored project
