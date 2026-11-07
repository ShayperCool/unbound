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

## Rate batch controls

The P4 controls add 600 records in two extensions, each recorded twice in independent private
Editor projects. The first adds 552 records and the second adds 48 capacity, expiry, start-curve
and mixed-burst controls. Publishing refuses changes to any existing key. The matrix varies all
three system seeds, constant and ranged time rates, plain and sphere shapes, counts around 4,
8 and 16, four successive Simulate calls, Play, and a 0.05-second loop boundary. A 0.02-second
step with the maximum particle delta set to one isolates a single update. The original controls
retain their original step subdivision and their original reference values.

| Contract or measurement | Applicable scope |
| --- | --- |
| [Main module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysMainModule.html) | Start lifetime, speed, size and color control new particles; disabling automatic seeding makes playback repeatable. Duration and looping control playback time. |
| [Emission module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysEmissionModule.html) | Time rate is particles per unit of time; bursts have configured times and counts. |
| [randomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-randomSeed.html), [useAutoRandomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-useAutoRandomSeed.html) | A system seed selects a repeatable effect; automatic seeding selects a new seed. Assigning a seed disables automatic seeding. |
| [Simulate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Simulate.html) | Advances by the requested period, restarts when requested, optionally includes children, then pauses. Fixed-step mode uses the configured fixed interval. |
| [Emit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Emit.html) | Scripted particles emit immediately; supplied EmitParams override specified properties only. These calls retain their existing path. |
| [GetParticles](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.GetParticles.html) | Returns the currently alive particles into the supplied buffer; a partial buffer may return only part of the system. No general particle-order guarantee is stated. |

The batch assignment and age arithmetic are measurements of 6000.0.71f1, not documented promises
about generators or ordering. In a rate-containing update the aligned part of the newly allocated
range receives the first initialization samples, then the leading partial group receives the
remaining samples. Rate ages increase in sample order from the carried fractional emission to
the oldest birth. The whole step shares its accumulated rate gain, including across a loop
boundary. Start curves use the final playback time; start speed uses that time minus the birth's
age, clamped to the curve's range. Bursts in the same update share initialization samples with
the rate births. Emission decisions keep their separate measured draws.

This matters beyond GetParticles order. For example, the original `start/rate/12345/1` particle
with seed 3092723128 has start lifetime 1.31924272 and age 0.02 seconds in the reference. The
previous assignment gave that seed age nearly zero and a different buffer slot. Its position,
subsequent remaining lifetime and eventual expiry therefore differ. The plain controls isolate
motion and lifetime from shape sampling; sphere controls keep the resulting spatial histories.
Complete four-lane groups initialize before expired newborns are compacted. Unused final lanes
have the minimum age and can become retained particles after compaction. They do not contribute
trigger, collision or Birth sub-emitter events. No existing particles are sorted to obtain these results. Other releases and ring-buffer batch
assignment remain unmeasured and retain their prior path through the compatibility profile.

The original 343-record window improves from 256 matches and 87 known differences to 262
matches and 81 known differences. The 600 added controls have 598 matches and the two existing
particle permutations described below, for 860/943 matching records, 83 known and zero
unexpected differences. All rate birth seed halves, start values, positions and ages match.

## Shape modifier controls

The P5 extensions add 1206 keys, each recorded in two independent, identical Editor captures. They retain all
943 existing reference values. Sphere, cone and box cover every combination of zero,
intermediate and full position, random-direction and spherical-direction amounts under three
fixed seeds. Additional burst, rate, separate Emit calls, and transformed Local/World controls
check continued stream consumption. Circle, edge and cone-volume controls discriminate the shared modifier
path. The shape transform includes translation, rotation and nonuniform scale.

| Contract or measurement | Applicable scope |
| --- | --- |
| [Shape module manual](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysShapeModule.html), [ShapeModule API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule.html) | Shape controls initial position and direction. Direction amounts blend from the shape direction at zero to random or outward direction at one. Position randomization moves the spawning position by up to the amount. The manual's Hemisphere/Circle spherical-direction description does not specify numeric samples. |
| [randomDirectionAmount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule-randomDirectionAmount.html), [sphericalDirectionAmount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule-sphericalDirectionAmount.html), [randomPositionAmount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule-randomPositionAmount.html) | Direction amounts accept 0 to 1; intermediate values blend directions. A higher position amount increases displacement. No generator, draw order, normalization order or cross-release numeric sequence is promised. |
| [randomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-randomSeed.html), [useAutoRandomSeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-useAutoRandomSeed.html) | Assigning a seed disables automatic seeding. Disabling automatic seeding reuses that seed for repeatable playback. This does not specify sample order or guarantee these numeric sequences across releases. |

