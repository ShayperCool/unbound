# 3D batch query commands

Unity 6000.0.71f1, Linux GPU Editor. Two independent project copies produced
byte-identical 4,791-record captures before the canonical reference was published.
The repeat captures and Editor supervision receipts remain in the validation cache.

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
| Multiple-hit order | No order is promised | Raw slot order is retained, including mesh face order; no global sort is introduced |

The scale and invalid-argument observations are release/context measurements, not
claims about all releases or every invalid container. Physics scene isolation is
covered by managed controls; the fixed-scene reference uses the default scene.

## Running

All disposable artifacts go under `$XDG_CACHE_HOME/unbound`. Every runner invocation
requires `--release`; the gate never starts the Editor. Native runs use the supervised
GPU Editor launcher and publish checks its receipts, completion and identical bytes.

```sh
python3 tests/projects/physicsbatch/run.py prepare --release 6000.0.71f1 --label nativeA
python3 tests/projects/physicsbatch/run.py native --release 6000.0.71f1 --label nativeA
python3 tests/projects/physicsbatch/run.py prepare --release 6000.0.71f1 --label nativeB
python3 tests/projects/physicsbatch/run.py native --release 6000.0.71f1 --label nativeB
python3 tests/projects/physicsbatch/run.py publish --release 6000.0.71f1 --label nativeA --repeat-label nativeB
python3 tests/projects/physicsbatch/run.py gate --release 6000.0.71f1 --cli "$UNBOUND"
```

`--work` selects a separate validation cache directory; `--label` selects its capture
and private project. The comparator retains every missing, extra and differing row
in the comparison JSON, with 1e-4 absolute and 1e-5 relative float tolerances.
