# DotRecast

C# port of Recast & Detour (Mikko Mononen's navigation mesh library, which Unity's NavMesh is built
on), by Choi Ikpil, from <https://github.com/ikpil/DotRecast> (main branch, October 2026), under the
zlib licence (`LICENSE.txt`). The projects used are DotRecast.Core, DotRecast.Recast,
DotRecast.Detour and DotRecast.Detour.Crowd, compiled into the engine.

Altered (marked `Unbound:` in the sources):

- every top-level type is `internal` (it is not Unity API);
- A* nodes compare by total cost alone and a modified node only bubbles up, as in Detour;
- a point on a polygon edge belongs to the polygon whose maximum the edge is (Unity's choice);
- the nearest polygon to a point is ranked by distance, a polygon under or over the point by its
  height minus the climb (negative within it: the nearest in height wins), as Unity's queries choose;
- of equally near polygons the last one found is the nearest (Unity's: ties on shared edges);
- A* places a node at the portal's point nearest the node it is reached from (not at the portal's
  middle), moves it when a cheaper way reaches it, and ranks the ways by their cost alone (Unity's
  paths then weigh area costs by the distance really travelled: they go around a water area that
  the midpoints would cross, and take the Editor's side round symmetric obstacles);
- the closest point of a polygon to a position outside it is the closest in 3D on its boundary
  (Unity's: on a slope it moves along the edge), not in the xz plane;
- rasterized spans take the cell above the surface (`floor + 1`), as Unity's voxelization does;
- crowd agents as Unity's (`tests/projects/avoidance`): each update first steps the velocity
  towards the previous update's plan (`DtCrowdAgent.pvel`; a braking agent's speed follows it at
  once), then steers with that velocity in 3D (a corner above slows them in the plane) and plans;
  they brake once the end is nearer than two radii scaled by their speed over full speed, losing
  v²/2d per second, steer to the end however close, and land on it when a step reaches it;
- crowd avoidance as Unity's: Detour's adaptive patterns around 0.7 of the desired velocity offset
  right by 0.04 of it, candidates weighed 1 (desired), 0.5 (current) and 2.5 (time to impact)
  within the prediction time, against the neighbours' own velocities (not RVO's halves), no side
  penalty, grazing paths and overlapping neighbours moved away from free; a desired velocity meeting
  nothing in time is kept whole; NavMesh edges count up to the next corner (32 edges, 64 polygons
  of local boundary) with their time to impact as it is (Detour doubles it); neighbours count by
  avoidance priority (as or more important than the agent);
- crowd separation and collisions as Unity's: a push of 1.4·(1 − (d/2.5r)²) from more important
  neighbours (half from equals), overlaps resolved by the less important agent alone (half each
  between equals);
- `DtNavMeshQuery.GetPathToNode` is visible to the engine (partial paths end at the reachable
  polygon nearest to the target);
- off-mesh links as Unity's (`tests/projects/navlinks`): a link's end connects within the agent's
  radius of the mesh, a wide link's only where it lies on the mesh (`tests/projects/navmesh`), and
  lands on the point of the detail surface closest to it in 3D; an agent enters a link when the step of
  the update has brought it within 2.25 radii of the link's start, and takes its first step on it
  in the same update; the crossing is Detour's (15% from where it entered to the link's start,
  then to its end) but advanced as a fraction of the start-to-end distance at twice the agent's
  current speed, frozen while the agent does not traverse links itself or is stopped, and ends
  at the link's own end (also off the mesh); the update that passes the end moves the agent on
  along the mesh by the rest of the step, which becomes its velocity (at most its speed); the
  desired velocity on a link points along the current part; the corridor keeps the link's end as
  its position (`DtPathCorridor.SetPos`); partial paths are searched again by the engine, not by
  the crowd; a wide link's ends are segments (its portals), crossed straight where the path meets
  them; a link's corners are kept at both ends, a ground corner in line with the funnel's other
  side narrows it, a path starting on a link's start is on the link, and a path's end merges only
  with a corner exactly at it; in the update an agent leaves a link onto a path ending in the
  polygon it lands in, it reports no path (`NavMeshAgent.hasPath`);
- edges count for avoidance up to the next corner, but only those before it when the corner is
  the path's end or a link's start (an edge the agent touches beyond them neither); a braking
  agent takes its plan at once.