Measured in 6000.0.71f1: random direction consumes azimuth then height from the existing shape
stream. Blend that unit direction with the basic shape direction, then blend toward the
normalized basic shape position. Normalize after both blends, rather than between them.
Cone base emission instead blends two disc samples before applying the cone angle and normalizing;
the direction-only disc uses a minimum squared radius of 0.001, independent of radius thickness.
Cone volume emission retains its basic direction and along-cone position, then uses the shared
sphere-direction blend. Position randomization consumes another azimuth/height pair and adds a fixed-length offset.
It does not affect the outward direction used by the spherical blend. All modifiers operate
before the shape transform; the transformed direction is normalized. Zero amounts consume no
extra draws. Packed shape lanes retain their existing advancement, independent of initialization
and emission. Other releases retain the previous modifier path through the compatibility profile.
These are measurements of the captured contexts, not a general ordering contract for particles.

All six original `shape/randomize` records now match, and their previous residual allowance is
removed. The original 943-record window improves from 860 matches/83 known differences to
866/77; the original 343-record window improves from 262 to 268 matches. All 1206 new controls
agree in particle identities and every per-seed value. The combined report has 2000/2149 matching
records, 149 known differences and zero unexpected differences. The 149 comprise the 77 retained
original residuals and 72 scripted Emit buffer permutations described below. Numeric tolerances
are unchanged. Emission remains at 18 failing records/54 values, gravity at 568/639, sub-emitters
at 975/1321 with all 58 random count controls exact, seed/axis at 48 failing values, and particles
at 1520/1560 with four of 94 scenarios differing. The 17 particle images are byte-identical to
the before capture; their worst reference mean remains 0.92.

## Birth origin and age controls

The P6 extension adds 342 records, each recorded in two independent, identical Unity
6000.0.71f1 Editor captures. All 2149 pre-task values are retained. The new systems explicitly
enable zero rotation over lifetime to isolate their histories from the angular-velocity context
of the earlier rotation scenarios. The fixture covers one and five parent births from bursts,
time rate and scripted Emit, successive small calls and one larger call, restart and withChildren,
zero and nonzero child speed and gravity, expiry, Local/World parent and child spaces, translation
and rotation, child time rate and multiple bursts, combined child rate/bursts, both retaining ring
modes, and fixed intervals of 0.01, 0.02 and 0.03 s.

| Unity 6000.0 contract | Applicable guarantee and limits |
| --- | --- |
| [Sub Emitters module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysSubEmitModule.html), [Birth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemSubEmitterType.Birth.html) | Sub-emitters spawn at the parent particle. Birth supports rates and multiple bursts throughout its lifetime. The single-burst restriction applies to the other event types. Neither page specifies an intra-step schedule or a particle buffer order. |
| [ParticleSystemSubEmitterProperties](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemSubEmitterProperties.html) | InheritNothing leaves child start properties independent. InheritLifetime shortens lifetime towards the parent's death; no numeric formula is promised. These new controls use InheritNothing. |
| [Simulate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Simulate.html) | Advances by the requested period and pauses; restart resets time, withChildren includes child systems, and fixedTimeStep uses the configured fixed interval. The child newborn catch-up schedule is unspecified. |
| [simulationSpace](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-simulationSpace.html) | Selects Local, World or Custom simulation. The new transformed controls measure Local and World only; Custom remains unmeasured here. |
| [ringBufferMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-ringBufferMode.html), [Main module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysMainModule.html) | Retaining modes preserve existing particles beyond their lifetime and recycle them at capacity. This does not specify whether to initialize a Birth emission already expired by its catch-up time. |

