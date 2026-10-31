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
