# DynamicsManager scene settings

This fixture uses real Unity 6000.0.71f1 Editor Play mode with its Native PhysX
backend, with no game project involved. `ProbeRunner.Create` authors the scene
through Unity APIs. `run.py prepare` copies that authored fixture into independent
private projects and makes explicit Unity-compatible YAML edits before either
runtime starts. Each profile uses a separate process and project Library.

The profiles cover PGS and TGS with each of patch, one-directional and
two-directional friction, four contact-pair modes, enhanced determinism under
both solvers, improved patch friction, and an injected legacy `m_EnablePCM: 0`.
Every profile has 227 observations. There are no images in this CPU fixture.
The motion cases are a diagonal sliding box, tilted box, two-body stack and fixed
joint, each observed for twelve 0.02-second steps. Pair cases cover kinematic /
kinematic, kinematic / static, kinematic / dynamic and static / static pairs,
contacts and triggers, enter / persist / exit, and fresh / activated / cloned
bodies. Imported active/inactive bodies and their clones also record body and
collider defaults before the simulation cases.

Two independent Editor recordings per profile precede runtime edits.
`Reference/` contains the first recording; `Evidence/native1/` and
`Evidence/native2/` retain both recordings and the Editor's serialized settings
property lists. `Profiles/` retains exact authored input settings. `Evidence/sha256.json`
records input and output hashes. `Evidence/baseline/` uses managed sources from
84880006, freshly built in private output, the frozen main-ABI-5 CLI and frozen
PhysX-shim-6 library. `Evidence/final/` uses the frozen main-ABI-5 CLI with the matching private native
shim and managed output built from this branch. The brief explicitly permits
that combination when the main protocol is unchanged. No shared mutable CLI or native
library is used.

`compare.py` retains the existing physics tolerances: 1e-3 relative to
max(1, |reference|) for motion, 1e-6 for fields, events and counts. Missing,
extra and duplicate keys fail. There are no exclusions or expected-failure
masks. Each phase's comparison text retains all failing records.
`*-all-differences.json` additionally retains every non-exact record, including
rounding that passes tolerance. Full original and candidate arrays remain in
the JSONL files. `Evidence/summary.json` reports exact versus non-exact counts
separately from comparator exit status.

Commands are run from this worktree root. All Unity, dotnet, cargo and engine
processes go through the same exclusive heavy-job lock. `SCENE_WORK` is a unique
private cache path, backed by a directory on the data volume to avoid filling
the root filesystem. Set `UnboundTarget` on every dotnet command. The fixture's
runtime caches stay under the private project's Library/Unbound.

```bash
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export SCENE_WORK="$HOME/.cache/compat/physicsscenesettings-e1acb38a"
export CARGO_TARGET_DIR="$SCENE_WORK/cargo" CARGO_BUILD_JOBS=2
export DISPLAY=:0
export XAUTHORITY=$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)
heavy="$HOME/.cache/compat/with-heavy-job"
fixture="$PWD/tests/projects/physicsscenesettings"
editor="$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity"

# Before runtime edits, build 84880006's managed host independently.
"$heavy" timeout 360 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release \
  -o "$SCENE_WORK/baseline-managed" -p:UnboundTarget="$SCENE_WORK/baseline-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  > "$SCENE_WORK/baseline-build.log" 2>&1
"$heavy" timeout 240 "$editor" -batchmode -nographics -projectPath "$fixture" \
  -executeMethod ProbeRunner.Create -logFile "$SCENE_WORK/create.log"
python3 "$fixture/run.py" prepare
python3 "$fixture/run.py" native > "$SCENE_WORK/native-driver.log" 2>&1
export UNBOUND_MANAGED_DIR="$SCENE_WORK/baseline-managed"
export LD_LIBRARY_PATH="$HOME/.cache/compat/parity-baseline-native6"
python3 "$fixture/run.py" baseline > "$SCENE_WORK/baseline-driver.log" 2>&1
python3 "$fixture/regressions.py" baseline > "$SCENE_WORK/regressions-baseline-driver.log" 2>&1
```

