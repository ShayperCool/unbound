# Particle noise parity

The Unity 6000.0.71f1 Linux Editor probe records 23,646 observations across 144 configurations.
Two independent Editor recordings agree at the unchanged particle-stream absolute/relative
numeric tolerance of 2e-4. Every defined value is compared; the comparator has no exceptions.

The probe covers Low/Medium/High quality, distinct system and particle seeds, frequencies
0/.25/.5/2, damping, one/two/four octaves with different multipliers and scales, identity and
nonlinear remaps, TwoConstants/TwoCurves and separate axes, constant/changing/negative scroll,
loop boundaries, empty systems, module enable changes, restart and pause/resume, independent zero/constant/random/curved position/rotation/size amounts,
scalar and 3D particle modes, size/size-by-speed/rotation/velocity interactions, and transformed
World/Local/Custom spaces. It records base/animated/total velocities, position, lifetime, rotation,
angular velocity, public current size and rendered SizeXYZ. All six noise stream widths are
baked individually and in packed combinations. Additional cases add/remove streams during
simulation and round-trip particles through GetParticles/SetParticles.

Native measurements establish these details:

- Noise is a curl of analytic Perlin derivatives using Unity's fixed permutation and gradient
  tables. Low uses a 1D derivative with factor two; Medium and High permute distinct potentials.
  Octaves average frequency-scaled derivatives. The system seed supplies the field offset;
  particle seeds affect private curve interpolation, without touching script-global Random.
- Damping divides by base frequency. Remap evaluates at clamped raw/(4*frequency)+.5, uses its
  upper curve/constant, and scales its output by 2*frequency. Amounts do not modify one another.
- Scroll integrates speed at the end of each simulation substep, with wrapped time in loops.
  Only existing particles advance scroll; newborns sample the current scroll without advancing it.
  Empty systems skip scroll accumulation. Restart resets scroll.
- Rotation modifies stored angles without writing angular velocity. Rendered size multiplies
  the current size after other size modules. Public GetCurrentSize/GetCurrentSize3D exclude noise.
- NoiseSum is the integral of N*dt. NoiseImpulse is the last N. Both ignore positionAmount.
  Sum storage starts collecting when geometry first requests the stream and continues if it is
  later removed. Restart resets that request flag but preserves the allocated sum history;
  requesting the stream again resumes accumulation. External particle structs do not replace
  optional noise arrays.
- Manual Emit initializes particles without running incremental modules. Automatically emitted
  particles still evaluate those modules even when their elapsed birth substep is zero.

Native optional impulse arrays are uninitialized immediately after Emit, before the first
simulation update. Two retained preliminary Editor runs show heap values, NaNs and huge values
there. The fixture records the defined public birth state and requests storage with an initial
bake, discards those uninitialized mesh values, then records every requested attribute after
simulation has initialized it. This changes neither numeric tolerances nor defined samples.

## Reproduction

Run every build, Editor and engine invocation through the machine's shared heavy-job wrapper.
Keep the build outputs separate from other worktrees. Source the environment for the CLI path
and display, but do not use its obsolete WT variable.

```bash
source ~/.cache/compat/env.sh
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export UNBOUND_MANAGED_DIR=$HOME/.cache/compat/particle-noise-managed
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" \
  -p:UnboundTarget="$HOME/.cache/compat/particle-noise-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false

PARTICLE_NOISE_OUT=/tmp/noise-editor ~/.cache/compat/with-heavy-job \
  "$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity" -batchmode \
  -projectPath "$PWD/tests/projects/particlenoise" -executeMethod ProbeRunner.Record \
  -logFile /tmp/noise-editor.log
PARTICLE_NOISE_OUT=/tmp/noise-engine ~/.cache/compat/with-heavy-job "$U" run \
  -p "$PWD/tests/projects/particlenoise" --frames 8 --size 320x180 --no-watch
python3 tests/projects/particlenoise/compare.py tests/projects/particlenoise/Reference /tmp/noise-engine
```

The Editor needs DISPLAY/XAUTHORITY and GPU support for BakeMesh. Engine products belong in
Library/Unbound. No real Unity game project is modified. Integrated api-check belongs to the
parent checkout and was deliberately not invoked from this child worktree.

## Validation evidence

Evidence lives under ~/.cache/compat/ on the validation machine:

- Independent Native recordings: particle-noise-unity12 and particle-noise-unity13, 23,646/23,646.
- Earlier uninitialized storage evidence: particle-noise-unity2 and particle-noise-unity3.
- Reconstruction inputs: noise-analysis/findings.md, noise_math.py and tables.json, including
  read-only native instruction evidence. particle-age-analysis/findings.md explains native ages.
- Managed builds: particle-noise-build*.log, using this worktree's own managed output directories.
- ParticleSystemTests: particle-noise-tests-final.log, 26 passing and four inherited failures. They are
  emission boundary count, stopped state after Emit, total/base velocity cap semantics and old
  billboard geometry expectations. Parent independently reproduced them before this work.
- Existing stream regression: particle-noise-streams-final, 3544/3544 records and 3/3 byte-identical PNGs.

Final engine comparison: particle-noise-unbound7, 23,646/23,646 records at 2e-4 with no exclusions.
The broad regression in particle-noise-broad2.jsonl matches 1496/1560 at its original 1e-3
tolerance. All eight noise records now match, improving the parent's 1488/1560 and reducing
its nine differing scenarios to eight. The remaining scenarios are set_particles, custom_data,
sub_emitters_child, sub_emitters_birth_child, collision_world, simulate_long_call,
trigger_kill_outside and external_forces_rotation. Broad images in particle-noise-broad-images2
retain worst mean 0.93/255 and trail mean 0.49/255, identical to the previous summaries.
One intermediate launch, particle-noise-unbound6.log, failed before the probe started with a
Vulkan RequestDeviceError/Device(Lost). A single retry ran successfully, and the final
particle-noise-unbound7 run also completed normally. No device reset or other process was touched.
The final host build particle-noise-build7.log has zero errors. No Rust/native build or API
check was run by this delegate; the parent owns the integrated API check.

This fixture establishes
particle noise parity for the measured cases, not full engine parity. GetTrails/SetTrails,
texture baking and unrelated broad particle differences remain separate work.
