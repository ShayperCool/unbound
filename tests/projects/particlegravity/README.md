# Particle gravity probe

Measures how a particle system's gravity follows the physics settings. The project's
`DynamicsManager` gravity is (1.5, -4.25, 2) and its `Physics2DSettings` gravity (-2.5, -6), both
off the -Y axis. `Assets/Scripts/ParticleGravityProbe.cs` builds every system in code: one burst at
time 0 of a particle moving along +Z at 3 units/s, no shape, a fixed seed, a 1 s looping duration.
It steps each system 25 times with `Simulate(0.043)` and records the count and every particle's
position and velocity after each step:

- `3d/modifier*`, `2d/modifier*`: gravity modifiers 0, 1, 0.5 and -1.5 with each source.
- `3d/runtime`, `2d/runtime`: `Physics.gravity` / `Physics2D.gravity` changed between steps, and the
  other source's gravity changed (no effect). `switch/source`: the source switched and back.
- `world/*`: world simulation on a rotated, scaled emitter. `local/*`: local simulation on a rotated
  emitter, with Hierarchy, Local (under a scaled parent) and Shape scaling.
  `custom/*`: a rotated, scaled custom simulation space, with and without start speed.
- `curve/*`: modifier curves over the duration. `random/*`: two constants (also given reversed) and
  two curves (5 particles). `delay/*`: a 0.2 s start delay with a rising curve, `Emit` during it.
- `played/*`: two systems played over frames at `Time.captureDeltaTime` 0.02 with
  AlwaysSimulate culling (the scene has no camera); both gravities change in `Update` after frame 5's
  record. `settings/*`: the gravities the project starts with.

Record (two independent GPU Editor captures; publish only when they are byte-identical):

    python3 tests/projects/particlegravity/run.py native --label native1
    python3 tests/projects/particlegravity/run.py native --label native2
    python3 tests/projects/particlegravity/run.py publish native1 native2

Check the engine (the landing gate, `gate.json`): `python3 tests/projects/particlegravity/run.py check`.
Run output stays under `$XDG_CACHE_HOME/unbound/validation/runs/particlegravity/`.

The canonical oracle is `Reference/unity-6000.0.71f1.jsonl` (639 records, Unity 6000.0.71f1 on
Linux, GPU Editor). Two independent captures were byte-identical.

Documented (6000.0 Scripting API `ParticleSystem.MainModule.gravityModifier`: "A scale that this
Particle System applies to gravity, defined either by Physics.gravity or Physics2D.gravity";
`gravitySource`: "Specify whether to use the gravity strength from the 2D or 3D physics system";
Manual, Main module: Gravity Modifier "Scales the gravity value set in the Physics window. A value
of zero switches gravity off."). Measured in this release, beyond that:

- The 2D source is (x, y, 0). The project's settings apply from the start.
- A change of `Physics.gravity` / `Physics2D.gravity` reaches a running system at its next
  `Simulate` step. In the player loop, a change made in `Update` applies to that frame's update.
- In local and custom simulation space, gravity is the world vector through the inverse
  simulation-to-world transform, scale included. Local space divides by the scaling mode's scale
  (Hierarchy: lossy, Local: local, Shape: none); custom space by the custom transform's.
- A modifier curve is read at the system time where each internal step ends (with the loop's wrap).

- A start velocity in a scaled custom space keeps its speed along the inverse-transformed
  direction (`custom/rotated_scaled`).
- Random constants with the minimum above the maximum draw between them as given
  (`random/constants_reversed`).
- A start delay takes its part of a step before the playback time moves; curves read where the
  step ends in playback time. A system that `Emit` stopped during its delay never leaves it: no
  burst fires and the curve stays at time 0 (`delay/*`, looping and not).

The comparator has no known differences (KNOWN is empty).

Unmeasured: other releases; zero or negative scales; the noise scroll during a start delay (it
reads the same playback time).