`run.py native` runs TWO separate Editor processes for EACH profile, using
`SCENE_SETTINGS_OUT=$SCENE_WORK/native{1,2}/PROFILE.jsonl`, `-batchmode -nographics`,
`-executeMethod ProbeRunner.Record` and a private `-logFile`. Each invocation is
wrapped in `with-heavy-job timeout 240`. Baseline/final use the same environment
variable and `run -p PRIVATE_PROJECT --headless 30000 --no-watch` under that lock.
Each complete case writes its output before quitting. Build, Editor and engine
logs remain under the unique private cache directory, alongside the original
recordings. Unrelated licensing/network curl warnings are retained in Editor logs.

Runtime builds and final captures, after the two Native runs have completed:

```bash
UNBOUND_SKIP_MANAGED_BUILD=1 "$heavy" timeout 1200 cargo build --release -p unbound-host -j2 \
  > "$SCENE_WORK/native-build.log" 2>&1
"$heavy" timeout 360 dotnet build managed/Unbound.Host/Unbound.Host.csproj -c Release \
  -o "$CARGO_TARGET_DIR/managed" -p:UnboundTarget="$SCENE_WORK/final-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  > "$SCENE_WORK/final-build.log" 2>&1
export UNBOUND_MANAGED_DIR="$CARGO_TARGET_DIR/managed"
export LD_LIBRARY_PATH="$CARGO_TARGET_DIR/managed"
export SCENE_SETTINGS_CLI="$HOME/.cache/compat/parity-baseline-native6/unbound"
python3 "$fixture/run.py" final > "$SCENE_WORK/final-driver.log" 2>&1
python3 "$fixture/contactpairs.py" final > "$SCENE_WORK/contacts-final-driver.log" 2>&1
python3 "$fixture/contactpairs.py" final --noise > "$SCENE_WORK/islands-final-driver.log" 2>&1
python3 "$fixture/cache.py" > "$SCENE_WORK/cache-final-driver.log" 2>&1
python3 "$fixture/regressions.py" final > "$SCENE_WORK/regressions-final-driver.log" 2>&1
"$heavy" timeout 300 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj -c Release \
  -p:UnboundTarget="$SCENE_WORK/test-build/" -p:UnboundNativeDir="$CARGO_TARGET_DIR/managed" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~UnityEngine.Tests.PhysicsTests|FullyQualifiedName~UnityEngine.Tests.Physics2DTests' \
  --logger 'trx;LogFileName=physics.trx' --results-directory "$SCENE_WORK/tests" \
  > "$SCENE_WORK/tests.log" 2>&1
python3 "$fixture/report.py" --complete > "$SCENE_WORK/report.json"
```

The frozen executable does not embed the PhysX library; managed library loading
finds the matching private library beside its assembly. No native library is
copied into or overwritten in the read-only snapshot. `cargo build -p unbound-host`
builds the PhysX SDK and shim in the private target without rebuilding the
unchanged Bevy executable. No api-check or gen-stubs command is run by this delegate.

Measured boundaries and limitations will be recorded below with final results.

The contact supplement uses `contactpairs.py native` for two independent Editor
runs of each of the four pair profiles, and `contactpairs.py baseline` / `final`
for the engine. It sets `SCENE_SETTINGS_QUERY_CONTACTS=1` and retains every
original record plus 72 raw-contact-count records, 299 observations per profile.
In that supplement, non-trigger kinematic/kinematic and kinematic/static cases
move one kinematic actor by 0.01 units per step with `MovePosition` so that the
pair is active. The original 227-record suite remains unchanged. Native counts
come from `Physics.ContactEvent`; engine counts inspect
`PhysicsWorld.CollectContacts` through reflection. This is a backend contact
measurement, not an implementation of the public ContactEvent API, which
remains a stub. No callbacks are fabricated to satisfy the comparison.

