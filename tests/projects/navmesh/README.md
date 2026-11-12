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

Q376 and Q385 runtime-build corrections, Unity 6000.0.71f1:

- Clip the horizontal build footprint to the source geometry before rounding to
  voxels. Padding beyond the sources previously allocated an extra voxel around
  `build.surface`, shifting its contour vertices and triangles. All 110 values
  now match, including the 241 vertices and 321 indices. `build.sources` remains
  101/101, including 193 vertices and 249 indices.
- `build.bounds.*` covers padded, tight, clipped and translated sources, an
  oversized modifier and a zero-area mesh. Q385 allows a one-cell monotone region
  to merge into a compatible neighbour, including at a tile border. This removes
  the translated floor's extra triangle: all 35 values now match, with 70 vertices
  and 96 indices. All nine bounds controls match, including negative, swapped-axis
  and elevated translations. The merge threshold is selected through Compat for
  6000.0.71f1; other releases retain the previous zero-cell threshold, unmeasured
  there. Runtime height-mesh generation remains a separate unsupported feature.
- `BuildSurface` restores the committed baked data before creating the agents.
  All existing agent records are unchanged by the correction. The 238 differing
  frame scenarios have distinct first divergences: climber at 13, right at 17,
  walker at 33, blocked at 191. The blocked agent first differs in corner count,
  4 versus 5, rather than position. Left differs only in `hasPath` at frame 222.
  Movement fields are strings rounded to three decimals and compared exactly.
  Path status agrees; corner counts differ in seven records: blocked on frames
  191 through 195, climber on 208, and right on 241.

Two new GPU Editor captures are byte-identical, including the three additional
translation controls. Removing only those new scenario blocks restores the
previous reference bytes exactly. The full comparator reports 238 differing
scenarios, all restored-baked-mesh agent frames. No comparator or tolerance changed.
[BuildNavMeshData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html)
documents sources and local build bounds; it does not specify polygon layout.

The full-precision agent trace keeps distinct causes visible. Climber's planned
velocity has a vertical component where the Editor's is horizontal before frame
13. Right's plan differs before its first rounded position difference at 17.
Walker changes its planned avoidance velocity one frame earlier, at 32 instead of 33. Blocked
advances its steering target later despite matching positions at 191. These are
observations in this capture, not a general timing or projection rule; the rounded
records and their original tolerances stay unchanged.
