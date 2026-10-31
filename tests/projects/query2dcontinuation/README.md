# Query continuation

The starting diagnostic commit is 29c7291dde2a85872617ff4bdfb551fe8a67eca5.
The accepted branch remains at 88a53c477dc43e646c21522f6c402c0cec98c6c1.
This work does not integrate the other owners' fixes or establish acceptance.

Two fresh Unity 6000.0.71f1 Linux CPU processes agree byte for byte on all
6,256 observations in 336 initial-overlap controls. They vary four target shapes,
three query shapes, fourteen gaps, both queriesStartInColliders values, zero and
short ranges, and casts toward and away from the target. The Native recordings
precede implementation changes. The new controls use the historical broad
fixture's unmodified ProbeRunner and ProbeLog in private copies.

Native distinguishes the initial query TOI threshold from surface overlap.
For example, a circle penetrating an unrounded box by 0.001 travels about 0.004
before reporting a hit, regardless of queriesStartInColliders. A penetration of
0.004 instead produces an initial hit when that flag is true and no hit when
false. Short ranges expose the existing root tolerance, including midpoint hits.
A zero range and a direction away from a shallow overlap both miss.

All generated projects, builds and caches are private:
`/mnt/storage-hdd/node-b-caches-20261023/query2d-continuation-t3-06ecf6ba/`.
Use its env.sh, which sources the shared PATH/DISPLAY setup and unsets WT.
Every Unity, dotnet and engine process takes the global heavy-job lock.

```bash
source /mnt/storage-hdd/node-b-caches-20261023/query2d-continuation-t3-06ecf6ba/env.sh
python3 tests/projects/query2dcontinuation/run.py prepare --label NEW_LABEL
python3 tests/projects/query2dcontinuation/run.py native --label NEW_LABEL
```

Labels and project copies are write-once. A second fresh label is required for
independent Native agreement. Existing query2dparity and colliderskin2d fixtures,
recordings and checkers remain unchanged. Older Unity releases are unmeasured.

## Implementation results

The query code now classifies initial overlap at the swept query's target and
root tolerance, while still requiring actual surface overlap. Shallow overlap
can therefore continue to a positive-distance hit. Initial contacts with equal
depth use the midpoint of the two manifold points. Circle/circle initial contacts
put the query shape first, which determines Native's coincident-center fallback
orientation without changing noncoincident geometry. These are query-only changes;
shared GJK, TOI, manifolds, solver settings and collider construction are unchanged.

The new Native controls match 6,256/6,256 observations, 5,790 exactly. The 2,880
resolved failing observations include all missing and extra keys in the union.
The threshold-only attempt retained 152 contact-point failures and failed one
new unit assertion. Its source, capture and failed TRX remain in Evidence. The
midpoint correction resolves all 152 without weakening that assertion.

Every existing suite is replayed using private copies of both the actual frozen
accepted runtime and the starting candidate. The unchanged historical comparator
uses `abs(actual-expected) <= 1e-3 * max(1, abs(expected))`. Complete reports retain
all failing values, missing/extra keys and every passing nonexact number.

| Suite | Final matched / union | Exact | Resolved / retained / introduced vs start | Resolved / retained / introduced vs accepted |
| --- | ---: | ---: | ---: | ---: |
| Main query | 52,059 / 55,251 | 36,721 | 1,350 / 3,192 / 0 | 1,857 / 3,192 / 0 |
| Support geometry | 66 / 86 | 64 | 0 / 20 / 0 | 0 / 20 / 0 |
| Selection | 1,250 / 1,440 | 1,081 | 0 / 190 / 0 | 319 / 163 / 27 |
| Fine selection | 1,504 / 1,728 | 1,109 | 0 / 224 / 0 | 423 / 200 / 24 |
| Owner boundary | 56 / 64 | 53 | 0 / 8 / 0 | 22 / 6 / 2 |
| Prior isolated units | 48 / 48 | 36 | 0 / 0 / 0 | 3 / 0 / 0 |
| Root skin lifecycle | 588 / 644 | 543 | 0 / 56 / 0 | 129 / 42 / 14 |
| New initial controls | 6,256 / 6,256 | 5,790 | 2,880 / 0 / 0 | 2,880 / 0 / 0 |

Support geometry has 72 Native keys and 86 union keys because the engine exposes
three separate edge shapes instead of Native's one chain. All twenty resulting
failures remain visible. Native pairs agree on 65,503 values across eight suites;
only the 6,256 continuation values were newly recorded here.

The main suite improves by 918 near-touch observations and 432 coincident-circle
contact observations over the starting candidate. It still retains 3,192 initial
contact-feature failures. The inherited 53 selection/boundary and 14 skin
introductions remain unchanged relative to the canonical accepted baseline.
This stage is unaccepted. See [REMAINING.md](REMAINING.md) for the confirmed
nonowned skin dependency and the limits of the remaining tie investigation.

## Commands and artifacts

