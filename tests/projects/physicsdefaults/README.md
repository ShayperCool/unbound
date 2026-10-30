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