`contactpairs.py native --noise` also records default/determinism and TGS/TGS +
determinism twice with 32 distant, non-interacting dynamic actors inserted before
each motion scenario. `baseline --noise` and `final --noise` repeat that workload
in the engine. These 227-record runs live under `islands-*`; their motion records
can also be compared with the original profile to measure island insertion's
effect separately from process repeatability. This does not establish determinism
for arbitrary insertion orders, parallel workers or long simulations.

`python3 tests/projects/physicsscenesettings/test_compare.py` runs six comparator
checks. They reject missing/extra/duplicate records and NaN, and preserve both the
original 1e-3 motion boundary and stricter 1e-6 field boundary.

`cache.py` reuses one private project's populated Library, changes only
`m_FrictionType` from 0 to 1, then restores 0 in a `finally`-protected sequence.
Its three complete recordings compare with the corresponding original Native
profiles. Settings documents are read directly through `DocumentLoader.load`;
no new settings cache format or version is introduced by this work.

## Native observations

Before the first runtime edit, all **5,055/5,055 observations agreed exactly**
between two independent real Editor recordings across 21 profile/workload
combinations. There are thirteen 227-record profiles, four 299-record contact
supplements, and four 227-record island supplements. No Native variation was
filtered or normalized. The first runtime edit is commit `cf233dd2`, after the
complete Native/baseline evidence commit `6ab9c113`.

The Editor's own enum names establish the serialized values:

| Field | Values |
| --- | --- |
| `m_SolverType` | 0 ProjectedGaussSeidel, 1 TemporalGaussSeidel |
| `m_FrictionType` | 0 PatchFrictionType, 1 OneDirectionalFrictionType, 2 TwoDirectionalFrictionType |
| `m_ContactPairsMode` | 0 DefaultContactPairs, 1 EnableKinematicKinematicPairs, 2 EnableKinematicStaticPairs, 3 EnableAllContactPairs |
| `m_EnableEnhancedDeterminism` | Boolean |
| `m_ImprovedPatchFriction` | Boolean |

PGS patch/one/two friction modes produce different motion in this workload.
TGS patch/one/two produce exactly equal records. These are accepted Native
combinations, not invalid input cases. The implementation passes the selected
mode to PhysX itself; it does not approximate TGS with PGS iteration changes.

Pair settings affect real contact generation and callbacks. In the original
suite, fresh sleeping kinematic actors generate no contact pair, while activated
and cloned kinematics generate four contacts and enter/stay/exit callbacks when
the corresponding pair mode is enabled. The contact supplement moves one
kinematic actor and confirms four contacts plus callbacks for fresh bodies too.
The disabled mode generates none. Static/static pairs remain absent. Trigger
participation is independent of these contact-mode bits.

