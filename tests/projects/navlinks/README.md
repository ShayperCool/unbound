# Off-mesh link probe

`NavLinksProbe` plays NavMesh agents over off-mesh links at a fixed 0.02 s step for 400 frames and
records every agent's state in every frame (in LateUpdate): position, rotation, next position,
velocity, desired velocity, steering target, remaining distance, path flags and corners,
destination and path end, `currentOffMeshLinkData`/`nextOffMeshLinkData` and the link's
`occupied`/`activated`. Each lane of `Lanes.cs` lies in its own 40 m cell: two 10 m platforms with
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

The staged `Reference/unity-6000.0.71f1.jsonl` contains 19254 records, SHA-256
`b5d6cbe7a9dabf78aaa53d45d3690859be35227b71bf20e058a054b734bc23bf`.
It comes from source `982cee918eab8018bce2a5b723e76f2079033592`. The retained
`native-c/native1` and `native-c/native2` captures agree byte for byte, but were
concurrent processes in distinct project copies from one setup, not independent
setups. The later `native-d` pair supports only the recorded matching
`deactivatewait`, `deactivatemid` and `redirectmid` lanes for agent `a`, not the
whole reference. Staging this canonical file does not admit the reference or a
runtime. Keep raw repeats outside Git.

Select the release explicitly as above. The runner uses `tests/probe_paths.py` to
select that release for Editor paths, project copies and `Reference/`; the
comparison command reads the canonical reference directly. Set `XDG_CACHE_HOME`
when the cache is elsewhere, and place comparator reports under that cache too.
The setup and publish modes are explicit fixture/reference authoring operations;
normal run logs, copies, captures and comparisons belong in the cache. A setup
can replace scene/NavMesh assets, and publish can replace the selected reference.
Neither operation is part of source staging or authorized by this source task.
