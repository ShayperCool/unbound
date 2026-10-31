# 2D trigger timing controls

Unity 6000.0.71f1 on Linux, manual Physics2D.Simulate. This continuation resolves
only the broad fixture's delayed trigger enter and exit. It does not certify full
engine or 2D physics parity. Original physics Sources/Reference and every prior
acceptance/rejection file remain unchanged.

Two independent Native processes agree byte for byte on each control suite:

| Suite | Cases | Observations per recording | SHA256 of either Native JSON |
| --- | ---: | ---: | --- |
| Initial controls | 37 | 13,926 | `830d285c2543e2b1f171b2ad15795587e23af3e54db5204b3c83148856d4fe52` |
| Expanded timing | 51 | 18,491 | `882a4fb07460a6ec24211615fe60ad6506f7a92a79d5607a16eab3e1961c25d5` |
| Mixed callback order | 10 | 5,720 | `8409ea6635fcae365a115137d5d1db1752ba3b72e4a0cbe7a958bad9fca25421` |

The expanded controls vary dt 0.01/0.02/0.04, speed 2/10, circle radius
0.1/0.25/0.7, polygon contact margin 0.001/0.01/0.1, sensor ownership, two sensors,
initial overlap, approaches and separation, dynamic/kinematic/static pairings,
sleep/wake, synchronized and unsynchronized transforms, collider/body/GameObject
lifetimes, fast crossings and a mixed solid/sensor layout. Every callback records
the current Simulate step and actual body pose/velocity. Before/after snapshots
include transforms, IsTouching and sleep state. Teardown callbacks are labelled
explicitly and remain part of comparisons.

Native evaluates sensors both before and after integration. At speed 10 and
dt 0.02, a circle starting at x=-1.4 enters a unit-halfwidth sensor in step 1,
with callback pose x=-1.2. The equivalent solid contact enters in step 2.
A sensor starting overlapped can enter and exit in the same step. A crossing that
starts and ends outside does not fabricate a swept trigger notification. Changing
margins changes the threshold independently of scheduling. The falling circle
with the original broad inputs enters at step 44 and exits at step 56.

The mixed layout exits its sensor, hits a solid wall, then solver correction
pushes it back into the sensor. Native reports trigger reentry at step 18 before
the wall's continuing contact callback. Ten further controls reverse creation
order and travel direction and start with both shapes overlapping. All show the
trigger notification phase before the solid-contact phase.

The runtime keeps the initial contact update, then updates only sensor contacts
after integration/TOI, using the actual shapes and existing broadphase pairs.
Solid manifolds, warm-start impulses and solver integration are not refreshed.
Callback dispatch preserves order within each kind and sends triggers before
solid contacts. No callback is synthesized or relabelled. No compatibility fork
is added: these measurements establish the current release, not a known
cross-release difference. Other releases remain unmeasured.

The first sensor-only candidate fixed the two broad steps but retained incorrect
mixed callback order, introducing 27 previously passing key failures in that
control. Its source patch, raw output and complete comparison are retained under
`Evidence/rejected/sensor-pass1/`. It was rejected despite passing 322 unit tests.
The follow-up Native order recordings preceded the phase-order implementation.

Final results, with unchanged references and the existing 1e-3 broad motion
comparison tolerance:

| Workload | Result |
| --- | --- |
| Expanded timing | 18,275/18,491 pass; 18,229 exact, 46 passing rounding, 216 retained failures |
| Mixed order | 5,560/5,720 pass; 5,556 exact, 4 passing rounding, 160 retained teardown failures |
| Broad physics | 23/29 scenarios pass; 11,495/11,626 values pass, 7,885 exact, 3,610 passing rounding; 131 failures |
| Scene settings | 5,055/5,055 pass; 4,803 exact, 252 passing rounding; all 21 outputs byte-identical to accepted SDK8 |
| Joint2D | 1,382/1,401 direct matches plus the existing 19 reported solver-rounding records |
| Effector2D | 2,531/2,559 direct matches plus the existing 28 measured contact-order alternatives |
| Forces | 608/608 pass |
| Focused managed 2D tests | 24/24 pass, zero skips |
| Requested integrated managed filter | 323/323 pass, zero skips |
| Existing comparator tests | Joint2D 11/11, Effector2D 8/8 |

Expanded timing improves from 17,388 passing Native observations and 1,103
failing Native observations, plus 48 surplus candidate observations, to the final
counts above. The union comparison therefore starts with 18,539 observations and
1,151 failures. It resolves 935 failing keys including those 48 surplus keys.
Mixed order improves from 5,152 passing and 568 failing observations, resolving 408.
No previously passing observation fails in either suite or broad physics.

The retained expanded failures comprise 112 teardown observations and 104
observations in the existing sleep/static-transform and disable/reactivation
controls. The mixed suite's 160 failures are teardown observations. Their raw
callbacks and state differences are included in `Evidence/summary.json`, along
with every passing nonexact numeric observation. These are limits, not expected
failure masks. The six remaining broad scenarios are 2d.bounce, 2d.stack,
2d.kinematic, 2d.queries, 2d.queries.options and 2d.casts.inside. All 13 broad 3D
scenarios, containing 3,047 observations, are identical to the private baseline.
Joint2D, Effector2D and forces JSONL outputs are byte-identical to that baseline.