Unity 6000.0.71f1 exposes **no `m_EnablePCM` serialized property**. Injecting
`m_EnablePCM: 0` leaves all 227 Native records exactly equal to default, and the
field is absent from both serialized property lists. The compatibility profile
therefore keeps PCM enabled in this release even with that legacy field present.
Pre-6000 documents with an explicit legacy field pass it through to the real
PhysX PCM flag; that older release path has documentation support but no Native
oracle in this fixture. This does not claim a measured SAT/off combination in
Unity 6000.0.71f1. The installed reference-source inspector also omits a PCM field:
[Unity 6000.0 inspector source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Modules/PhysicsEditor/PhysicsManagerInspector.cs).
The earlier selectable PCM setting is described in
[Unity 2017.3's manual](https://docs.unity3d.com/cn/2017.3/Manual/class-PhysicsManager.html).

All four motion scenarios retain their Native results when unrelated islands
are present, for both determinism settings and both solvers. Later kinematic /
dynamic contact cases, after those islands have been destroyed, differ from the
original Native workload: eight records under PGS and six under TGS. Those
include motion and, under PGS, two contact-count records. The two processes
repeat each workload exactly. This is a creation/contact-order history limit,
not evidence of arbitrary order invariance. Both original and island records
remain in the accounting. The unchanged engine baseline also has eight extra
PGS island-workload failures versus its original workload. None are excused by
changing the 1e-3 tolerance.

The supplemental fixture initially failed compilation because this installed
Editor uses `Physics.ContactEventDelegate` with `NativeArray<ContactPairHeader>.ReadOnly`.
Those failed attempt logs remain under the private cache's
`attempts-pre-reference/`. They produced no recordings and preceded the two
successful reference runs. The engine also defines `UNITY_EDITOR` when compiling
game code, so the backend adapter identifies the actual native PhysicsModule
assembly rather than using that define to select its raw-contact reader.


## Final results

**4,111/5,055 baseline records match; 4,782/5,055 final records match.** The final
recording has 4,530 exact records and 252 additional records within the unchanged
tolerances. **273 failures remain**, and every final comparator still exits
nonzero. There are no expected-failure masks. Each of these CPU profiles has zero
images. The per-profile counts below include every record.

| Profile/workload | Records | Baseline match | Final match | Final exact |
| --- | ---: | ---: | ---: | ---: |
| default | 227 | 212 | 216 | 204 |
| tgs | 227 | 155 | 216 | 204 |
| one | 227 | 155 | 216 | 204 |
| two | 227 | 155 | 216 | 204 |
| tgs-one | 227 | 155 | 216 | 204 |
| tgs-two | 227 | 155 | 216 | 204 |
| pairs-kk | 227 | 202 | 216 | 204 |
| pairs-ks | 227 | 202 | 216 | 204 |
| pairs-all | 227 | 192 | 216 | 204 |
| determinism | 227 | 212 | 216 | 204 |
| tgs-det | 227 | 155 | 216 | 204 |
| improved | 227 | 179 | 216 | 204 |
| pcm-off | 227 | 212 | 216 | 204 |
| contacts-default | 299 | 284 | 288 | 276 |
| contacts-pairs-kk | 299 | 263 | 288 | 276 |
| contacts-pairs-ks | 299 | 263 | 288 | 276 |
| contacts-pairs-all | 299 | 242 | 288 | 276 |
| islands-default | 227 | 204 | 204 | 192 |
| islands-determinism | 227 | 204 | 204 | 192 |
| islands-tgs | 227 | 155 | 207 | 195 |
| islands-tgs-det | 227 | 155 | 207 | 195 |

All original thirteen profiles have zero final motion failures. Their eleven
remaining failures per profile are trigger-stay delivery: Native sends stay in
the enter step, and fresh sleeping kinematics also retain a stay on the next
step in Native. These predate the scene-settings implementation. Correct
kinematic activation/cloning resolves four other trigger-stay records per
original profile as a consequence of creating those bodies with their actual
initial type. The contact supplements have the same eleven trigger failures;
all their non-trigger callback and generated-contact counts now match.

The island workloads retain contact-order failures as well. PGS has 23 failures
per profile: eleven trigger records, nine kinematic/dynamic motion records and
three contact-count records. TGS has 20 per profile: eleven trigger records and
nine kinematic/dynamic motion records. The four core motion scenarios remain
within the original tolerance in every workload.

**Eight failures are newly introduced versus the starting engine**, not
inherited: PGS `islands-default` and `islands-determinism` each change
`pair/kd/False/clone/{0,1,2}/motion` and `pair/kd/False/clone/1/contacts` from passing
to failing. The body-registration correction changes native actor creation
history. The cloned pair now takes the alternate contact-order outcome already
seen in the independent Native fresh/activated island pairs, rather than the
Native cloned pair's outcome. At step 1, for example, Native clone has four
contacts, while final clone has three, exactly like Native's fresh/activated
island pair. Final clone's velocity is `[0.648613632,-0.3294555,-0.2629254]` instead
of Native clone's `[0.8527438,-0.115501478,-0.13520436]`. These remain failures.
There is no actor-order workaround or change to the vendored solver.

`Evidence/failure-transitions.json` retains the reference, baseline and final
arrays for all **679 resolved**, **8 introduced**, and **18 retained failures
whose values changed**. The latter are the TGS island pair motion cases under
the actual TGS solver, which the baseline did not select. The net reduction is
671 failing records. Do not describe all 273 failures as inherited or claim full
physics parity from this fixture. Arbitrary allocation/actor creation history,
long stacks, parallel-worker determinism and older Unity releases remain outside
the measured guarantees. Runtime improved-friction toggling is implemented via
cached PhysX material flags but is not separately measured by this project-flag
oracle.

The native shim is **7**, with a six-int/24-byte scene definition. Main engine
ABI/protocol **5** and shader cache **13** are unchanged. The ABI probe creates
and destroys all 48 valid solver/friction/pair/determinism combinations, and
rejects three invalid enums with a clear DynamicsManager diagnostic. The stale
library test loads the frozen shim 6 against the new binding and records the
expected version-6/expected-7 error before the new scene-definition call.
`Evidence/build-artifacts.json` retains snapshot and private artifact hashes;
the snapshot hashes were rechecked unchanged.

## Existing regressions

The baseline and final data files of all six existing fixtures are byte-for-byte
identical, as checked in `Evidence/regression-byte-comparison.json`. No inherited
failure value changes in those fixtures. Full recordings, engine logs and
comparison text are under `Evidence/regressions-baseline/` and
`Evidence/regressions-final/`.

- physicsdefaults: **582/582**, 576 exact and the same six inherited solver
  rounding records, maximum 6.15e-8 absolute.
- PhysicsTests + Physics2DTests: **33/33**, no skips. The native library copied
  to the test output is explicitly selected through `UnboundNativeDir`.
- Broad physics: **21/29**, the same eight differences: 2d.bounce, 2d.stack,
  2d.kinematic, 2d.triggers, 2d.queries, 2d.queries.options, 2d.casts.inside and
  3d.stack. The stack retains 85/292 differing values at 1e-3.
- Joints: **26/27**, retaining the hinge-limit settling-force difference,
  three values and worst 41.73. No unexpected failure.
- Controller: **23/23**.
- ConstantForce: **608/608**.
- RaycastHit: **3,203/3,203**.

The cached-project sequence retains the same eleven trigger failures in each
227-record run, while all motion records match default, one-directional and
restored-default Native respectively. Default and restored output agree exactly.
`Evidence/cache-final/` contains all three recordings, comparator failures and
engine logs, including reuse of cached game assemblies. The setting is reread
without recompiling the game or changing a cache format.

Additional commands used for guard/evidence validation:

```bash
"$heavy" timeout 120 python3 "$fixture/abi.py" > "$SCENE_WORK/abi.log" 2>&1
# Copy final managed files without libunbound_physx into SCENE_WORK/stale-managed first.
UNBOUND_MANAGED_DIR="$SCENE_WORK/stale-managed" \
  LD_LIBRARY_PATH="$HOME/.cache/compat/parity-baseline-native6" \
  "$heavy" timeout 60 "$SCENE_SETTINGS_CLI" run -p "$SCENE_WORK/projects/default" \
  --headless 2 --no-watch > "$SCENE_WORK/stale-version.log" 2>&1
rg -n 'version 6.*expects 7' "$SCENE_WORK/stale-version.log"
python3 "$fixture/test_compare.py"
ccc search --refresh --path 'native/unbound_physx.cpp' 'scene configuration solver friction kinematic pair modes'
```

No authored game files, generated stubs, shared progress documents, parent build
outputs, preserved backup branches or snapshot artifacts were changed. This
branch contains no PR or merge. Parent review and integrated API-check remain
with the parent.
