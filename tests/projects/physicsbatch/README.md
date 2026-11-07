# 3D batch query commands

Unity 6000.0.71f1, Linux GPU Editor. Two independent project copies produced
byte-identical 5,867-record captures before the canonical reference was published.
The repeat captures and Editor supervision receipts remain in the validation cache.
The extensions preserve every earlier reference value. They add global backface
controls, 80 faces on one mesh, container/closest-point limitations and 63 colliders
containing a multiple-face ray origin. The scheduling/terrain/controller extension is
described in [FOLLOWUPS.md](FOLLOWUPS.md).

## Initial audit

At base 2e151c334, all eight commands had constructors, properties and ScheduleBatch:
RaycastCommand, SpherecastCommand, BoxcastCommand, CapsulecastCommand,
ClosestPointCommand, OverlapSphereCommand, OverlapBoxCommand and OverlapCapsuleCommand.
Managed tests covered filters, local physics scenes, command/result indexing, empty
results, obsolete overloads and dependency completion. None had an Editor reference.
Backfaces and ray multiple-face hits were refused. Closest-point scale was restricted.
Scheduling ran synchronously; minCommandsPerJob was unused. Every unused slot was
cleared. Invalid capacities threw. The underlying queries allocated lists, closures
and native vectors per call and had a 4,096-touch ceiling.

## Contracts and observations

Official 6000.0 API pages consulted for this work:

- [RaycastCommand](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RaycastCommand.html)
- [SpherecastCommand](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SpherecastCommand.html)
- [OverlapSphereCommand](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/OverlapSphereCommand.html)
- [QueryParameters](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/QueryParameters.html)
- [ClosestPointCommand](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ClosestPointCommand.html)
- [ScheduleBatch](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RaycastCommand.ScheduleBatch.html)

| Mechanism | Documented guarantee | Reference observation |
| --- | --- | --- |
| Result placement | Command N starts at N * maxHits; closest point uses N | Three commands, including a repeated collider and an empty middle command, preserve their own blocks and extra result storage |
| Termination | First invalid hit has collider ID 0; later unused slots have no guaranteed value | Every unused slot is cleared, including preseeded buffers |
| Face flags | Backfaces are configurable; multiple mesh faces apply only to rays | Front/back mesh tests obey the command flags; shape casts ignore hitMultipleFaces |
| Filters | Layer mask and trigger interaction select colliders | Ignore, Collide and UseGlobal, two global values, empty mask and one-layer masks |
| Sweeps | Single-call and batch APIs are distinct operations | Batch sweeps omit initial overlaps at both maxHits 1 and 4; NonAlloc single calls include them |
| Closest point | Results are available after completion | Supplied scale does not affect the measured box result, including zero, negative and nonuniform values |
| Scheduling | dependsOn precedes queries; results require completion; minCommandsPerJob selects job granularity | Dependent command writer is visible; min values -1, 0, 1 and 64 complete |
| Invalid capacity | Public 6000.0 C# scheduling checks warn and return for invalid maxHits or short results | maxHits 0/-1, short results and an unassigned short result buffer return without an exception |
| Closest-point limits | Box, sphere, capsule and convex mesh colliders only | A nonconvex mesh or unresolved ID returns the input point with a diagnostic; a disabled collider also returns the input point; a null constructor argument throws NullReferenceException |
| Container limits | Scheduled job containers require valid allocations | Disposed and Temp containers throw InvalidOperationException; zero commands, including an unassigned command array, return without work |
| Multiple-hit order | No order is promised | Raw slot order is retained, including mesh face order; no global sort is introduced |

The scale and invalid-argument observations are release/context measurements, not
claims about all releases or every invalid container. Physics scene isolation is
covered by managed controls; the fixed-scene reference uses the default scene.

## Running

All disposable artifacts go under `$XDG_CACHE_HOME/unbound`. Every runner invocation
requires `--release`; the gate never starts the Editor. Native runs use the supervised
GPU Editor launcher and publish checks its receipts, completion and identical bytes.

```sh
python3 tests/projects/physicsbatch/run.py prepare --release 6000.0.71f1 --label nativeG
python3 tests/projects/physicsbatch/run.py native --release 6000.0.71f1 --label nativeG
python3 tests/projects/physicsbatch/run.py prepare --release 6000.0.71f1 --label nativeH
python3 tests/projects/physicsbatch/run.py native --release 6000.0.71f1 --label nativeH
python3 tests/projects/physicsbatch/run.py publish --release 6000.0.71f1 --label nativeG --repeat-label nativeH
python3 tests/projects/physicsbatch/run.py gate --release 6000.0.71f1 --cli "$UNBOUND"
```

`--work` selects a separate validation cache directory; `--label` selects its capture
and private project. The comparator retains every missing, extra and differing row
in the comparison JSON, with 1e-4 absolute and 1e-5 relative float tolerances.

## Validation and remaining limits

The engine resolves every measured result and invalid-input case. The strict
comparison is 5,855/5,867. Twelve raw differences remain in `stress/{faces,mixed}/{0,2,5,7,40,42}`:
three pairs of mesh faces exchange slots in each stress scene. All 80 unique collider/triangle identities per scene,
distances, points and normals agree at the unchanged tolerances; termination and
counts agree. `compare` still fails on these twelve rows and saves both values.
The gate's separate `verify.py` keeps those failures and checks the complete identity
set without sorting either capture or requiring an unspecified hit order. Missing,
extra, duplicate faces, changed geometry and a hit after termination all fail.
Six comparator controls exercise those failure cases.

Batch queries retain the last synchronized transforms even when autoSyncTransforms
is enabled, as measured with ordinary edits and pending transform jobs.
Scheduling uses parallel jobs with `minCommandsPerJob` chunking and dependency
tracking. The returned handle owns read access to commands and write access to
results until completion. Scene preparation runs on the calling thread; worker
queries use thread-local scratch. Scene mutations wait for query execution while
array safety remains owned by the job until completion. Terrain heightmap/tree
queries and tree material lifetimes are Editor-measured by the added controls.
Additional releases and every unsafe-container alias combination remain unmeasured.

Validation: release build; all 3,377 managed tests; Rust unbound-host test/doc-test
commands; stub regeneration with no drift; the engine-only gate above. Managed
controls include local scenes, nonuniform actual collider scale, a missing collider
between valid commands, initial overlaps, no managed allocation growth between 1
and 1,024 commands, and 4,105 overlapping colliders beyond the former touch ceiling.
The native query callback uses fixed stack chunks and reserved reusable ray-face
storage. Batches reserve managed/native scratch before their command loops, allocate
result staging once per batch and prepare each physics scene once. They share the
single-call query collectors and add no per-command list, closure or native vector.
No importer or serialized cache output changes.

The mixed stress case adds 63 spheres containing the ray origin before repeating
an 80-face query into 96 slots. Both Editor captures return all 80 faces. Its managed
regression fails against the previous native library with 33 faces, and passes with
80 after rejecting initial hits in the native post-filter before touch storage.
The query uses the requested logical capacity even when reusable storage is larger;
results therefore do not depend on earlier queries warming that storage.
