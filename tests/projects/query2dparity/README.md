# Query geometry continuation

This is a blocked implementation candidate, not an accepted integration. All 76
assigned broad query observations are resolved, with zero introduced broad keys
or values. Additional offset/lifetime controls introduce 53 observations across
three suites. Their complete values remain in the reports. See OWNER-PROPOSAL.md
for the Native-backed collider skin capture proposal; the other owner's files
were not edited. Further general query precision remains unproved.

The source parent is 7c0bde630f630c55181a31ac7563954323e774da. Production edits are
limited to Physics2DQueries.cs and three new QueryParity helpers. Shared GJK,
contact manifolds, solver TOI, Collider2D, Rigidbody2D, Physics2D.cs, World, generic
runtime, Compat, Rust and native sources are unchanged. No authored files in
~/unity-projects/game were read, run or modified.

Two independent Unity 6000.0.71f1 Linux CPU processes agree byte for byte on every
suite below, totaling 58,603 observations per set of six recordings. The three
original query scenarios also equal the canonical broad Native reference exactly
at all 7,375 original keys.

| Native suite | Observations per recording | Manifest |
| --- | ---: | --- |
| queries | 55251 | [manifest](Evidence/native-manifest.json) |
| support geometry | 72 | [manifest](Evidence/geometry-manifest.json) |
| selection | 1440 | [manifest](Evidence/selection-manifest.json) |
| fine selection | 1728 | [manifest](Evidence/fine-manifest.json) |
| owner boundary | 64 | [manifest](Evidence/boundary-manifest.json) |
| isolated unit controls | 48 | [manifest](Evidence/tests-manifest.json) |

The Native measurements precede the respective query math changes. Static sweeps
include original inputs, scaled/rotated circles, boxes and capsules, starting
inside/outside, tangency/near-touch, ranges, masks, depth, normals and nonalloc
queries. Selection controls vary angle, size and contact offset. The boundary
suite changes offset before/after identical collider creation and after a size
edit. Isolated controls correct a unit-fixture isolation mistake discovered by a
failed 389/390 run; both the failed source and TRX are retained.

The query TOI uses fixed query precision, checks for actual surface contact, and
keeps common solver TOI unchanged. Contact reconstruction clips actual supporting
features, distinguishes polygon and capsule side radii, and averages near-flat
contacts over their core overlap. No fixture names, callback synthesis, allocator
padding, tolerance changes, exclusions or expected-failure masks were added.

| Final gate | Result |
| --- | --- |
| Canonical broad physics | 26/29 scenarios; 11,571/11,626 pass; 8,546 exact; resolves 76, retains 55, introduces 0 |
| Assigned query scenarios | 7,375/7,375 pass; 64 + 2 + 10 prior failures resolved |
| Initial new query controls | 50,709/55,251 pass; resolves 507, retains 4,542, introduces 0 |
| Offset selection | 1,250/1,440 pass; resolves 319, retains 163, introduces 27 |
| Fine selection | 1,504/1,728 pass; resolves 423, retains 200, introduces 24 |
| Owner boundary | 56/64 pass; resolves 22, retains 6, introduces 2 |
| Isolated unit controls | 48/48 pass; resolves 3, introduces 0 |
| Integrated managed filter | 390/390, zero skips; 353 existing + 37 new |
| 3D matrix | 5,055/5,055; 4,803 exact; all 21 files byte-identical to accepted SDK8 |
| Focused 3D | All accepted inactive/sequence/active/clone/trigger/SDK/order/component/barrier controls preserved |
| Timing / mixed order | 18,275/18,491 and 5,560/5,720; same 216 + 160 retained observations; introduces 0 |
| Joints2D | 1,382/1,401 direct matches and the existing 19 comparator-reported roundoff records |
| Effectors | 2,531/2,559 direct matches and the existing 28 measured contact-order alternatives |
| Forces | 608/608 |
| Graphics / Canvas / messages | 80/80, 8,671/8,671, 316/316 |
| Lifecycle | Byte-identical to Native |

Evidence/summary.json contains every broad, timing and order failure, plus links
and hashes for all complete new-control comparisons. Evidence/comparisons has
full union-key reports, including every passing nonexact number and every
introduced or retained observation. No teardown records are excluded. The 55
broad failures remain bounce 9, stack 27 and kinematic 19. API checking remains
parent-owned and was not rerun here. Older releases remain unmeasured. This is
not full engine or full query parity.

Rejected math from TOI-only, blanket manifold, clipping, warm simplex, feature,
first-point, orientation, solver-coupled tolerance, fixed tolerance, query TOI and
argument-order attempts is under Evidence/rejected. Every candidate capture,
failed diagnostic, failed test and interrupted validation output is retained in
Evidence or the hashed private artifact tree. Candidate group8 and query10 parent
replays were stopped when additional controls rejected them; final12 is fresh.

## Reproduction

Private outputs and caches are under:

```
/mnt/storage-hdd/node-b-caches-20261023/query2d-t3-2851ee93-20261031/
```

Use its env.sh, which sources the shared environment for PATH, unsets WT, then
overrides every engine/build/cache/output path. Native8 managed outputs and the
ABI5 CLI were copied, never hardlinked. Every Unity, dotnet, engine and GPU
process uses ~/.cache/compat/with-heavy-job and the shared exclusive flock.

```bash
source /mnt/storage-hdd/node-b-caches-20261023/query2d-t3-2851ee93-20261031/env.sh
~/.cache/compat/with-heavy-job dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$UNBOUND_MANAGED_DIR" -p:UnboundTarget="$UnboundTarget" \
  -p:UnboundNativeDir="$UnboundNativeDir" --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false /nodeReuse:false
python3 tests/projects/query2dparity/run.py engine --label NEW_UNIQUE_LABEL
```

run.py rejects capture-label reuse. New Native captures use `prepare --probe X`,
then `native --probe X --label NEW_NATIVE1` and a second independent label.
Supported suites are queries, geometry, selection, selection-fine, boundary and
tests. Native commands use -batchmode -nographics and the specified Linux Editor.

validate.py replays the exact parent focused, broad, matrix, UI/graphics and
lifecycle commands into the private paths in env.sh. For another validation,
choose fresh SCENE_WORK, SCENE_PARENT_WORK and TRIGGER_WORK first and preserve
all existing captures. Dotnet uses the required three no-server environment
variables and flags. The filter explicitly appends Physics2DQueryParityTests.
report.py archives into this stage and marks strict_acceptance false; it must
not be read as an approval override. Do not rerun historical report writers.

All frozen native6/7/8, sleep3d, trigger2d and inactive3d manifests were verified.
Actual assembly-local and test-local shim hashes equal
4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208.
The copied CLI hash is
0a55f7d5fb6314deda371ee9c9334ec129652047925272d803d5cb86bb704b56.
Final host CoreModule hash is
a7947ec993f8ee7345480318979714a19a4808ee5f04adbbe2805dcded57810f.
Main ABI5, shim8 and shader cache13 are unchanged. No Rust/Bevy rebuild was used.

ccc initialization succeeded, but refreshed indexing and search attempts timed
out. Their logs are preserved; exact-identifier searches used rg. No shared
index was reset. No PR or merge was created and no parent/backup branch changed.
