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
- crowd agents brake with the square root of the distance left within 1.9 radii, can opt out of
  braking, and do not avoid NavMesh edges unless asked (Unity's agents);
- crowd agents steer towards their corner in 3D (a corner above them slows them in the plane),
  integrate the velocity planned by the previous update (`DtCrowdAgent.pvel`: Unity's agents act on
  their steering one frame late), and with nothing to avoid take the desired velocity as it is;
- crowd agents carry Unity's avoidance priority: an agent avoids only the neighbours as or more
  important than itself (a lower or equal `avoidancePriority`);
- `DtNavMeshQuery.GetPathToNode` is visible to the engine (partial paths end at the reachable
  polygon nearest to the target).
