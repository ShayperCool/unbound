# Serialized 3D body loading

This stage starts at 7c0bde630f630c55181a31ac7563954323e774da and measures
Unity 6000.0.71f1 on Linux. It does not certify older releases or full engine parity.
Historical fixtures, references, accepted/rejected evidence and comparators are
read-only inputs. No game project is used.

## Native measurements

The CPU Editor creates the new authored scene and Resources prefabs through
Unity's creation, serialization and PrefabUtility APIs. The checked-in Assets,
Packages and ProjectSettings are that new fixture. Sources contains the exact
creation and recording scripts. Creation hashes are in Evidence/creation-manifest.json.

Each YAML order has two recordings from independently imported private projects
and separate Editor processes. Complete YAML documents are reordered without
changing component lists or references. All four captures are byte-identical,
with 4,656 records each and SHA256
`1b7f503b172bf15489d08ae76b8a6f3e29233ea78d6d27327f78247da5807e1b`.
All four preceded runtime changes. Native logs, frozen baseline captures and
every baseline difference are committed in Evidence/baseline.

The probe covers 192 bodies per recording, varying scene objects, nested scene
prefab instances with two levels of overrides, Resources prefab assets and
runtime instances; active/inactive, dynamic/kinematic, root/translated and rotated
child; and six first-read sequences. Those sequences are unchanged, Transform
edit, Transform edit plus Sync, physics setters, Transform edit then physics
setters, and Transform edit then activation plus Sync. Every authored snapshot
reads only Transform and GameObject before any physics property. All scene
pre-read Transform edits precede all scene Rigidbody property access. No-sync
scene reads precede the first explicit Sync. Later records retain setters,
Transform edits, Sync, Move/Publish, activation, simulation and deactivation.
Each Resources case has its own asset, so prior caller edits cannot contaminate
another first read. Defaults, type and initial linear/angular velocity are recorded.

Inactive scene bodies and prefab assets keep the completed serialized world pose
through later Transform edits and Sync. Active scene bodies keep their serialized
physics pose until Sync. Activation uses the current Transform. Nested overrides
and parent fields must finish before the inactive snapshot. Prefab assets never
join the scene physics world, even if activeSelf is true. Kinematic deactivation
also clears motion computed by the previous simulation step.

The frozen baseline passes 4,558 records in Transform-first order and 4,574 in
Rigidbody-first order. It fails 98 and 82 respectively. The exact union-key
comparison reports every failure without rounding tolerance, exclusions or masks.

## Reproduction

All large artifacts and caches are private to
`/mnt/storage-hdd/node-b-caches-20261023/inactive3dloading-t3-54da5671/`.
Its env.sh sources the shared environment for PATH and replaces obsolete paths.
The CLI, full baseline managed output and shim are copied from the immutable
inactive3d-native8 snapshot, never hardlinked. Every Editor, engine and dotnet
job takes the shared exclusive flock through with-heavy-job. This agent's cgroup
is capped at 30 GiB. Assembly-local and test-local native hashes are verified.

Set LOADING_WORK to a fresh private directory with the same environment layout.
Run `python3 tests/projects/inactive3dloading/run.py create`, then prepare and
record each combination of `--order transform-first|rigidbody-first` and
`--label native1|native2`. Recording uses `native`; baseline and candidate use
`engine` with explicit matching UNBOUND_MANAGED_DIR and LD_LIBRARY_PATH. The
runner refuses to overwrite an existing capture or log. `report.py --label LABEL`
archives a new report and refuses to overwrite old reports.

Build the host with the private environment:

```bash
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false
```

Keep UseSharedCompilation=false, MSBUILDDISABLENODEREUSE=1 and
DOTNET_CLI_USE_MSBUILD_SERVER=0 for tests too. The requested six-class integrated
filter is preserved. validate.py reuses the exact accepted parent engine commands
with fresh private outputs. verify.py compares the existing Native references and
accepted results and archives only into this new fixture. It checks all focused
controls, matrix, broad physics, 2D timing/order, joints, effectors, forces,
controller, defaults, queries, graphics, Canvas, messages and lifecycle. The parent
owns the final integrated API check.
