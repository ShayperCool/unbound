# Sub-emitters probe (Death and Manual)

Measures what Death and Manual (`ParticleSystem.TriggerSubEmitter`) sub-emitters emit.
`Assets/Scripts/SubEmittersProbe.cs` builds every system in code. Parents get their particles from
`Emit` with explicit positions, velocities (+Y at 1 unit/s), lifetimes, colors, sizes and rotations;
children use world simulation space, no shape, a constant start speed of 1 along +Z and a 2.013 s
lifetime. Most scenarios step the parent with `Simulate(0.05, true, false, false)` 14 times and record
the parent's count and time and every child particle after each step (sorted by position, so storage
order does not matter):

- `death/*`: four parents dying at 0.123, 0.223 and (two) 0.323 s. The child's bursts vary: one,
  two, the first one late (`late_first`, with the burst times read back), three different times,
  cycles (finite, infinite, after a first burst), none (rate only), a disabled Emission module. Also
  the same child in two Death slots, two different children, 40 parents dying in one step into a child
  capped at 100 particles, inherited color/size/rotation/lifetime (also from color and size over
  lifetime), and a child rotated against its parent.
- `manual/*`: four long-lived parents, `TriggerSubEmitter` called before steps 3 and 8 and the child
  recorded right after the call (`call/`). Bursts as above, a disabled module, a Death slot, inherit
  everything and lifetime (also 4 s parents), the `ref Particle` overload with a moved, recolored
  particle, the `List` overload, and an index past the last slot (`result/`: exception thrown, errors
  logged).
- `random/*`: 120 parents, burst probability 0.5, a 2-6 count range, emit probability 0.5 (Death and
  Manual). Only [parent count, child count] is recorded.
- `timing/*`: ten parents dying at (k + 0.3) / 10 of step 2 for steps of 0.02, 0.05 and 0.1 s (one,
  two and four internal steps), also while the frame delta is 0.01 and while the fixed timestep is
  0.01.
- `direction/*`: one parent moving along nine directions (zero included) with the child unrotated,
  turned 90° about Y and -90° about X; a rotated parent; a child whose zero-size box shape is moved off
  centre; a Death child where four parents die.
- `played/*` (frames at `Time.captureDeltaTime` 0.02, the state recorded in the next `Update`):
  TriggerSubEmitter from `Update` (`update`), from `OnParticleSystemStopped` with the particles saved
  in the last `Update` (`stopped`, the parent played again at frame 20), from `OnParticleTrigger`
  (`trigger`), a one-shot parent whose Death children outlive it (`finish`), and Death children in
  0.05 s frames (`death`). The `finish` child's stop action is Callback.

Record (two independent GPU Editor captures; publish only when they are byte-identical):

    python3 tests/projects/subemitters/run.py native --label native1
    python3 tests/projects/subemitters/run.py native --label native2
    python3 tests/projects/subemitters/run.py publish native1 native2

Check the engine (the landing gate, `gate.json`): `python3 tests/projects/subemitters/run.py check`.
Run output stays under `$XDG_CACHE_HOME/unbound/validation/runs/subemitters/`.

The canonical oracle is `Reference/unity-6000.0.71f1.jsonl` (1321 records, Unity 6000.0.71f1 on
Linux, GPU Editor). Two independent captures were byte-identical.

Documented (6000.0 Manual, Sub Emitters module: "The Collision, Death, Trigger, and Manual
conditions support only a single burst in the Bursts list"; Manual "Emits particles only when
explicitly triggered by ParticleSystem.TriggerSubEmitter"; Scripting API SubEmittersModule death0:
"uses the first set of emission burst properties ... If the system does not contain burst
properties, then the system emits nothing"; TriggerSubEmitter "Triggers the specified sub emitter on
all particles" or on the given ones; ParticleSystemSubEmitterProperties.InheritLifetime: "New
particles will have a shorter lifespan, the closer their parent particles are to death"). Measured in
this release, beyond that:

- Only the first burst in the list fires (not the earliest), at once, whatever its time; its count
  once: cycles (also infinite) and the interval are ignored. Its probability applies. A disabled
  Emission module on the child does not matter; a child without bursts emits nothing.
- The same child in two Death slots emits once, from the first slot; two children both emit.
- `TriggerSubEmitter` emits at once (the child's count right after the call), at the parent
  particle's current position, also on a Death slot. A disabled Sub Emitters module emits nothing, and
  its child does not emit on its own either. An index past the last slot does nothing, without an
  exception or a logged error.
- The child's emission frame turns +Z to the parent particle's world direction of travel and +Y
  towards +Z as far as that allows; a still particle keeps the child's own frame; a rotated parent
  does not turn the direction further.
- InheritLifetime multiplies the child's start lifetime by the parent's remaining lifetime in seconds
  (2.013 × 3.863 = 7.776); a dying parent leaves none, so its Death children are not emitted.
  InheritColor/Size use the parent's current color and size (with its over-lifetime modules).
- A parent whose `Emit`ted particles all died is stopped, not reset: its time keeps running with
  each `Simulate` step (also within the step its last particle dies in).
- Played frames: Death children start where the dying particle's internal step ends, with their full
  lifetime at the end of the frame (not aged in it), and the child starts playing. A parent whose last
  particle died is no longer playing but stays alive, and its `OnParticleSystemStopped` comes in the
  frame its sub-emitters' last particles die, after the child's own `OnParticleSystemStopped`
  (the child, playing since its Death particles came, stops when they die). Particles triggered from `OnParticleSystemStopped` do
  not move (the child does not play) until the parent plays again. A child triggered from `Update`
  plays from the next frame.

Known differences (the comparator lists each one and keeps it out of the verdict):

- First-step age of Death children in `Simulate`: Unity's newborns have lived a multiple of 0.02 s
  that depends on the internal step they were born in and on `Time.fixedDeltaTime` (four steps of
  0.025 s: 0.1, 0.06, 0.04, 0.02 s; with a fixed timestep of 0.01: 0.1, 0.07, 0.05, 0.02 s), not on the
  frame delta. Unbound ages them by the rest of the call's child update. Positions differ by that age
  along the velocity; counts and every other value match. Played frames match.
- A child rotated against its parent (`direction/*/1`, `/2`, `death/child_rotated`): Unity combines
  that rotation with the emission frame in a way that is not a rotation of the child's direction
  (two orthogonal child directions give non-orthogonal velocities); Unbound applies it inside the
  frame. Unrotated children match.
- `death/many`: once the child is full, Unity takes the particles dying in one step in another
  order (the first four in order, then from the end), so other parents get the last children.

All 58 `random/*` count records compare exactly. Selection uses a stable roll from the parent
particle seed; burst probability and count use the parent particle seed plus the parent system
seed. The comparator has no statistical count allowance.

Birth origin and Simulate age controls are in [particlerandom](../particlerandom/README.md).
They extend that probe's fixed-seed Birth histories; this probe retains its Death and Manual scope.

Unmeasured here: other releases; Birth, Collision and Trigger sub-emitters (Collision and Trigger use the
same count rule, unmeasured); a paused parent; the up axis of the frame for a rotated parent; Inherit
Duration.
