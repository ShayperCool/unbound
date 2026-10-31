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
All four preceded runtime changes. Each recording starts from a fresh private copy.
At Editor exit Unity saves the deliberate runtime edits to the 48 prefab-asset
cases in that recording's private copy. Evidence/native-post-state preserves those
post-run files. document-order-manifest.json verifies the ordered starting inputs,
all three engine stages and both matching Native post-states. Scene, nested and
instance-source files retain their requested document order. Native logs, frozen baseline captures and
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

## Loading results

The final runtime passes 4,656/4,656 exactly in each YAML order, resolving all 98
Transform-first and 82 Rigidbody-first baseline failures, with zero introduced or
retained failures. The snapshot-only candidate passes 4,614/4,656 in each order;
its remaining 42 kinematic deactivation records are preserved in Evidence/candidate1
along with its patch, build artifacts manifest and 369/369 test result. It was
never accepted as full certification. The final integrated filter passes 371/371
with zero skips, preserving 353 tests and adding 16 loading-order/first-read cases
and two dynamic/kinematic deactivation cases.

The loader snapshots the completed hierarchy after reading every component and
asset field, before creation callbacks or scene activation. Getter-triggered
capture is removed. Runtime AddComponent attachment and post-placement clone
snapshots remain intact. Kinematic deactivation discards computed target velocity;
dynamic deactivation retains its actual motion. No Compat, Instantiation,
Lifecycle, 2D, generated, Rust or native source changes are included.

## Accepted regression replay

Evidence/validation/summary.json and its accepted outputs contain the complete
fresh replay. Inactive 1,050, sequences 4,668, active 20,700 and clone-placement
172 remain exact. Long triggers 1,728, SDK flags 1,152, trigger pairs 1,440,
ignore lifecycle 1,848 and immediate setters 15 remain exact. Order 276,
component 276 and barrier 227 pass the unchanged comparator with unchanged data.
All 21 matrix files are byte-identical to acceptance, passing 5,055/5,055 with
4,803 exact and the original 252 passing motion-rounding records.

Graphics passes 80/80 on NVIDIA RTX 4060 Vulkan, Canvas passes 8,671/8,671 at
24 frames and 640x480, messages passes 316/316 with the existing clock checks,
and lifecycle is byte-identical to Native. The raw messages output retains all
335 rows, including the existing variable-clock rows checked by its comparator.
Defaults, joints, controller, ray-query and forces comparators pass. Joint2D
retains its 1,382 direct matches and 19 existing solver-rounding records;
effectors retains 2,531 direct matches and 28 measured contact-order alternatives.
Their raw outputs and forces are byte-identical to the parent acceptance.

Broad physics remains 23/29 scenarios and 11,495/11,626 values passing, 7,885
exact, with the same 131 failures in six existing 2D scenarios. Expanded timing
remains 18,275/18,491 and mixed order 5,560/5,720, retaining all 216 and 160
failures respectively. All three datasets equal the accepted parent data. Every
retained value, teardown observation and passing nonexact value remains in the
new report. There are zero newly failing accepted values or keys.

Main ABI5, PhysX shim8 and shader cache13 are unchanged. The assembly-local,
root-private and test-local native libraries all have SHA256
`4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208`.
The private CLI and the frozen native8 CLI used by the unchanged matrix runner
both have SHA256 `0a55f7d5fb6314deda371ee9c9334ec129652047925272d803d5cb86bb704b56`.
All six native6/native7/native8/sleep3d/trigger2d/inactive3d snapshot manifests
verify unchanged. Evidence/final/artifact-manifest.json hashes the complete
private managed outputs and all used native/CLI artifacts.

The measurements use unit scale and exact half-turn rotations. Arbitrary scale,
other rotations, further loading mechanisms and older releases remain unmeasured.
The parent's final integrated API check is still required. These results do not
claim full engine parity.

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

commands.sh records the exact final build, integrated test filter, focused replay,
full regression and verification commands. Build the host with the private environment:

```bash
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false
```

Keep UseSharedCompilation=false, MSBUILDDISABLENODEREUSE=1 and
DOTNET_CLI_USE_MSBUILD_SERVER=0 for tests too. The requested six-class integrated
filter is preserved. `validate-stage.sh` sets the private regression output paths
and runs validate.py. That driver reuses the exact accepted parent engine commands
with fresh private outputs. verify.py compares the existing Native references and
accepted results and archives only into this new fixture. It checks all focused
controls, matrix, broad physics, 2D timing/order, joints, effectors, forces,
controller, defaults, queries, graphics, Canvas, messages and lifecycle. The parent
owns the final integrated API check.

The ccc project was initialized here. The initial 40-second semantic refresh, a
subsequent 25-second refresh and the final 25-second index request timed out.
Logs are retained; source review used rg and direct reads. No shared daemon or
sibling process was stopped.

## Parent integration

The parent independently reviewed and rebuilt the merged implementation, then
replayed both document orders from fresh copies of the committed authored
inputs. Both captures pass 4656/4656 exactly against the agreeing Native pairs,
9312/9312 total, with no missing or extra keys. The parent verified the authored,
Native, source, delegate-artifact and regression hashes before integration.

The fresh parent replay also passes 371/371 tests, API-check with zero errors in
2266 hand-written types, all 5055 matrix observations and every existing focused
control. All 21 matrix files remain byte-identical to acceptance. Graphics80,
Canvas8671 and messages316 pass, lifecycle is Native-byte-identical, and all
existing broad/2D regression results are preserved without new failing values.

Complete fresh parent captures, comparison logs, exact counts and artifact/source
hashes are in `Evidence/ParentIntegration/summary.json`. Its `verify.py` writes
only this new parent archive; historical delegate evidence remains intact.

The accepted parent runtime is also frozen locally at
`~/.cache/compat/parity-baseline-loading3d-native8/`, containing the matching ABI5
CLI, full managed output and shim8. All file hashes and the source commit are in
`Evidence/ParentIntegration/baseline-manifest.json`. Copy it for subsequent
experiments; keep this and every earlier immutable baseline unchanged.
