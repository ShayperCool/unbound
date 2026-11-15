# NavMeshAgent arrival

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 166 rows. A macOS Editor capture of the existing
`navmesh` probe agrees with its Linux reference, and this probe records discrete states only (frame
numbers, flags and corner counts), so its rows do not depend on the platform's last-bit arithmetic.
The gate compares every missing, extra or unequal row exactly.

On a flat runtime-built NavMesh, 122 agents walk straight to destinations without obstacle
avoidance. They use four speed and acceleration pairs, with and without autoBraking, over distances
4 + i × 0.0137 m that leave different remainders before the last step. Two further lanes without
braking end a step a few micrometres before the destination. Each `lane.*` row records the frame
whose step leaves the agent standing, then `hasPath`, the path's corner count and whether
`remainingDistance` is positive over its last three frames. The `query.*` rows record the corners of
`NavMesh.CalculatePath` between points 0 to 1 mm apart along x, z and a diagonal.

- A straight path merges only exactly equal points. An end 1e-7 m from the start is a second
  corner unless the two floats are equal (`query.d1E-07.z`: 2.7 + 1e-7 rounds to 2.7).
- An agent without braking keeps steering to an end a few micrometres away: `hasPath` is false within
  a centimetre, but the end stays a corner and the remaining distance stays positive. The next step
  passes the end and lands on it.
- A braking agent lands on the end with the step that leaves no more than 0.0001 m. A step leaving
  0.0000992 m lands; one leaving 0.000131 m walks once more. The step and the distance left are both
  three-dimensional: on a height mesh, an end above the agent is farther than its climbing plan
  reaches (tests/projects/navbaked `height.agents`). After a landing past the end, the climbing plan
  can reach it (tests/projects/navlinks `endatlink`).

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [NavMeshAgent.autoBraking](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-autoBraking.html) | `lane.k*.b1`: the braking lanes' remainders straddle 0.0001 m. |
| [NavMesh.CalculatePath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMesh.CalculatePath.html), [NavMeshAgent.remainingDistance](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-remainingDistance.html) | `query.*`; `lane.fast.b0` and `lane.slow.b0` end micrometres short. |

The documentation states only that autoBraking slows the agent near its destination to avoid
overshooting. The thresholds are observations for 6000.0.71f1, selected through
`Compat.NavMeshBrakingArrivalMeasured` and `Compat.NavMeshStraightPathExactMergeMeasured`. Other
releases keep the previous rules, unmeasured.

Not measured: avoidance near the end, stopping distances, and links. A runtime-built destination
projected onto a polygon edge can differ by about 1e-6 m in the macOS Editor; such lanes are not part
of this probe. Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/navarrival/run.py native --label reference-a
python3 tests/projects/navarrival/run.py native --label reference-b
python3 tests/projects/navarrival/run.py publish reference-a reference-b
python3 tests/projects/navarrival/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