`commands.sh` contains the final build, capture and regression commands. Build
outputs retain the assembly-local native8 shim. All builds use no-server flags
and the global exclusive lock. The first copied frozen build failed because its
private files retained read-only permissions; only that private copy was made
writable. The failed build log is retained. The first regression driver stopped
after all thirteen focused probes because its private scene output directory was
missing. The corrected driver resumed with `validate.py --skip-focused`; the
completed probes and failed log remain intact.

```bash
source /mnt/storage-hdd/node-b-caches-20261023/query2d-continuation-t3-06ecf6ba/env.sh
# Starting runtime, rebuilt from the exact starting source before edits:
python3 tests/projects/query2dcontinuation/capture_existing.py --label starting
# Frozen runtime copied to managed-accepted, never hardlinked or rebuilt:
UNBOUND_MANAGED_DIR="$QUERY_WORK/managed-accepted" \
LD_LIBRARY_PATH="$QUERY_WORK/managed-accepted" \
python3 tests/projects/query2dcontinuation/capture_existing.py --label accepted
# Final fresh builds and captures, using fresh labels/paths for another replay:
bash tests/projects/query2dcontinuation/commands.sh
```

These are historical write-once labels. Use fresh private paths for a new replay.
Existing query2dparity/colliderskin2d drivers, sources, captures and checkers were
read only. `private_cli.py` copies repository gate fixtures before engine runs so
their generated Library/Unbound caches also stay private. No Rust or full Bevy
build, SDK or ABI changes, Native re-recording of existing suites, authored game
access, PR, sibling merge or cherry-pick was used. Shared API checking remains
parent-owned. The ccc refresh/search attempts timed out at 25 seconds; logs are
retained and exact identifier searches used rg without resetting the daemon.

`Evidence/native-manifest.json`, `runtime-manifest.json` and `summary.json` retain
Native agreement, source/runtime hashes, all eight suite reports, all rejected
attempts, and hashes for every archived artifact. Eight frozen baseline manifests
were verified without modification. The copied CLI SHA256 is
`0a55f7d5fb6314deda371ee9c9334ec129652047925272d803d5cb86bb704b56`.
The assembly-local native8 SHA256 is
`4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208`.

## Regression gates

| Gate | Fresh result |
| --- | --- |
| Integrated managed filter | 444/444, zero skips; 421 inherited + 23 added |
| Canonical broad physics | 11,581/11,626; 8,556 exact; resolves 76, retains 45, introduces 0 vs accepted |
| Broad vs starting candidate | All 11,626 decoded JSON values identical |
| Assigned original query scenarios | 7,375/7,375 |
| Timing | 18,491/18,491; 18,445 exact; introduces 0 |
| Mixed callback order | 5,720/5,720; 5,716 exact; introduces 0 |
| 3D matrix | 5,055/5,055; 4,803 exact; all 21 raw files byte-identical to accepted SDK8 |
| Focused 3D lifecycle | All thirteen inactive/sequence/active/clone/trigger/SDK/order/component/barrier controls preserved |
| 2D contact lifecycle / receiver order | 25,356 + 1,152 observations exact to Native |
| Serialized loading | Both Transform/Rigidbody orders, 4,656 + 4,656 exact observations |
| Joints2D | 1,382/1,401 direct; existing 19 comparator-reported roundoff records |
| Effectors | 2,531/2,559 direct; existing 28 measured contact-order alternatives |
| Forces | 608/608 |
| Graphics / Canvas / messages | 80/80, 8,671/8,671, 316/316 |
| Lifecycle | Raw file byte-identical to Native |

All complete regression reports and raw files are in `Evidence/gates`. Neither
passed parent gates nor the query improvements override the inherited 67 new
failing stress observations. The handoff remains unaccepted pending an authorized
integration and replay with the motion owner's skin correction.

Final CoreModule SHA256 is
`2d5a528ac6f76c9d9fa3fb2078a77276da825ceadb5f2d2b180ce2baf870a689`;
final Host SHA256 is
`07e7c06b9ac4d34bf6c9f4010a681c714eb68827234f074b8500473b44245382`.
The integrated test-local native8 hash equals the assembly-local native8 hash.
The exact post-setup-error resume was:

```bash
export UNBOUND_MANAGED_DIR="$QUERY_WORK/managed-final"
export UnboundNativeDir="$UNBOUND_MANAGED_DIR" LD_LIBRARY_PATH="$UNBOUND_MANAGED_DIR"
python3 tests/projects/query2dcontinuation/validate.py --skip-focused \
  > "$QUERY_WORK/validation/gates-resume.log" 2>&1
python3 tests/projects/query2dcontinuation/check_gates.py
python3 tests/projects/query2dcontinuation/report.py
python3 tests/projects/query2dcontinuation/verify.py
```

The starting broad replay used the private starting runtime with
`PHYSICS_PROBE_OUT="$QUERY_WORK/physics-starting.json"` and
`with-heavy-job timeout 240 private_cli.py run -p tests/projects/physics
--headless 600 --no-watch`. Ordered implementation SHAs are in
`Evidence/ordered-implementation-commits.txt`. The final evidence commit follows
those source commits on `compat/query2d-numerics-20261031`.