Measured in this release: a Birth burst at age zero starts at the parent's position before
movement, including particles emitted from script. Scripted Emit does not immediately create
the child. The newborn integrates its share of the parent step, then catches up through whole
fixed intervals for the remaining Simulate call. A burst child in a 0.05 s call with maximum
particle delta 0.03 and fixed interval 0.02 integrates 0.025, 0.02 and 0.02 s, reaching age
0.065 s. Five separate 0.02 s calls reach age 0.12 s; one 0.1 s call reaches age 0.125 s.
These histories affect positions, gravity, remaining lifetime and the call in which the child
disappears. They are measurements of these contexts, not a general callback or particle-order
contract. Other event types retain their existing paths.

The captured histories advance existing Birth child particles before new parent emissions.
New children therefore do not replace expired old children during the old-particle update.
Time-rate initialization assigns ages from youngest to oldest, retaining each seed's position
and eventual expiry. An emission whose remaining call time reaches the constant lifetime bound
is skipped before drawing start samples. The implementation uses the exact maximum of constant lifetime modes for this bound.
The early-skip capture uses a constant; ranged lifetime variants, lifetime curves, lifetime by
emitter speed and inherited lifetime remain unmeasured here. No callback-order guarantee follows
from these particle histories.

The combined controls retain the previously matched seed identities by initializing child rate
and burst particles together. Rate particles take start samples before bursts, with each part
keeping its own origin and age. Two independent ring-mode captures report zero new Birth children
for lifetime 0.05 and a 0.1 s call, in both PauseUntilReplaced and LoopUntilReplaced. The early skip
therefore also applies in these measured ring contexts. Separate unit controls retain already
existing ring particles beyond their lifetime, as the documented contract requires. The skip
does not remove existing particles.

For the measured moving-parent transforms, the Birth frame faces along the parent's world
velocity. Its up vector blends the child's world forward and up axes by the absolute dot product
of travel direction and child forward; the child's rotation relative to the parent is then
applied inside that frame. The world spawn point is converted to the child's simulation space,
including its transform's translation. This is a release-specific frame measurement. It does
not certify the unresolved rotated-child frames for Death or Manual.

All 342 new records match strictly. The combined result is 2342/2491 matching records,
149 known differences and zero unexpected differences. Only the six original `subemit/child`
runtime records change; every original count and seed half is unchanged. Their position and
remaining lifetime now compare without an allowance. Their angular field retains its separate
raw residual, so the number of known records stays at 149.

Regression results retain emission's 18 failing records/54 values, gravity's 568/639 matches,
sub-emitters' 975/1321 with all 58 random counts exact, and seed/axis's 48 failing values.
Particles improves from 1520/1560 to 1522/1560, with four of 94 scenarios still differing.
All 17 images are byte-identical to the before capture and worst mean remains 0.92.
At P6 the broader particle probe retained eight Birth time-rate records, including boundary
counts and subsequent histories. P7 resolves these separately below, preserving the origin/age
controls. Lifetime curves, inherited lifetime, nested Birth systems and other releases are not
certified by this extension.

## Retained differences

All raw values, missing/extra keys and failing indices remain in comparator output. The residual
registry names only the following fields; it does not change numeric tolerances.

- `shapemod/(Box|Cone|Sphere)/[01]{3}/emit/<seed>/1`: the second scripted Emit call, five
  newborns after nine existing particles, has a buffer permutation even with zero modifiers.
  All 33 fields agree by particle seed. Only the measured permutation of that newborn tail is
  classified as known, after checking every field of the entire record. The nine old particles
  must remain in place. Every raw failure stays printed; no particle sorting or stronger order
  guarantee is imposed. These 72 newly exposed control records are outside modifier sampling.
- `ratebatch/edge/expiry/987654321/2` and `ratebatch/edge/expiry/4242/2`: two particles
  surviving from the preceding step occupy opposite buffer slots after other old particles die.
  All 33 fields agree by particle seed. The registry accepts only that exact pair permutation;
  every other slot and field keeps its usual comparison. This existing-particle compaction
  difference is outside rate birth assignment. No global removal or callback order is imposed.
- `start/gradients`, `color/gradients`, `color/randomcolor`: packed color interpolation differs by
  byte values after the random draw. Public MinMaxGradient evaluation and particle packed colors
  need separate measurement before changing their arithmetic.
- Color, custom, played, sub-emitter and the later speed-modifier records report nonzero reference
  angular velocity after the rotation scenarios, although they enable no rotation module. The
  cause is unresolved. Only the recorded nonzero-reference/zero-candidate angular Z field is
  classified as known; the raw values remain printed. No general initialization rule follows
  from this capture.

