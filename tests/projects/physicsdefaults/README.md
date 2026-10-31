# Project physics defaults probe

This fixture measures Unity 6000.0.71f1 directly, using its native PhysX backend in
Editor Play mode. It never opens the real game project. `ProbeRunner.Create`
authors the scene and prefab through Unity APIs. Project DynamicsManager values
are intentionally distinct from stock defaults: position iterations 11, velocity
iterations 4, sleep threshold 0.03125, angular speed 13, depenetration speed 3.25,
and contact offset 0.025.

The 389 JSONL records cover static settings, new components, active and inactive
scene imports, prefab loading, instantiation, lazy native allocation, activation,
reactivation, runtime overrides, individual global changes, and existing versus
future bodies. Invalid assignments cover both allocated and inactive bodies,
negative and zero values, iteration limits 255/256, integer extremes, NaN and
infinities. Each invalid assignment starts from restored project globals. Motion
cases exercise angular clamping, supported-body sleeping, penetration recovery,
and a tilted box solved over five steps.

Two separate Editor processes produced `Reference/unity-6000.0.71f1.jsonl` and
`Evidence/native-repeat.jsonl` before runtime changes. All 389 records agree.
The comparator checks every key and rejects missing/extra records. Field tolerance
is 1e-6 relative to max(1, |reference|); motion retains the broad physics fixture's
original 1e-3 tolerance. Solver motion differences are counted separately and
remain failures. No records are excluded.

Measured behavior:

- Components snapshot globals at creation, even while inactive. Allocation and
  reactivation retain that snapshot. Globals do not retune existing components.
- These runtime tuning fields are absent from Unity-authored scene/prefab YAML.
  Loading and cloning use current defaults, including cloning an overridden body.
  The authoring script deliberately sets different runtime values before saving.
- Global iteration setters clamp to 1..255. Body iteration setters clamp the
  absolute magnitude to 1..255, including int.MinValue becoming 255.
- Body angular and linear speed setters take absolute values. Global angular
  speed stores negative values; new bodies take their absolute value.
- Global and body sleep thresholds preserve assigned values, including negative
  values and NaN. Body depenetration preserves them too. Global depenetration and
  contact offset reject values <= 0 but accept NaN and positive infinity.
- A changed global affects future bodies only. Angular and depenetration motion
  confirm this, rather than relying only on managed field readback.

Commands, run from this worktree root, one heavy process at a time:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
fixture="$PWD/tests/projects/physicsdefaults"
editor="$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity"
heavy="$HOME/.cache/compat/with-heavy-job"

"$heavy" timeout 240 "$editor" -batchmode -nographics -projectPath "$fixture" \
  -executeMethod ProbeRunner.Create -logFile /tmp/physicsdefaults-create.log
PHYSICS_DEFAULTS_OUT="$fixture/Reference/unity-6000.0.71f1.jsonl" \
  "$heavy" timeout 180 "$editor" -batchmode -nographics -projectPath "$fixture" \
  -executeMethod ProbeRunner.Record -logFile /tmp/physicsdefaults-native1.log
PHYSICS_DEFAULTS_OUT="$fixture/Evidence/native-repeat.jsonl" \
  "$heavy" timeout 180 "$editor" -batchmode -nographics -projectPath "$fixture" \
  -executeMethod ProbeRunner.Record -logFile /tmp/physicsdefaults-native2.log
python3 "$fixture/compare.py" "$fixture/Reference/unity-6000.0.71f1.jsonl" \
  "$fixture/Evidence/native-repeat.jsonl"

"$heavy" timeout 300 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release \
  -o "$PWD/target/physicsdefaults-managed" -p:UnboundTarget="$PWD/target/physicsdefaults-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false
export UNBOUND_MANAGED_DIR="$PWD/target/physicsdefaults-managed"
export LD_LIBRARY_PATH="/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
cli=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound
PHYSICS_DEFAULTS_OUT="$fixture/Evidence/unbound.jsonl" \
  "$heavy" timeout 180 "$cli" run -p "$fixture" --headless 600
python3 "$fixture/compare.py" "$fixture/Reference/unity-6000.0.71f1.jsonl" \
  "$fixture/Evidence/unbound.jsonl"
