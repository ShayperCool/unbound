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
configuration. Total: 10 systems, 30 emitting calls, 40 complete state records.
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
