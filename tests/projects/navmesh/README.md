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
previous reference bytes exactly. The polygon correction alone leaves 238 restored-baked-mesh frame differences;
Q385 neighbour prediction reduces that count to 212. No comparator or tolerance changed.
[BuildNavMeshData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html)
documents sources and local build bounds; it does not specify polygon layout.

Q385 neighbour prediction, measured on Unity 6000.0.71f1, inserts each agent over
its radius and the travel of its current velocity within the prediction time.
Climber's failing records fall from 196 to 3, right's from 225 to 1. This resolves
their initial divergences at 13 and 17. The 212 remaining frame scenarios include
walker from 33, blocked from 191, left's hasPath at 222 and right's arrival at 241.
Climber retains three three-decimal velocity-string differences at 21, 85 and 88;
raw vertical velocity differs by at most 0.0000254 m/s, height by 0.00000018 m.
They stay in the strict report and the insignificant-residual table.

`agents.predict.*` runs after the original 250 agent frames on a separate clear
floor: an approaching neighbour whose predicted bounds enter the query, the same
neighbour moving away, a stationary control and a stationary neighbour with a large
radius. Four more scenarios assign nonfinite velocity components or radius while
the agent is already moving; the Editor logs an error and retains its previous
motion and radius. Eight further scenarios exercise a radius-10000 obstacle and a fast diagonal
obstacle: both retain finite simulation state. All 608 values in these 44
scenarios match. Unit controls also require bounded grid storage and actual
neighbour admission for both obstacles; their exact trajectories are unmeasured. Two GPU repeats
agree byte for byte; removing just the new blocks restores the previous reference
bytes. Other releases retain point insertion and previous input handling through
Compat. Invalid or overflowing stored predictions also keep point insertion to
avoid an unbounded cell traversal. Grid cell sizes include the mean diameter
and predicted travel of both agents and the existing obstacle-circle proxies,
and oversized footprints use a pooled bounds tree instead of cell-by-cell
storage. Candidate coverage and cell-order admission are preserved; unit controls
include 9,999 distant participants and the 64-candidate limit.
Box obstacles retain their existing circle approximation, unmeasured here.

The remaining full-precision trace keeps distinct causes visible. Walker changes
its planned avoidance velocity one frame earlier, at 32 instead of 33. Blocked
advances its steering target later despite matching positions at 191. These are
observations in this capture, not a general timing rule; neither is hidden by a
new tolerance or a fixture-specific correction. Runtime height-mesh generation
remains unsupported.

[velocity, Unity 6000.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-velocity.html)
documents manual velocity control and that its effect appears on the next update;
[radius](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-radius.html)
defines the agent's avoidance space. Neither specifies proximity-grid insertion
or nonfinite-input handling; those rules above are observations for the measured
release, with controls where the agent is moving before the invalid assignment.

Q401 (Unity 6000.0.71f1 reference, Unbound on macOS arm64) leaves these scenarios
unchanged; its post-step corner reports change no navmesh value. Walker's divergence
is a tie. At frame 32 Unbound's walker steers straight at the corner
(-3.667, -8.5), which is also where the next NavMesh edge it avoids begins. Its
desired velocity's ray therefore meets that edge at the vertex where it starts, and
rounding places the meeting just inside the edge. The time to impact falls just below
the prediction time, and a ring sample wins over the desired velocity. The reference
keeps the desired velocity for one more update (frame 33 still moves at 3.499, 0.099);
how Unity treats a hit exactly at an edge's end point is inferred from that alone.
No constant or tolerance is changed for this, and the sampling weights are unchanged.
The other agents' residuals (blocked corners from 191, left hasPath at 222, right
arrival at 241, climber rounding) remain as described above.
