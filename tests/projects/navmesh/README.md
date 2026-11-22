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
  there. Runtime height-mesh generation remains a separate unsupported feature (Q519 later implements it: tests/projects/navheight).
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
walker from 33, blocked from 191, left's hasPath at 222 and right's arrival at 241. (Q493 later resolves right's arrival.)
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
how Unity treats a hit exactly at one of an edge's vertices is inferred from that alone.
No constant or tolerance is changed for this, and the sampling weights are unchanged.
The other agents' residuals (blocked corners from 191, left hasPath at 222, right
arrival at 241, climber rounding) remain as described above (Q493 later resolves right's arrival.)

Q426 rechecks the remaining agents against two identical GPU Editor diagnostics
with `NAVMESH_PROBE_STEER=250`. At frame 191, blocked has exactly the same horizontal
position, velocity and desired velocity in both engines, but Unity has advanced its
steering target to (3.5, 3.5) while Unbound retains (-2, 4.5). The desired velocities
first diverge on 192 and positions on 193. This excludes accumulated movement drift
as the cause of that first corner disagreement. It does not establish a universal
corner-advance timing rule or justify selecting a particular topology-search order.

Left's frame-222 horizontal position and remaining distance agree at full precision
(0.003315133 m remains), while hasPath differs. Right's last moving frame leaves
about 0.0000786 m in Unbound, while Unity has arrived; both agree on the following
frame. These remain distinct path-state and arrival questions, with their raw
discrepancies retained. The walker tie remains deferred, as do the three climber
rounding records.

Removing straight-path endpoint projection alone leaves all 212 failing frame
scenarios and the avoidance residuals unchanged. That candidate was discarded.
The current navigation regression run retains every reference and tolerance. To
record the diagnostic without replacing a reference, run the existing editor and
unbound modes with `UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1` and
`NAVMESH_PROBE_STEER=250`; keep the added steering records in the run cache.

Expected failures (Q525, main on Linux and on macOS arm64, the same set on both): 212
frame scenarios, each failing only on the agents above: walker on 211 of the 218 frames
from 33 to 250 (the tie; it matches on 36, 37, 41, 45 and 53 to 55), blocked on 191 to
250 (60), climber's three rounding records (frames 21, 85 and 88) and left's `hasPath` at
222. Any other failing agent or frame is not one of these recorded residuals.


Q462 adds an optional corridor diagnostic with its own reference. It records frames
190 to 196 of the blocked agent, including its live path, a fresh agent path query,
and a global query whose start is held at the frame-191 reference position.
The default probe and reference are unchanged.

    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/navmesh/run.py editor --profile corridor --label L
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/navmesh/run.py publish --profile corridor --label L
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 UNBOUND=.../unbound python3 tests/projects/navmesh/run.py unbound --profile corridor --label L

The reference is `Reference/unity-6000.0.71f1/corridor.json`. Two GPU Editor repeats
agree byte for byte on the NVIDIA RTX 4060 renderer, and all original main-reference
values agree exactly. Other devices were not measured. All 422
values are compared with the existing tolerance, including missing and extra values.
The current diagnostic fails strictly in 103 values across six of seven scenarios;
frame 190 is the matching control. It has no listed-key exceptions and is separate
from the default carving gate.

At frame 191 both engines have the same position, velocity and desired velocity.
Both fresh queries keep the corner at (-2, 4.5), including with the same fixed
start. Unity's live corridor has already dropped that corner and steers towards
(3.5, 3.5), while Unbound's retains it. This distinguishes corridor history from
a disagreement in the static query at that position. It does not establish a new
shortcut order or threshold. Desired velocities diverge on 192 and positions on 193.
A fresh-query correction alone would not account for this first live-path change.

Q493 resolves right's frame-241 record: its last step left 0.0000786 m, within the braking
arrival distance measured by tests/projects/navarrival, so it lands in that update as in the Editor.
The frame stays counted because walker also differs there. Left's destination (6, -2.5) lies exactly
on the polygon edge z = -2.5, which left walks along, and its hasPath at 222 depends on which side
the boundary point falls on. Like walker's vertex, this is an exact-geometry tie; no rule is fitted.

The default rerun remains at 212 differing frame scenarios. The deferred walker
vertex tie, three climber rounding records and left's frame-222 hasPath remain
visible; right's frame-241 arrival is resolved by Q493 (above). Avoidance retains two near configurations and
one speed configuration. All three differ only in the second-frame steeringTarget,
whose x coordinate is 42.6666679 in the reference and 50 in Unbound; position,
velocity, desired velocity and next position match. This leaves corner or corridor
reporting open and does not justify changing avoidance weights. Their twelve raw
capture files are byte-identical before and after both Q462 corrections.

Q595: after a move that leaves the corridor's first polygons, Unity joins the polygon the agent
reached to the furthest corridor polygon it borders (tests/projects/avoidance `corners`). With
that join the corridor diagnostic matches in all seven frame scenarios (six differed without it),
the blocked agent matches from frame 191 to 231 (61 -> 19 of 253 values; it differs again from
frame 232 with an extra corner), and the avoidance configurations named above match. The default
rerun's differing frame scenarios stay at 212 (41 of them differ in fewer values).

## Route after removing a carved hole

The carving-route profile records the blocked agent as it walks past the parked
obstacle. At frame 230 one run turns off carving and the control leaves it
enabled. From frame 232 the cleared ground differs between the runs. In both
Editor runs the agent follows the same remaining route, with the same reported
corners, steering, velocity, and remaining distance. The hole that disappears
lies behind the agent on a tile its corridor still uses. In a third run a
new hole appears on a distant tile in the same update. Its open-ground sample
changes and the blocked agent retains its route. In a fourth run the new
obstacle sits directly above the removed hole, too high to cut the ground.
The reported ground and route match the removal run in every frame.

    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/navmesh/carving_route.py editor --label L
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/navmesh/carving_route.py publish --label L
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 UNBOUND_CLI=.../unbound python3 tests/projects/navmesh/carving_route.py gate

Two independent GPU Editor captures for each setting agree on all 3,174
values. The route check uses the existing comparator and reference tolerance.
The earlier broad capture still reports all its raw residuals. The Unity 6.0
API documents carving's effect on the mesh and pathfinding. The running
corridor through this edit is an observation from these controls.
[NavMeshObstacle.carving](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/ai/navmeshobstacle/carving),
[AI Navigation manual](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshObstacle.html).

When removing a hole replaces the agent's current polygons, Unbound reconnects
the affected prefix to the surviving ground route. This closes the 19 later
blocked-agent differences in the broad capture, so all 253 of its values match.
The control covers a complete ground path with a replaced prefix. A new
hole in any tile of that prefix, an intact prefix before the edit, partial
paths and off-mesh routes use the existing replan.

## Weighted edge projection

Moves clipped to a wall use the weighted sum of its endpoints in the
measured Unity 6000.0.71f1 profile. The earlier position drift along the
walker's first diagonal edge changed the velocity chosen near a later
vertex. With the projection corrected, the remaining five differences
are climber vertical velocity at frames 21, 85 and 88, walker vertical
velocity at 142, and left's path flag at 222. Each of those frames
also differed in the previous 212-frame comparison. The baked reference,
comparator and tolerances are unchanged.

## Baked surface height

The surface-height profile samples twelve positions on the baked NavMesh.
It also moves an agent to each horizontal position from a nearby start and
records the exact height from both operations. The controls include flat
ground, an oblique slope and two points on the far side of a detail-triangle
edge. The nearer point still takes the earlier triangle's height. The farther
point takes the adjacent triangle's height.

The Unity 6.0 API describes sampling the nearest point within range by
projecting along the surface instance's vertical axis, without testing
obstructions. It also documents that Move applies a relative offset and
adjusts an existing path. The interpolation and the edge rule were measured
on Unity 6000.0.71f1. [NavMesh.SamplePosition](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/ai/navmesh/sampleposition),
[NavMeshAgent.Move](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/ai/navmeshagent/move).

    python3 tests/projects/navmesh/surface_height.py editor --release 6000.0.71f1 --label L
    python3 tests/projects/navmesh/surface_height.py publish --release 6000.0.71f1 --label L
    UNBOUND_CLI=.../unbound python3 tests/projects/navmesh/surface_height.py gate --release 6000.0.71f1

Both Editor runs completed and wrote identical captures. The profile records
133 values, including the completion marker. The previous interpolation differs
in 22 exact height fields. Other release families use their existing,
unmeasured interpolation.