Random modes in inherited velocity, lifetime by emitter speed, texture-sheet animation, collision
and lights remain outside this probe. Their existing implementation is unchanged and is not
certified by these measurements. Other Unity releases remain unmeasured.

## Birth time-rate intervals, P7

The extension records 817 controls twice independently in Unity 6000.0.71f1 and
retains all 2491 prior keys unchanged. New systems use deterministic seeds, no
shape, and an enabled zero rotation module to isolate angular context. Controls
measure counts, exact seed halves, positions, remaining lifetimes and clocks.

| Official Unity 6000.0 source | Documented contract and limits |
| --- | --- |
| [Sub Emitters module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysSubEmitModule.html), [Birth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemSubEmitterType.Birth.html) | Birth emits throughout the parent lifetime and supports all Emission settings and multiple bursts. Only the other event types restrict emission to one burst. Intra-step boundary arithmetic is unspecified. |
| [Emission module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysEmissionModule.html) | Time rate is particles per unit time; burst times are seconds after playback begins. No floating-point count accumulator is specified. |
| [Simulate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Simulate.html) | Advances the requested interval and pauses. Restart resets playback; withChildren includes child systems; fixedTimeStep uses the configured fixed interval. Split and single calls need not share an undocumented newborn schedule. |
| [Main module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysMainModule.html) | Duration is system run time, start delay postpones emission and start lifetime sets each new particle's lifetime. Nonlooping systems stop emitting after their duration, while existing particles finish. |
| [Stop](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Stop.html), [Clear](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Clear.html) | StopEmittingAndClear stops emission and clears particles. Clear includes linked sub-emitters, independently of whether hierarchy children are selected. Subsequent stopped-clock arithmetic is measured by the new control. |
| [startLifetime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-startLifetime.html) | Lifetime is assigned at creation; PositiveInfinity prevents lifetime expiry. The finite-age interval path must preserve the prior rate behavior for infinite parents. |

Before any engine change the broader canonical probe fails exactly
`sub_emitters_birth_child` steps 7 through 14. Counts at steps 7, 9, 11 and 13
are respectively Native/engine 1/2, 3/4, 4/6 and 6/7. At step 8 particle 1
is at z 0.4325001 with lifetime 0.34800002 in Native, versus z 0.435 and
lifetime 0.343 in the engine. These are observable count and history differences,
not a request to impose a buffer order. The original raw captures and tolerances
are retained. New `birthrate/original` reproduces these values with angular
state isolated. `below` and `above` select 9.999/s and 10.001/s, with separate
1.013 s and 0.4 s parent lifetime controls, one/three parents, a fractional 7/s
rate, split/single calls, expiry, stop/clear, and child delay/duration controls.

The measured implementation uses per-parent active intervals after the child's
start delay and before a nonlooping child's duration. Exported parent ages select
rate boundaries, with the initial interval limited to the newborn's integration
span. Fractional count carry stays on each parent and the P6 initialization batch,
origin, age and random-draw paths remain separate. This arithmetic is a measured
6000.0.71f1 scheduling rule, not a guarantee in the Unity documentation.

In both delay controls the child's shared `time` stays zero while its particles
age and its per-parent emission interval passes the delay. This is measured for
0.05 and 0.1 s calls with constant delay 0.073 s, not a general promise about
all delayed sub-emitter configurations. StopEmitting leaves parent particles to
emit until expiry; StopEmittingAndClear clears linked particles and subsequent
non-restarting Simulate calls continue the stopped parent clock. Random start
delays, rate curves, child loop boundaries, inherited lifetime and nested Birth
chains remain outside this extension's measurements. Infinite parent lifetimes and
ring age rewinds retain their existing integration-delta rate path; the finite
exported-age interval measurement does not cover them. A managed regression
control checks that an infinite parent still emits children with finite histories.

The extended expiry captures also retain 26 raw buffer permutations. For
`birthrate/{original,below,above,lifetime04,stop}/child`, these are `17`,
`end/0.05/18` and `end/0.2/19`. For `birthrate/three/child`, they are steps
9 through 17, `end/0.05/18` and `end/0.2/19`. Every value agrees for every
unique particle seed, including positions, lifetimes and exact seed halves.
The comparator reports each original differing index and both full records,
and accepts these keys only when that complete identity check passes. No engine
sorting, global phase rule, or buffer-order contract is introduced.

