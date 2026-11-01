# Particle seed and axis initialization

This control makes no `Simulate` calls. Each fresh stopped system has an explicit
seed, identity transform, local space, disabled shape, rate zero and one constant
burst. Child speed is 1, lifetime 2.013, size .75, rotation .3 radians and color
[192,128,224,200]. Safe particle fields and public system states are read
immediately after each call. Buffers are cleared before every read.
`GetMeshIndex` is never called.

The predeclared matrix uses child seeds 12345 and 305419896, and explicitly
supplied stationary parent particles with seeds A=12345 and B=3735928559.
Parent system seed is independently fixed at 54321, with no stored particles.
Fresh child systems receive manual triggers in ABA and BAB order, for each of
InheritNothing and InheritColor|InheritSize|InheritRotation|InheritLifetime.
The supplied parent has zero position/velocity, size 2, color [128,192,64,255],
rotation [10,20,30] degrees, axis [1,0,0], lifetime 4.026 and remaining life 2.013.
Each child seed also gets three ordinary `Emit(1)` calls with the same explicit
configuration. The original control has 10 systems, 30 emitting calls and 40 complete state records.
Independent captures repeat the entire matrix, including its internal order
controls. No birth/death scheduler, external delta or fixed-step trial is involved.

Offline hypotheses are selected before capture. They translate the existing
`ParticleRandom.Hash` and `ParticleRandom.Value` with `SubEmit`, plus the current
particle-system xorshift64* initialization and call accounting. They test
parent-only, child-only and parent/child inputs without fitting any constants.
The script also tests the existing script `Random.State.FromSeed` recurrence as
a separate source-derived hypothesis. Every prediction, mismatch and missing
observation is retained. A passing subset cannot establish the seed mechanism.

Prepare and capture one pair, choosing the release explicitly:

```bash
RUN="$HOME/.cache/unbound/validation/runs/particleseedaxis-example"
python3 tests/projects/particleseedaxis/run.py prepare --unity-version 6000.0.71f1 --work "$RUN" --label native-a
python3 tests/projects/particleseedaxis/run.py prepare --unity-version 6000.0.71f1 --work "$RUN" --label native-b
python3 tests/projects/particleseedaxis/run.py native --unity-version 6000.0.71f1 --work "$RUN" --label native-a
python3 tests/projects/particleseedaxis/run.py native --unity-version 6000.0.71f1 --work "$RUN" --label native-b
```

The runner enters the installed `with-heavy-job` once per capture. All generated
projects, logs and reports belong under the validation cache. `engine` additionally
requires `--cli` and `--runtime` pointing to a private verified matching runtime.
No runtime build or production-code edit is part of this probe.

## Measured result for 6000.0.71f1

The original independent pair agrees byte-for-byte on all 40 records. Counts equal the
number of emitting calls, clocks stay zero, and reported remaining/start lifetime
are equal at each observation. Manual emission retains a tiny nonzero z position
of about 1e-10; its exact value remains in the reference.

Both ordinary and manual emission use the same sequence for each child seed.
Changing the supplied parent seed, its order, or inheritance flags does not change
that sequence. Child seed 12345 produces 692788716, 1622567041, 3251230716;
child seed 305419896 produces 1131246930, 3829280589, 3382565082.
The supplied parent seeds themselves remain unchanged after the by-ref call.

All nine predeclared causal hypotheses fail the requirement to predict all 30
emitted seeds and all 60 stored seed observations. The existing script Random
xorshift128 recurrence produces the observed values at words 1, 6 and 11.
This is an observation about the stream, not an accepted draw-consumption rule.
The control does not independently vary the number or kind of initialization
samples, so hard-coding a five-word stride would be a fit to the output.
`ParticleRandom.Hash`/`Value` and the current particle-system xorshift64* models
predict none of the emitted seeds. No lookup, fitted stride or RNG patch is supplied.

The first particle in every system reports axis [0,1,0]. Later ordinary particles
and manual particles with flags 0 report [0,0,1]. Later manual particles with flags
15 report [1,0,0], matching the supplied parent's axis. These are observations,
not a proven ordinal initialization rule. Replacing every default axis with Y
would contradict this control. The seed and first-axis residuals therefore already
occur in ordinary emission. A Death-only initialization repair is not justified.

Inheritance fields also remain explicit: flags 15 give start/remaining lifetime
4.05216932, size [1.5,1.5,1.5], color [96,96,56,200] and rotation [0,0,47.188736].
The reference retains these fields without narrowing the comparison to seeds.

The private immutable v14 baseline completes all 40 records with correct counts.
Strict parity fails with 240 paths in 30 records and 60 exact raw-seed mismatches
across stored snapshots. The full union includes axis, seed, rotation and lifetime
values. It is a baseline observation, not a v15 runtime acceptance or a new
runtime regression. Production initialization, simulation, traversal, Birth,
Death and removal code remain unchanged. No complete causal seed/axis mechanism
or shared-helper implementation contract was established in the bounded control.

## Review without recapture

```bash
python3 tests/projects/particleseedaxis/verify.py --unity-version 6000.0.71f1 --output "$RUN/reference-review.json"
python3 tests/projects/particleseedaxis/analyze.py --unity-version 6000.0.71f1 --output "$RUN/algorithms.json"
python3 tests/projects/particleseedaxis/diagnose.py --unity-version 6000.0.71f1 --output "$RUN/diagnostics.json"
```

`verify.py` exits zero for the canonical reference. Add `--native-a` and
`--native-b` to verify exact independent agreement. Add `--actual` to retain the
complete comparison union and get a nonzero exit on parity failure. Existing
particle float/color tolerances remain unchanged; a separate audit requires exact
raw uint seeds. `analyze.py` reports every model prediction and mismatch, including
old particles in later snapshots. Its completion status does not accept a model.

## Initialization interventions for 6000.0.71f1

The canonical reference preserves the original 40 records and adds 25 records
from eleven fresh systems. One independent pair agrees exactly on all 65 records.
The added controls use seed 12345, except for an explicit stopped-system reset to
305419896. They compare bulk Emit(2)/Emit(5), three separate Emit(1) calls before
reading, 3D size/rotation separately and together, two-constant random settings,
an explicit particle seed and an imported particle axis. No simulation occurs.

Bulk Emit(2) retains seeds 692788716 and 601952211; a subsequent Emit(1) retains
1622567041. Bulk Emit(5) retains 692788716, 601952211, 495557956, 337019225 and
1622567041; its subsequent Emit(1) retains 3251230716. These distinguish packed
four-lane initialization from a scalar stream. Size3D or rotation3D moves the
next call's seed to 2613184690; both flags move it to 1812774966. Randomized
settings consume the same draws as constant settings with the same 3D flags.
Lifetime, size, rotation and color sample results remain in the complete records.

The first nonempty read fills all existing unmaterialized axes with Y. Empty
reads do not activate storage. Later emitted particles use Z or the inherited
axis, and existing axes persist. Clearing and changing the seed preserves axis
storage: the reset pair reads Z for both new particles. SetParticles preserves
its supplied X axis, then ordinary emission uses Z. Explicit randomSeed=77
retains 77 for both particles and leaves the next ordinary seed at 589364589.

The original nine hypotheses and their failed predictions remain available in
analyze.py. They describe the original control, independently of these added
interventions. Runtime reports must retain all fields and exact uint seeds.
