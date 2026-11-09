# NavMesh path-cost, raycast-area and agent-placement probe

Level and query scenarios on NavMeshes built at run time (`NavMeshBuilder` from box sources: a floor,
cost areas level with it and holes cut into it), recorded identically in the Unity Editor and in
Unbound by `PathProbe` (`Assets/Scripts`). Parts sit off the 1/6 m voxel grid so that the builders
agree on the mesh (`meshN.vertices` is logged for every level to show it).

| scenario | what it records |
| --- | --- |
| `cost.pool`, `cost.offset`, `cost.strips`, `cost.mixed` | `NavMesh.CalculatePath` corners over pools, strips and holes at several area costs, ends and places (which way round a cost area the route goes, and where it crosses) |
| `hole.symmetric`, `hole.shapes` | routes round holes moved a little off the line between the ends, a wall with a gap, two boxes, a turned box, an L, a pocket |
| `carve.sides`, `carve.mud` | the same round carved `NavMeshObstacle` boxes, with the carved tile's polygons (`polyX.pN`: vertices in order, sorted) |
| `ray.area` | `NavMesh.Raycast` over water and mud strips, a wall and rays from far off the NavMesh, with area masks excluding the areas |
| `snap.enable`, `snap.warp`, `snap.destination`, `snap.reach`, `snap.reachtype` | where the agent's transform is right after `AddComponent`, activation, `enabled`, `Instantiate`, `Warp` and `SetDestination`, in the update and LateUpdate of the next frames, and how far from the NavMesh an agent is still placed |
| `link.snap` | links over a 2 m gap whose ends lie either side of the agent radius (0.5 m, and 2 m for a second agent type) across and either side of the climb above: which connect (paths over them or round by another link) |
| `link.wide` | 4 m wide links: paths straight across at several lateral offsets (also beyond the width), one whose start lies partly off the NavMesh; agents crossing them one at a time (nextOffMeshLinkData, currentOffMeshLinkData, where they end) |
| `link.wideoff` | a 4 m wide link whose ends lie 0.2 m off the NavMesh across the gap: a wide end does not attach (no snap across it) |
| `link.repath` | three agents on partial paths over inactive links that open at frame 30: autoRepath on near the gap, autoRepath off, autoRepath on far away (status and isPathStale every frame) |
| `sync.updatePosition` | `updatePosition` turned off while the transform (in Update or LateUpdate), `nextPosition` or a walking agent moves, then on again (and a move right after); transforms moved by script with it on, in Update and LateUpdate: the transform and `nextPosition` right after, in LateUpdate and in the next frames |

    python3 tests/projects/navpath/run.py editor  --label L              # two Editor captures
    python3 tests/projects/navpath/run.py publish --label L              # both equal: the reference
    UNBOUND=<binary> python3 tests/projects/navpath/run.py unbound       # Unbound against it

`--scenario PREFIX` runs only the scenarios starting with it (`editor` and `unbound`).

Measured (Unity 6000.0.71f1, `Reference/unity-6000.0.71f1.json`):

- Paths follow the cheapest way as the search weighs it: each polygon's node sits at the portal point
  nearest the node it was reached from (placed once), a way costs the distance times the area cost
  of the polygon it crosses, the estimate is the distance from the end to the polygon. Pools cross
  or go round at the costs where the two ways tie in that weight, not in the straight-line length.
- A straight path through a mesh vertex exactly has no corner there: the funnel makes a corner only where a
  portal lies strictly beyond a side, with offsets from the apex within 1e-4 counted as zero.
- A ray stopped at an edge by the area mask reports the refused polygon's area; stopped by a wall, none;
  a start far from the NavMesh is no hit (infinite position and distance).
- `AddComponent` on an active object puts the transform on the NavMesh at once. Activation, `enabled`
  and `Warp` keep the transform (a warp: the given position) until the next frame's AI update
  (neither the same frame's update nor its LateUpdate); `nextPosition` is on the NavMesh at once.
  Agents are placed up to 20 radii sideways and 15 radii in height from the NavMesh (the agent's own radius: measured with 0.5, 1 and 2; not its height or agent type).
  `baseOffset` set moves the transform.
- `updatePosition` turned on again moves the transform to `nextPosition` at the next frame's AI update, not in
  the setter; a transform moved while it was off (in Update or LateUpdate) does not move the agent, one moved
  after turning it on does; turning it off takes in a script move made before. A transform moved by script
  with `updatePosition` on, in Update or LateUpdate, goes onto the NavMesh at the next frame's AI update.

- A carved hole is cut per group of connected polygons of a tile: the group's hole is the convex hull
  of what the hole covers of it; each polygon is cut by the hull's edges into triangles (each edge
  inside the polygon with the polygon's corner farthest outside it, fans from the hull's corners over
  the corners between), merged in order into convex pieces of at most six vertices (`polyX.*`), and the
  routes round the hole follow those pieces.

- A link's end attaches within the agent type's radius across and its climb in height. A wide link's
  ends are segments attached only to the polygons they cross (no snap): paths cross it straight at
  their offset, held to the part of each end over the NavMesh; an agent's next link is reported from
  where its path reaches the link.
- A NavMesh change does not replan a path: a stale partial path is searched again (autoRepath) once
  its end is the agent's next corner within its radius; with autoRepath off it stays partial and stale.

Listed, not gated (`run.py` header): `two.p1`, a line grazing a vertex: the builders place that vertex
about 2e-6 apart, on either side of the line; `link.wide` a3 (a path leaving a wide link beyond its
width) and where the agents walking to a wide link reach and cross it (ga*, gb*, the keys listed).