P7 validation: 3133/3308 records match, 175 retained differences and zero unexpected
differences. All 342 P6 origin/age controls remain exact and all 2491 prior canonical
keys are unchanged. The 817 new controls have 791 exact records and 26 complete
per-seed matches with the reported expiry permutations. The original broad Birth
gate improves from 22/30 to 30/30 without an allowance or reference edit.

Stop clearing completes before stop actions run, so particles emitted by an
OnParticleSystemStopped callback survive. The managed regression test preserves
that behavior. [Unity 6000.0 OnParticleSystemStopped](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnParticleSystemStopped.html)
describes the callback as notification that the system has stopped. This does
not establish an order among callbacks of different systems.

## Birth lifetime inheritance, P8

The new `birthlife/` controls compare InheritNothing and InheritLifetime with
constant parent and child lifetimes in Unity 6000.0.71f1. They isolate a parent
shorter or longer than the child, age-zero bursts, later bursts and time rate,
three parent cohorts, split versus single Simulate calls, restart, both stop
modes, and child expiry. Counts, exact seed halves, positions, start and remaining
lifetimes use the existing full-particle recorder and unchanged comparator.

| Official Unity 6000.0 source | Documented contract and limits |
| --- | --- |
| [Sub Emitters module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysSubEmitModule.html), [Birth](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/particlesystemsubemittertype/birth) | Birth emits during the parent lifetime and supports all emission settings and multiple bursts. The other event types support one burst. No intra-step schedule is specified. |
| [InheritLifetime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemSubEmitterProperties.InheritLifetime.html) | Children born closer to parent death have shorter lifespans. No numeric formula is documented. |
| [Main module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysMainModule.html), [startLifetime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-startLifetime.html) | Start lifetime is assigned when a particle is created. Infinite lifetime prevents lifetime expiry. |
| [Simulate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Simulate.html) | Advances and pauses, optionally restarting and including children; fixedTimeStep uses configured fixed intervals. No equivalence of split and single newborn scheduling is promised. |
| [OnParticleSystemStopped](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnParticleSystemStopped.html) | With stopAction set to Callback, notifies after all particles die and no more will be born. No ordering among different systems is specified. |

The documented inheritance description does not establish that Birth uses the
measured Death/Manual formula. Lifetime curves, other inheritance channels,
nested chains and other releases remain outside this constant-lifetime slice.


Two expanded independent captures, `p8-native3` and `p8-native4`, are byte-identical
and append 1204 keys without changing any of the 3308 prior keys. The initial pair,
`p8-native1` and `p8-native2`, is also byte-identical. All four captures and the
baseline's complete failures remain in the validation cache. The baseline matches
4009/4512 records, with 175 prior known differences and 328 unexpected differences
in the new Birth lifetime controls.

| Cause | Measured 6000.0.71f1 behavior | Implementation scope |
| --- | --- | --- |
| Parent lifetime fraction gives the wrong child lifespan | Child start lifetime multiplies by the parent's remaining seconds after the parent integration step. A 0.413 s parent and 0.813 s child give 0.813 × 0.388 = 0.315444 s for the first birth. A 1.613 s parent and 0.413 s child instead give 0.655844 s. | Constant parent and child lifetime modes without lifetime-by-emitter-speed, finite parent lifetime, Simulate Birth path, selected by the compatibility profile. |
| Later emissions must use a later parent state | Rate and burst children emitted in one parent step share its remaining-seconds multiplier even when their initial ages differ. Later steps use shorter remaining lifetimes. | No interpolation of the multiplier to each child's birth time; initialization seeds, origins and catch-up ages keep the P6/P7 paths. |
| Lifetime changes affect expiry and callbacks | The isolated age-zero inherited child disappears in the sixth 0.05 s call. After resuming only the child, its stop callback first appears at captured frame 12 with inheritance and frame 37 without it. | Full histories include live counts and callback counters; no callback-order rule is added. |

These are observations in the captured contexts, not a documented numeric formula
or evidence about Unity's internal mechanism. The measurement leaves continuous
parent playback, lifetime curves, nested chains, infinite parent lifetimes,
lifetime-by-emitter-speed and other inheritance channels separate. Death/Manual,
Collision/Trigger and other releases retain their existing behavior.
