# vfxbounds probe

Bounds of Visual Effect Graph systems computed on the GPU (com.unity.visualeffectgraph 17.0.4
under URP 17, the Editor's built-in packages, unmodified): an Initialize context whose
`Bounds Mode` is `Automatic` has its system's bounds computed from the particles while the
effect runs, and the effect's renderer is culled by them. Small graphs of the probe's own, each
drawn in front of a camera of its own, recorded by the same script in the Unity Editor and in
Unbound. Bounds the graph gives (manual and recorded ones), the culling flags and the rest of
the simulation are `tests/projects/vfx`.

The Editor records in a window (not in batch mode), after letting five editor updates pass
(`Assets/Editor/ProbeRunner.cs`): see `tests/projects/vfx/README.md`.

## Graphs

`Assets/Editor/Authoring/BoundsAuthoring.cs` builds them in the Editor through the package's own
models (its assembly carries the name of a friend assembly of the package). Every system draws
opaque unlit quads.

- `BoundsAuto`, `BoundsReinit`, `BoundsMoved`, `BoundsShared`, `BoundsTurned`: one graph five
  times: six particles of different sizes and turns that fly apart and never die; automatic
  bounds. `BoundsMany`: the same with a hundred and fifty particles, more than the threads of
  one group of the kernel.
- `BoundsPadded`: the same with a bounds padding, an exposed property `Padding` of the effect.
- `BoundsWorld`, `BoundsWorldTurned`: the same in world space.
- `BoundsDying`: ten particles a second that move right and live half a second;
  `BoundsDyingPadded`: the same with a padding.
- `BoundsAway`: four particles that fly out of view to the right; `ManualAway`: the same with
  bounds the graph gives (`Manual`): a box whose numbers are not round.

The graph compiler sets the culling flags of an asset with automatic bounds to none (such an
effect goes on simulating and recomputing its bounds when culled); `ManualAway` keeps the
default flags.

## What is recorded

`Assets/Scripts/BoundsProbe.cs` gives each control an effect of a graph (seed 11) and an
orthographic camera three units high rendering into a 64x64 texture. At the frames named it
saves the image `draw-<control>-f<frame>.png`; at a fixed list of frames a row
`sim/<control>/f<frame>`: the effect's alive count and whether it is culled, its renderer's
bounds (`renderer=`, world space), and of its particle system the bounds it answers
(`GetParticleSystemInfo`, `bounds=`), the bounds computed from its particles (`computed=`) and
its bounds padding (`padding=`). The last two are members `VisualEffect` declares for the
Editor (`GetComputedBounds`, `GetCurrentBoundsPadding`), called through reflection. Boxes are
written as centre and extents, floats with every digit.

- `auto`: the box follows the particles. `bounds=` and `computed=` are the box of the frame
  before (the particles' spheres, in the effect's space), `renderer=` the one of the frame
  before that: it is zero at the effect's place in frame 2.
- `padded`: the padding grows the box on every side; at frame 20 the probe sets `Padding` to
  another value, which the box of that frame already has.
- `world`: a system in world space: its box is in the effect's space all the same.
  `world turned`: the same on an effect turned 30 degrees: the box is along the effect's axes
  and the renderer's world box around it (three rows; `matrix/world turned/*` are the effect's
  matrices).
- `dying`, `stopped`: two effects of `BoundsDying`, the second's spawner stopped at frame 20:
  each has the box of its own particles alive, and once none is left a zero box at the effect's
  place. `stopped padded`: the same of `BoundsDyingPadded`: once none is left the box is the
  padding around the effect's origin. Before the system first wakes (frame 3: it has spawned
  nothing yet) its box is zero, without the padding, and at frame 2 the padding it answers is
  zero too.
- `away`, `manual away`: the effect with automatic bounds is culled from frame 20 on, when its
  renderer's box has left the view, and goes on simulating; the one with the graph's bounds
  never is. (`manual away` computes no bounds: `computed=` is zero, and its padding is not
  asked, a question the Editor does not survive.) Its `renderer=` box is not its `bounds=` box
  in the last place of the centre: an effect's box is the union of its systems' corners, taken
  back as a centre and extents; so is every other control's.
- `reinit`: `Reinit()` at frame 20: the box is zero until the new particles' box arrives.
- `moved`: the effect moved half a unit at frame 20: the box stays (it is in the effect's
  space) and the renderer's follows.
- `turned`: the effect turned 30 degrees, its system in local space: the box is along the
  effect's axes, the renderer's world box around it.
- `many`: a hundred and fifty particles: the box of them all.
- `shared`, `shared second`: two effects of `BoundsShared`, the second half a unit up and with
  another seed (three rows each): see the residuals.

## Image limits

Every image is the Editor's pixel for pixel on the machine both ran on (an NVIDIA driver, the
Linux Editor on OpenGL Core).

## Known residuals

`residuals.json` lists seven rows with their causes:

- `shared` (one row) and `shared second` (three): the Editor runs the effects of one instanced
  graph as a batch, and where a system kills no particle the kernel's reduction runs over
  thread groups that span the batch's effects: the first effect of a group gets the box of
  every particle in the group, each in its own effect's space, and the others none. Unbound
  gives each effect the box of its own particles. (Effects whose particles die have a thread
  group of their own each: `dying` and `stopped` are exact.)
- `world turned` (three rows): the box differs from the Editor's by up to 6e-5 along x and y,
  where the kernel's matrix product cancels terms of the size of the effect's place
  (x = 1300), and by one unit in the last place of the extent along z: last places that
  depend on the shader compiler's arithmetic. `world`, the same system on an effect that is
  not turned, is exact, and so is `turned`, a system in local space on a turned effect. A
  residual row is left out whole: nothing else checks a world-space system on a turned effect.

## Not covered

- A graph saved while its bounds were being recorded (bounds computed, and the graph's used):
  the Editor's graph model does not keep that state, so no graph of the probe has it.
- Several steps in a frame (the box is emptied before a frame's last step alone), a frame
  without a step, a paused effect, a prewarm, `Simulate`.
- Systems of strips, systems spawned by GPU events, mesh outputs, scaled or non-uniformly
  scaled particles and effects, a system with several update contexts.
- An effect culled with culling flags that stop its simulation or its bounds update (an asset
  with automatic bounds has none of them unless they are set again by hand).
- The effects of a batch beyond two in one thread group.
- A system that falls asleep with particles' box still standing, and the padding a system
  answers after a reinitialisation, before it wakes again.
- Other devices than OpenGL Core in the Editor.

## Running

    python3 tests/projects/vfxbounds/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfxbounds/run.py record --label b
    python3 tests/projects/vfxbounds/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfxbounds/run.py gate                 # the engine on the authored project
