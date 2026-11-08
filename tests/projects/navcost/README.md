# NavMesh path cost, raycast mask and agent placement probe

A scene with a legacy-baked NavMesh (`UnityEditor.AI.NavMeshBuilder.BuildNavMesh`) of nine 12 m
rigs, each on its own ground (`Assets/Scripts/Rigs.cs`), measured identically in the Unity Editor
and in Unbound by `CostProbe` (`Assets/Scripts`):

| scenario | what it records |
| --- | --- |
| `strip.strip1`, `strip.strip2`, `strip.rays`, `strip.rays.water` | a Mud (or Water) strip across the way, open at one end: the area cost swept from 1 to 30 for crossings at several x, back, and diagonal, then the far end swept across at costs 2 and 6 |
| `bake.box`, `bake.bar`, `bake.turned` | round a baked obstacle (a 2 m box; a 1 x 3 bar off the grid; a turned 3 x 1 bar): the far end, the near end, the way back, a near pair and a side pair swept across |
| `carve.carvebox`, `carve.carvebar`, `carve.carvebar.mud` | the same round NavMeshObstacle holes carved at runtime (the box, the bar beside a Mud patch, whose cost is swept) |
| `carve.*.mesh` | the carved NavMesh's vertices and area per NavMesh area |
| `fans.*` | the carved NavMesh's triangles (listed, not gated: the first vertex of each polygon's fan) |
| `raycast`, `raycast.agent` | NavMesh.Raycast stopped where the mask excludes Mud or Water (from either side, over an allowed area, starting inside an excluded one), at a border, and clear; NavMeshAgent.Raycast with the agent's mask |
| `place.*` | standing agents' transform and nextPosition from Awake, at Start and in every Update and LateUpdate of 14 frames: agents saved in the scene (at the ground's height, above it, beside the NavMesh's edge), added in Awake and later by AddComponent, activated, re-enabled, warped, moved by script in Update and LateUpdate, with baseOffset set, and with updatePosition turned on |
| `walk.*` | the same for walking agents |
| `loop.*` | where in the player loop walking agents change (`Assets/Scripts/LoopTiming.cs`, after `place.*`): 21 lanes in the strip, obstacle and rays rigs, each one mechanism (a destination set in Update or LateUpdate, on the agent's polygon or needing a search, re-set ahead, behind, to itself or beyond the NavMesh; updatePosition or updateRotation off; transform.position, nextPosition, Move, Warp, velocity, isStopped set in Update, or the transform or nextPosition in LateUpdate; a short path's arrival; a link crossed; an agent made in Update), recorded after `PreUpdate.AIUpdate` (`a`), in Update (`u`), after `PreLateUpdate.AIUpdatePostScript` (`s`), in LateUpdate (`l`) and right after the scripted changes (`c`, `d`) of 20 frames |

A route is the path's interior corners (NavMesh vertices, which stay fixed while an end moves), so
each sweep records its first route and every change of route, bisected to the parameter where it
happens; one cost model has to give every threshold. Agent positions are relative to their rig, so
the 1e-3 tolerance is absolute for them.

    python3 tests/projects/navcost/run.py setup   --release 6000.0.71f1 --label L   # scene + bake
    python3 tests/projects/navcost/run.py editor  --release 6000.0.71f1 --label L   # two Editor captures
    python3 tests/projects/navcost/run.py publish --release 6000.0.71f1 --label L   # identical? -> Reference/
    UNBOUND_CLI=.../unbound python3 tests/projects/navcost/run.py check --release 6000.0.71f1  # the gate

`check` runs Unbound on a fresh copy under `$XDG_CACHE_HOME/unbound/validation/runs/navcost/` and
compares it with the reference using `tests/projects/navmesh/compare.py` (numbers within 1e-3,
text exactly; every missing, extra or differing value is listed). The agents' frames are compared
per agent (`walk.<agent>`, `loop.<lane>`). It gates every scenario except `fans.*` and the lanes
`loop.redirect`, `loop.redirectSame`, `loop.redirectOff` and `loop.velocity`, whose residuals are
in docs/COMPATIBILITY_GAPS.md (the path cost, agent update and local avoidance rows).

The reference `Reference/unity-6000.0.71f1.json` holds 31857 values, SHA-256
`d17b809a6d27a8550cd87cfb14f542847acb82229cbadf7e46f50efab4a330f7`.
