# Off-mesh link probe

`NavLinksProbe` plays NavMesh agents over off-mesh links at a fixed 0.02 s step for 400 frames and
records every agent's state in every frame (in LateUpdate): position, rotation, next position,
velocity, desired velocity, steering target, remaining distance, path flags and corners,
destination and path end, `isPathStale`, `navMeshOwner`, `currentOffMeshLinkData`/
`nextOffMeshLinkData` and the link's `occupied`/`activated`. Each lane of `Lanes.cs` lies in its own 40 m cell: two 10 m platforms with
a 2 m gap only a link crosses (B 1.5 m lower in the drop lanes, a bridge in front in the cost
lanes). The level and its baked NavMesh are committed (`Assets/Scenes`); one lane's NavMeshLink and
another's legacy OffMeshLink are serialized in the scene, the other links are added by the probe.

| lanes | what they vary |
| --- | --- |
| `serialized`, `auto`, `legacy`, `oneway`, `reverse`, `reverseoneway`, `drop`, `climb`, `high` | the link components, directions, heights |
| `fast`, `slow`, `radius1`, `baseoffset`, `norotation`, `noposition`, `diagonal`, `turn` | agent speed, radius, base offset, updateRotation/updatePosition, turning onto and off a link |
| `startonlink`, `endatlink`, `shortafter`, `nobraking` | paths starting or ending at a link |
| `snap`, `snapnear`, `snapfar`, `wide` | link ends off the NavMesh (which connect), a 4 m wide link |
| `costlow`, `costhigh`, `areajump`, `areamask`, `query` | cost modifier, link area cost and mask; `NavMesh.CalculatePath` over a link |
| `two`, `inactive`, `inactive130`, `deactivatemid`, `deactivatewait` | occupancy; activating and deactivating links |
| `manualwait`, `manualmove`, `manualcurve`, `manualtoggle`, `autooffmid` | `autoTraverseOffMeshLink = false`: waiting, scripts moving the agent, `CompleteOffMeshLink` |
| `warpwait`, `warpauto`, `stopmid`, `resetmid`, `redirectmid`, `speedmid`, `completeauto`, `disableagent` | Warp, isStopped, ResetPath, SetDestination, speed, CompleteOffMeshLink, disabling the agent on a link |
| `offedge`, `island`, `aheadoff`, `pendingold`, `incall` | destinations beyond the NavMesh's edge (standing, walking ahead and behind), on another island and over the gap; the current path while a new one is pending; a path given in the SetDestination call |
| `targets` | `NavMesh.CalculatePath`, `NavMeshAgent.CalculatePath` and `SetDestination` to targets beyond an edge, a corner, above, below and over the gap, each bisected (17 steps, one frame per `SetDestination` step) for where it stops finding a path; agents of radius 0.25, 0.5, 0.75 and 1 and height 4 |
| `widetype` | the same for an agent of the second agent type ("Wide", radius 1.5, height 5, `ProjectSettings/NavMeshAreas.asset`) on a NavMesh built at run time |
| `low`, `lowmanual`, `crosstile` | links whose ends lie 0.3 m below the NavMesh (crossed, completed by script); a link across a NavMesh tile edge, crossed both ways (occupancy) |
| `turning`, `staleowner` | turning at another speed and angularSpeed (1.5 m/s and 40°/s, 6 m/s and 400°/s); areaMask and the agent's own area cost changed, a new destination |

`run.py` makes the level, records the Editor and runs Unbound, each run on a fresh copy of the
project under `$XDG_CACHE_HOME/unbound/validation/runs/navlinks/<label>` (labels are never
reused; Editors run through the shared heavy-job launcher on the GPU and exit by themselves):

    python3 tests/projects/navlinks/run.py setup   --release 6000.0.71f1 --label L   # level + NavMesh
    python3 tests/projects/navlinks/run.py editor  --release 6000.0.71f1 --label L   # two Editor process/copy captures from one setup
    python3 tests/projects/navlinks/run.py publish --release 6000.0.71f1 --label L   # identical? -> Reference/
    UNBOUND_CLI=.../unbound python3 tests/projects/navlinks/run.py unbound --release 6000.0.71f1 --label L
    python3 tests/projects/navlinks/compare.py tests/projects/navlinks/Reference/unity-6000.0.71f1.jsonl \
        ~/.cache/unbound/validation/runs/navlinks/L/unbound/values.jsonl --report ~/.cache/unbound/validation/runs/navlinks/L/report.jsonl

