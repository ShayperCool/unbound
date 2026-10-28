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

Measured (see `managed/UnityEngine/AI/DotRecast/UNBOUND.md`): Unity's avoidance is Detour's
adaptive sampling (its four presets) around 0.7 of the desired velocity, offset right by 0.04 of
it, candidates weighed by their distance from the desired (1) and current (0.5) velocities and their
time to impact (2.5) within `NavMesh.avoidancePredictionTime`, against neighbours' velocities alone,
no side penalty; a desired velocity that meets nothing within that time is kept. Neighbours count as
important as or more than the agent; separation pushes 1.4·(1 − (d/2.5r)²) from the more important
(half from equals); overlaps are resolved by the less important agent. NavMesh edges count up to the
next corner. Braking: once the end is nearer than two radii at the agent's speed relative to full
speed, the speed falls by v²/2d per second, at once.

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

Left (the remaining differences): near-ties between two sides once agents move (a few percent of
the records of the first sets), grazing paths at exactly the radii, separation from a neighbour
behind in the first frames of a path, and the long runs, where any of these carries on.
