# NavMesh probe

A level baked with com.unity.ai.navigation's NavMeshSurface (ground, boxes, a ramp up to a platform,
a step, a pillar, a water volume and a mud modifier, an island behind a NavMeshLink), and scenarios
run identically in the Unity Editor and in Unbound: the baked NavMesh's triangulation, settings,
`SamplePosition`/`Raycast`/`FindClosestEdge`/`CalculatePath` (area masks and costs), runtime builds
(`NavMeshBuilder` from collected sources, `NavMeshSurface.BuildNavMesh`), and six agents moved for
250 frames at a fixed 0.02 s step (paths, braking, avoidance, a partial path, a ramp, a carving
obstacle dropped at frame 60). `compare.py` lists the differences.

Carving: the carver (carveOnlyStationary off) blocks the `blocked` agent's lane from frame 60; a
parked obstacle with the default settings (turned 30 degrees) is added at frame 100, moved 0.05 m at
160 and 1.25 m at 180, and has its carving turned off at 230. Each frame from 58 logs `holes`
(whether the NavMesh is there at the carver and at the parked obstacle's two places, from Update
and from LateUpdate); `carve.carver` (frame 70) and `carve.parked` (frame 150) trace each hole's
outline with 16 rays and list the NavMesh vertices around it; `blocked.corners` is logged at frames
62, 100 and 200.

    python3 tests/projects/navmesh/run.py editor  --label <label>   # two Editor captures
    python3 tests/projects/navmesh/run.py publish --label <label>   # both equal: the reference
    UNBOUND=<binary> python3 tests/projects/navmesh/run.py unbound  # Unbound, then the carving check

`run.py unbound` writes compare.py's full report beside its capture and exits with the carving
check (the `carve.*` and `holes` values); the blocked agent's differences are listed, not gated
(its route past the hole is the path search's, see docs/COMPATIBILITY_GAPS.md).

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
`UNBOUND_NAVMESH_POLYS=<path>` makes Unbound write each agent type's polygons (reference, area,
type, vertices, then the links as `neighbour:edge`) to `<path>.<agent type>.txt`.
`NAVMESH_PROBE_STEER=<frames>` adds each agent's position, velocity, desired velocity, steering
target and next position in full precision for the first frames (in both engines, for working on
the agents; the reference does not have them).

Q361 build and movement isolation, Unity 6000.0.71f1:

- `build.sources` matches all 101 values on the starting base, with 193 vertices
  and 249 indices. This improvement predates Q361.
- `build.surface` still differs in 13 of 110 values. Both recordings have
  241 vertices and 321 indices, but 11 triangles on each side differ. A floor
  contour moves from x = -3.833 to -3.667, and ramp/landing triangles also differ.
  These are geometric differences, beyond the choice of a polygon's first vertex.
  The generating rule remains open; no surface correction is inferred from the
  matching source build.
- `BuildSurface` restores the committed baked data before creating the agents.
  Its mesh construction residual therefore does not explain the 238 differing
  agent-frame scenarios. The first differing serialized movement record is frame 13: the climber
  velocity is -1.732 versus -1.736 m/s. These movement fields are strings
  rounded to three decimals and compared exactly; path status and sampled
  corner counts agree.

Two new GPU Editor captures are byte-identical to the committed reference.
The full comparator still reports 239 differing scenarios, down from the
sheriff-S19 count of 240 because the source-build scenario now matches.
The existing comparators, raw differences and tolerances are unchanged.
[BuildNavMeshData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html)
documents sources and local build bounds; it does not specify polygon layout.
