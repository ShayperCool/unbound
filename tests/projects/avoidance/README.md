# NavMesh agent avoidance

`AvoidanceProbe` plays NavMesh agent configurations on a flat 1000 m plane (`Probe.unity`) or the
same plane with a 20 m wall in each 80 m cell (`Walls.unity`), each configuration on its own cell,
and records every agent's position, velocity, desired velocity (what its avoidance chose), next
position and steering target per frame at a fixed 0.02 s step. The configurations
(`Configs/*.txt`, one per line: `frames;agent;agent;...`, an agent being
`x,z,destX,destZ,speed,radius,quality,priority,velX,velZ,acceleration`, `-` for no destination)
isolate one thing each:

| set | what it varies |
| --- | --- |
| `ahead`, `near`, `speeds` | a standing neighbour at distances and angles; speeds, headings, radii, qualities |
| `moving` | the agent's own velocity; neighbours that move or have a velocity |
| `overlap`, `separation`, `separation-behind` | neighbours touching or within separation reach; priorities, radii |
| `runs` | long head-on and crossing runs |
| `arrival`, `arrival-start` | braking at the end of the path (speeds, accelerations, radii; starts near the end) |
| `walls-along`, `walls-towards` (`Walls.unity`) | walking along a wall, towards it, round its end |

Measured (see `managed/UnityEngine/AI/DotRecast/UNBOUND.md`): the neighbours Unity's agents
avoid (a proximity grid sized from all agents, covering each neighbour's radius and predicted travel; as or more important, not behind, the soonest
reachable 8), their NavMesh edges (the nearest 8, none while the corner they steer by is the end
of their path within reach), and the sampling: the desired velocity kept with nothing to avoid,
else weighed first, then Detour's adaptive rings around 0.7 of it offset right by 0.04 of it,
weighed 0.2 (desired), 0.1 (current) and 2 / (0.1 + time to impact / prediction time) against the
neighbours' velocities shared by responsibility; separation pushes 1.4·(1 − (d/2.5r)²) from the
more important (half from equals); overlaps are resolved by the less important agent. Braking:
once the end is nearer than two radii at the agent's speed relative to full speed, the speed falls
by v²/2d per second, at once.

Make the scenes (once; they are committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -nographics -projectPath tests/projects/avoidance \
        -executeMethod ProbeSetup.Create -logFile ~/.cache/avoidance-create.log

Record Unity's reference of a set (the walls sets with `AVOIDANCE_SCENE=Assets/Scenes/Walls.unity`):

    AVOIDANCE_CONFIG=$PWD/tests/projects/avoidance/Configs/ahead.txt \
    AVOIDANCE_OUT=$PWD/tests/projects/avoidance/Reference/ahead.txt DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/avoidance \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/avoidance.log

Run Unbound and compare (`--scene Assets/Scenes/Walls.unity` for the walls sets):

    AVOIDANCE_CONFIG=$PWD/tests/projects/avoidance/Configs/ahead.txt AVOIDANCE_OUT=/tmp/ahead.txt \
        unbound run -p tests/projects/avoidance --headless 100000 --no-watch
    python3 tests/projects/avoidance/compare.py tests/projects/avoidance/Reference/ahead.txt /tmp/ahead.txt

Left (the remaining differences): the side some agents choose once they move, and the long runs,
where any difference carries on.

Q385, Unity 6000.0.71f1: the swept neighbour bounds remove all three failing
long-run configurations, three of five moving configurations, and one speed
configuration. Four groups retain 34 configurations: moving 2, near 10, overlap 5,
speeds 17. No newly failing record is introduced in these sets. The post control
in navpath remains at 17 values. The new prediction and finite-input controls are
recorded after the original agents in navmesh; its two GPU Editor repeats agree
and preserve every earlier reference row. Other releases retain point insertion
and previous input handling through Compat, unmeasured there.

Q401, Unity 6000.0.71f1: the recorded steering target is the first corner found
after the step; a corner within a centimetre of where the step ends is dropped.
Moving, overlap and 16 of 17 speed configurations now match, and near falls from
10 to 2. Three configurations remain, two near and one speeds. Each differs only in
the steering target of its second frame: Unity reports an intermediate corner at
x = 42.67, Unbound the destination at x = 50. No newly failing record is
introduced (Mac engine run against the unchanged references).

Q426 retains the three configurations in the near and speeds groups. Their
second-frame steering target is the intermediate corner near x = 42.67 in Unity
and the destination in Unbound. Removing straight-path endpoint projection does
not resolve them. No corner is inserted, snapping distance changed or velocity
sample preferred to fit these records. The separate navpath post-avoidance
control remains at 17 differing values. The measurements use the existing Linux
6000.0.71f1 references; new diagnostic Editor runs used the NVIDIA RTX 4060 only.
