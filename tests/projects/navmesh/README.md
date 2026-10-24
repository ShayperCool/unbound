# NavMesh probe

A level baked with com.unity.ai.navigation's NavMeshSurface (ground, boxes, a ramp up to a platform,
a step, a pillar, a water volume and a mud modifier, an island behind a NavMeshLink), and scenarios
run identically in the Unity Editor and in Unbound: the baked NavMesh's triangulation, settings,
`SamplePosition`/`Raycast`/`FindClosestEdge`/`CalculatePath` (area masks and costs), runtime builds
(`NavMeshBuilder` from collected sources, `NavMeshSurface.BuildNavMesh`), and six agents moved for
250 frames at a fixed 0.02 s step (paths, braking, avoidance, a partial path, a ramp, a carving
obstacle dropped at frame 60). `compare.py` lists the differences.

The level and its baked `NavMesh.asset` are made by `ProbeSetup.Create` and committed:

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/navmesh \
        -executeMethod ProbeSetup.Create -logFile /tmp/setup.log

Record Unity's reference:

    NAVMESH_PROBE_OUT=$PWD/tests/projects/navmesh/Reference/unity-6000.0.71f1.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/navmesh \
        -executeMethod ProbeRunner.Record -logFile /tmp/navmesh.log

Run Unbound and compare (the agents need 250 frames):

    NAVMESH_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/navmesh --no-watch --frames 300
    python3 tests/projects/navmesh/compare.py tests/projects/navmesh/Reference/unity-6000.0.71f1.json /tmp/unbound.json

`NAVMESH_PROBE_ONLY=<prefix>` runs only the scenarios whose name starts with it (`agents` for the
agents). `UNBOUND_NAVMESH_DUMP=<path>` makes Unbound write each runtime build's input geometry to
`<path>.<id>.txt`, for working on the build outside the engine.