```

The parent CLI is reused with this worktree's managed output and unchanged native
libraries through LD_LIBRARY_PATH. No child invocation of gen-stubs or api-check
is used. Runtime caches belong in each fixture's Library/Unbound directory.

Before the fix, the parent source at 49407b61 with a fresh child-managed build
matched 76/389 records. The 313 failing records include five solver motion
records. The complete recording and comparison are retained as
`Evidence/unbound-before.jsonl` and `Evidence/unbound-before-comparison.txt`.
The two Native recordings are exactly equal by key and value; enumeration order
of the two scene bodies differs between Editor processes.

After the fix, `Evidence/unbound.jsonl` matches 388/389 records. Of those, 382
are exactly equal by parsed key/value and six solver-motion records have rounding
up to 6.15e-08 absolute. They pass the original 1e-3 motion tolerance. The comparator
reports those six separately, with zero solver failures outside tolerance.
The only failing record is `global-5/contact-old`: an existing collider returns
0.05 after the global changes, while Native retains its creation offset 0.025.
`Collider.cs` is outside this task's exclusive ownership. Fixing it requires
snapshotting collider defaults independently of lazy shape allocation and
activation, rather than reading the mutable global in its getter. This failure
is retained in the fixture and the comparator still exits 1. It is not a
Rigidbody-default failure and no native setter or ABI extension is needed for
the implemented Rigidbody changes.

The runtime fix snapshots project values in the Rigidbody constructor, which is
used for imported, inactive, fresh and cloned components. It does not wait for
PhysicsWorld.CreateBody. Clone copying leaves the new snapshot intact; later
ApplyTuning calls and native body recreation keep that component's values.
Setter fixes implement the measured global clamp, body magnitude/clamp, and
nonpositive global rejection rules. PhysicsWorld and native code are unchanged.

Regression results are retained in `Evidence/regressions.txt`:

- Managed PhysicsTests and Physics2DTests: 33/33 passed, none skipped.
- Broad physics: 21/29 scenarios match, retaining eight existing differences.
  The differing scenarios are 2d.bounce, 2d.stack, 2d.kinematic, 2d.triggers,
  2d.queries, 2d.queries.options, 2d.casts.inside, and 3d.stack. The 3D stack
  retains 85/292 differing values at the unchanged 1e-3 tolerance. This solver
  drift and Euler-angle wrapping are separate from project-default readback.
- 3D joints: 26/27 records match, plus the existing hinge-limit settling-force
  difference, three values with a worst difference of 41.73. No unexpected failures.
- CharacterController: 23/23 records match.
- ConstantForce: 608/608 records match. Duplicate-component errors in its engine
  log are intentionally exercised by that fixture.

Exact regression commands, after exporting the same environment as above:

```bash
mkdir -p target/physicsdefaults-results/{joints,controller,forces}
"$heavy" timeout 300 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$PWD/target/physicsdefaults-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~UnityEngine.Tests.PhysicsTests|FullyQualifiedName~UnityEngine.Tests.Physics2DTests' \
  --logger 'trx;LogFileName=physics-regressions.trx' \
  --results-directory "$PWD/target/physicsdefaults-results"
PHYSICS_PROBE_OUT="$PWD/target/physicsdefaults-results/physics.json" \
  "$heavy" timeout 180 "$cli" run -p "$PWD/tests/projects/physics" --headless 600 --no-watch
JOINTS_PROBE_OUT="$PWD/target/physicsdefaults-results/joints" \
  "$heavy" timeout 180 "$cli" run -p "$PWD/tests/projects/joints" --headless 300000 --no-watch
CONTROLLER_PROBE_OUT="$PWD/target/physicsdefaults-results/controller" \
  "$heavy" timeout 180 "$cli" run -p "$PWD/tests/projects/controller" --headless 300000 --no-watch
FORCES_PROBE_OUT="$PWD/target/physicsdefaults-results/forces" \
  "$heavy" timeout 180 "$cli" run -p "$PWD/tests/projects/forces" --headless 30000 --no-watch
python3 tests/projects/physics/compare.py tests/projects/physics/Reference/unity-6000.0.71f1.json \
  target/physicsdefaults-results/physics.json
python3 tests/projects/joints/compare.py tests/projects/joints/Reference target/physicsdefaults-results/joints
python3 tests/projects/controller/compare.py tests/projects/controller/Reference target/physicsdefaults-results/controller
python3 tests/projects/forces/compare.py tests/projects/forces/Reference target/physicsdefaults-results/forces
```

Limits: this evidence covers the installed 6000.0.71f1 release, not older release
families. NaN, infinity and invalid assignments test stored API values on both
allocated and inactive bodies; subsequent simulation with invalid settings and
exact error diagnostics are unmeasured. Project YAML contains valid custom
settings, not malformed settings assets. The maxLinearVelocity tests establish
readback, sign handling and clone resets, not its existing post-step clamping
algorithm. Collider contact-default lifetime, other releases, and the inherited
solver/contact differences remain separate work. This is not full engine parity.

## Collider follow-up oracle

The parent extends the reference to 582 records. The original 389 Native values
are unchanged. `Evidence/native-collider-repeat.jsonl` is an independent Editor
recording and agrees exactly by key/value with the complete new reference.
`Evidence/native-repeat.jsonl` retains the earlier 389-record Native reference.

The additional 193 records cover BoxCollider, SphereCollider, CapsuleCollider,
CharacterController, empty MeshCollider/TerrainCollider, and those two types with
valid geometry. They measure creation-time defaults, allocation, global changes,
inactive objects and parents, disabled components, reactivation, clone resets,
prefab lifetime, nonpositive/nonfinite values, and the smallest positive float.
The authoring runner also assigns runtime contact offsets before saving; Unity
omits them from scene/prefab YAML, just like Rigidbody runtime tuning.

Native retains the creation offset through global changes and activation. Clones
use current globals. A setter changes the value only while a physics shape
exists; inactive, disabled and geometry-less colliders ignore the assignment.
Valid overrides survive deactivation. Nonpositive values are rejected; active
colliders retain NaN and positive infinity in API readback. Invalid-value
simulation and exact diagnostic text remain unmeasured.

A moving-box case checks live contact generation, not just API readback.
Increasing the dynamic box's offset creates four positive-separation contacts;
decreasing it ends that pair, restoring it enters again, and rejected zero/negative
assignments preserve subsequent stay callbacks. Every phase records events,
contact count/separation, position, velocity and the current offset.

Repeat the Native recording command above with
`PHYSICS_DEFAULTS_OUT="$fixture/Evidence/native-collider-repeat.jsonl"`, then
compare that file with the full reference using the same unchanged comparator.
