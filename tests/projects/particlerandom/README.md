# Seeded particle random values

Unity release: 6000.0.71f1. Run with `UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1`.
`run.py native --label <unique-label>` records a private project copy through the Editor job pool.
Two identical captures are required by `run.py publish <label1> <label2>`.
`run.py extend <label1> <label2>` adds new keys from two agreeing captures, refusing any change to an existing value.
`run.py check` runs only Unbound and compares every recorded value against the committed reference.
Outputs and repeat captures stay in the validation cache.

The probe covers fixed seeds, start values, shapes, module random modes, burst counts and
probabilities, Birth sub-emitters, custom data, mesh selection, Simulate and Play. Seeds are stored
as two exact 16-bit halves. The comparator requires exact halves, counts and mesh indices.
Other values use the gravity probe's existing absolute and relative tolerance of 0.0002.

The Unity 6000.0 [Main module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysMainModule.html)
promises repeatable playback when automatic seeding is disabled.
[ParticleSystem.randomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-randomSeed.html)
selects that effect, and
[Particle.randomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Particle-randomSeed.html)
keeps each particle's random properties stable during simulation.
[MinMaxCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MinMaxCurve.html)
describes the constant/curve interpolation modes. These pages do not specify a generator,
module salts or draw order. Exact numeric sequences here are measurements of this release.

Recovered authored sources and the initial reference from branch `t3/particle-random-stream`
at base `d370c642`. No unlanded commits or tracked edits existed there. The independent cached
captures `a1` and `a2` agree exactly. The recovered baseline matched 70 of 277 records.


## Measured draws

The fixed-seed controls cover three system seeds, Simulate and Play, repeated steps and loop
boundaries. These observations describe 6000.0.71f1; the documentation does not promise these
numeric sequences for other releases.

| Property | Measured sampling |
| --- | --- |
| Start lifetime, size, rotation, color and particle seed | Existing initialization lanes, independent of the shape stream |
| Gravity, size, rotation, color, drag, radial velocity | Restart from the particle seed plus a module salt; size/rotation axes share a draw |
| Linear velocity, force, orbital velocity | Three successive draws from a module's seeded generator |
| Speed modifier | One draw from the first particle's seed in a group of four |
| Randomized force | Persistent four-lane generators; three draws per group and update, including unused lanes |
| Custom vector components | Separate seed offsets for every component and stream |
| Custom colors | A separate seed offset for each stream |
| Mesh selection | A seeded full word modulo the mesh count |
| Emission | One time-rate draw per step, including constants; loop segments share it. Constant burst counts draw nothing; two-constant counts use a full word modulo the inclusive integer range |
| Sub-emitter selection | A stable roll owned by the parent particle; Death/Manual burst probability and count restart from the parent seed plus the parent system seed |

The mixed orbital-axis controls record the Editor's diagnostic and ignored motion. The matched
controls keep all three axes in Two Constants mode, including zero-width ranges on unused axes.
Assigning `randomSeed` disables automatic seeding, as the Scripting API documents. A restart
prepares selected child systems before a parent emits into them; it no longer erases those
particles afterwards. Playback snapshots retain the emission and randomized-force cursors.

## Retained differences

All raw values, missing/extra keys and failing indices remain in comparator output. The residual
registry names only the following fields; it does not change numeric tolerances.

- `shape/randomize`: position and velocity with random position/direction and spherical direction
  combined. The combined shape sampling order remains unresolved. Sphere, hemisphere, cone,
  cone volume, box, circle, edge, donut and rectangle controls match without those modifiers.
- `start/rate`, steps 1 and 2, particles from index 3: a batch's start samples are assigned to births
  in a different order, also changing shape positions and how far each seeded particle has aged.
  This has observable effects. It needs a separate batch-initialization and birth-age fix.
- `start/gradients`, `color/gradients`, `color/randomcolor`: packed color interpolation differs by
  byte values after the random draw. Public MinMaxGradient evaluation and particle packed colors
  need separate measurement before changing their arithmetic.
- `subemit/child`: Birth child origin and first-step age in Simulate remain different. Counts and
  particle seed halves still compare exactly. These timing differences are separate from the
  stable sub-emitter selection roll.
- Color, custom, played, sub-emitter and the later speed-modifier records report nonzero reference
  angular velocity after the rotation scenarios, although they enable no rotation module. The
  cause is unresolved. Only the recorded nonzero-reference/zero-candidate angular Z field is
  classified as known; the raw values remain printed. No general initialization rule follows
  from this capture.

Random modes in inherited velocity, lifetime by emitter speed, texture-sheet animation, collision
and lights remain outside this probe. Their existing implementation is unchanged and is not
certified by these measurements. Other Unity releases remain unmeasured.