`compare.py` holds positions, rotations, corners and distances to 1e-3 and velocities to 2e-3 (the
NavMesh probes' tolerances), everything else exactly, and counts and reports every missing, extra
or failing value. The probe raises `NavMesh.pathfindingIterationsPerFrame` so that every lane's
path is found in the first frame; the per-frame A* budget is not measured here.

The staged `Reference/unity-6000.0.71f1.jsonl` contains 29090 records, SHA-256
`3a85a43b489b4303f8837638b26d51d624427fe96a1fdd29c8b50b8fc6b7792c`: the 19254 records of
the earlier reference (source `982cee918eab8018bce2a5b723e76f2079033592`), unchanged, with
`isPathStale` (`stale`) and `navMeshOwner` (`own`) added to each agent record, and the lanes from
`offedge` on. Its two Editor captures (concurrent processes in distinct project copies from one
setup) agree byte for byte. In two earlier pairs of captures of the same lanes the second agent of
lane `two` planned another desired velocity from frame 4 in one capture of each pair (avoidance
between the two agents varies between Editor runs); the published values are the ones the earlier
reference recorded. Staging this canonical file does not admit the reference or a runtime. Keep
raw repeats outside Git.

Select the release explicitly as above. The runner uses `tests/probe_paths.py` to
select that release for Editor paths, project copies and `Reference/`; the
comparison command reads the canonical reference directly. Set `XDG_CACHE_HOME`
when the cache is elsewhere, and place comparator reports under that cache too.
The setup and publish modes are explicit fixture/reference authoring operations;
normal run logs, copies, captures and comparisons belong in the cache. A setup
can replace scene/NavMesh assets, and publish can replace the selected reference.
Neither operation is part of source staging or authorized by this source task.

Traversal follow-up for Unity 6000.0.71f1: automatic crossing advances by the
current speed on every step. `speedmid` changes from 3.5 to 1 m/s during the
approach, and `stopmid` holds its position and desired velocity while stopped,
then resumes the same crossing. Managed controls also exercise zero speed, a
speed increase and stop/resume on a longer link. These observations refine the
automatic traversal profile; the API documents stop/resume of the current path
([isStopped](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-isStopped.html)),
but does not specify an interpolation curve or crossing duration.

The first differing frames of the sheriff-S19-a6f0d8af2 capture are `speedmid`
86, `stopmid` 86, `nobraking` 110, `crosstile` 68 and `two` 83. The last two
first differ in desired velocity, before their later positional differences.
`nobraking` first differs in `hasPath` on landing and later overshoots its
destination on frame 127. The speed and stop changes address the first pair;
landing path state, arrival, crowd steering and route geometry remain visible
in the unchanged comparator. A link's approach length relative to agent radius
is not covered by the current reference.

Q361/Q342 movement follow-up reduces strict failures from 9327 to 1993. The
reference and tolerances remain unchanged. Crossing agents supply their planned
velocity to crowd avoidance, while `velocity` still reports the frame's actual
position change. A waiting agent permits zero steering speed, including the
separation calculation. Braking takes the whole slower plan, so a tilted landing
velocity can become a horizontal walking velocity on the next update. Reaching
a straight destination ends the path with automatic braking disabled too.

`costlow` and `areajump` each fall from 869 to 2 failing values; `nobraking`
from 2423 to 1; `two` from 1678 to 65; `crosstile` from 2369 to 939. The
remaining `crosstile` divergence starts with desired velocity on frame 86; the
remaining `two` divergence starts with rotation on 111. No lane's failing-value
count increases. Some previously matching keys within these changing
trajectories differ, including three path-end reports near arrival; the full
reports retain them. They are not assertions of universal parity.

Horizontal arrival remains open. Simply replacing the internal step's 3D norm
with its horizontal norm adds 785 failures in `endatlink`: the agent lands past
its destination on frame 110 and must return to it on 111. The final change
retains that existing behavior. Managed controls exercise a destination at the
landing, arrival without braking, and braking that changes heading.