`Evidence/accepted/artifacts.json` contains the complete private managed artifact
manifest. CLI is frozen native8, main ABI 5, PhysX shim 8, shader cache 13.
Final UnityEngine.CoreModule.dll SHA256 is
`2e32efb54997d633fc70ccda11c0b828c6c4dbf9222fb23b16dc23d594475e03`.
The copied shim SHA256 remains
`4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208`.
No Rust, native SDK/shim, generated stubs, Compat, 3D runtime or shared docs changed.

Private builds, projects, logs and caches live at
`/mnt/storage-hdd/node-b-caches-20261023/trigger2d-timing-cd4eff00/`.
Both Native pairs were recorded before implementing their respective behavior.
The initial suite also retains the existing first-world settings reset observed
in the baseline; expanded controls realize an empty world before applying their
settings. The first build attempt failed on read-only private copied artifacts;
its log is preserved. Only the private copies were made writable.

Reproduce from the repository root using a unique HDD work directory. The private
`env.sh` sources ~/.cache/compat/env.sh for PATH and immediately overrides all
output/cache variables. It never uses that file's obsolete WT. Copy native8's
full managed output to private managed-baseline/ and managed-final/ directories;
make only managed-final/ writable. Every Editor, engine, build and test process
uses ~/.cache/compat/with-heavy-job. The runner never changes existing authored
fixtures; private preparation copies the original broad inputs and settings.

```bash
source /mnt/storage-hdd/node-b-caches-20261023/trigger2d-timing-cd4eff00/env.sh
python3 tests/projects/trigger2dtiming/run.py prepare
python3 tests/projects/trigger2dtiming/run.py native --label expanded-native1
python3 tests/projects/trigger2dtiming/run.py native --label expanded-native2
python3 tests/projects/trigger2dtiming/run.py engine --label baseline
python3 tests/projects/trigger2dtiming/run.py prepare --probe order
python3 tests/projects/trigger2dtiming/run.py native --probe order --label native1
python3 tests/projects/trigger2dtiming/run.py native --probe order --label native2
python3 tests/projects/trigger2dtiming/run.py engine --probe order --label baseline
python3 tests/projects/trigger2dtiming/run.py regressions --label baseline
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$TRIGGER_WORK/managed-final" -p:UnboundTarget="$TRIGGER_WORK/dotnet/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false
python3 tests/projects/trigger2dtiming/run.py engine --label final
python3 tests/projects/trigger2dtiming/run.py engine --probe order --label final
python3 tests/projects/trigger2dtiming/run.py regressions --label final
UNBOUND_MANAGED_DIR="$TRIGGER_WORK/managed-final" LD_LIBRARY_PATH="$TRIGGER_WORK/managed-final" \
  python3 tests/projects/trigger2dtiming/matrix.py
python3 tests/projects/trigger2dtiming/report.py
```

Managed tests use private `UnboundTarget=$TRIGGER_WORK/test-build/` and
`UnboundNativeDir=$TRIGGER_WORK/managed-final`, with the same dotnet flags and
UseSharedCompilation=false, MSBUILDDISABLENODEREUSE=1,
DOTNET_CLI_USE_MSBUILD_SERVER=0. The integrated filter is the exact filter in the
assignment; its TRX and logs are preserved in Evidence/accepted. The parent owns
the final integrated API check and shared documentation. No real-game project
was read, run or modified.

ccc was initialized in this checkout. Its shared daemon did not return the
semantic refresh/search in the available attempts; the later refreshes each
exited 124 after 25 seconds. The private index cache is on HDD and timeout logs
are retained. Source review continued with exact rg searches and direct reads;
no shared daemon or sibling process was stopped.

## Parent integration

The parent merged this continuation with the accepted active-body sleep changes
on a separate integration branch. Its fresh combined managed build passes
332/332 integrated tests and API-check with zero errors in 2266 hand-written
types. Fresh expanded timing, mixed-order and broad physics captures contain
the exact same data as this delegate's accepted output. Joint2D, Effector2D and
forces remain byte-identical, with their existing reported rounding/alternatives.
The parent also reran graphics against Native, passing all 80 cases.
All 21 fresh 3D matrix files remain byte-identical to SDK8 acceptance, passing
5055/5055 observations, 4803 exact and 252 passing rounding. The parent's active
sleep and trigger controls remain exact at 20700, 1728, 1152, 1440 and 1848
records. The inactive-body boundary remains byte-identical at 318/1050, with
732 retained differences still owned by a separate 3D continuation.

Parent evidence, hashes and verification scope are in
`Evidence/ParentIntegration/summary.json`. The 21-profile 3D matrix replay uses
`Evidence/ParentIntegration/replay_matrix.py` with the same TRIGGER_WORK and
UNBOUND_MANAGED_DIR environment as above. It reuses matrix.py's exact inputs and
comparator, exporting only to the private work directory so the delegate's
historical evidence remains intact. The parent's code index refresh succeeded.

The accepted combined parent state `a9b130e0` is frozen locally at
`~/.cache/compat/parity-baseline-trigger2d-native8/`, including the ABI5 CLI,
complete managed output and shim8. File hashes are preserved in
`Evidence/ParentIntegration/baseline-manifest.json`. Use copies for further
experiments and leave this snapshot and all previous snapshots unchanged.
