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
- A* as Unity's (`tests/projects/navcost`): a polygon's node is placed once, when the search first
  reaches the polygon, at the point of the portal from the polygon it is reached from nearest that
  polygon's node (in 3D, kept 5% of the portal's length from its ends; a portal shorter than 1 cm
  is its left end); a cheaper way found later changes the node's cost and parent, not its place;
  a polygon's heuristic is the distance from the end to the polygon's nearest point (times
  Detour's 0.999), and ways are compared by their total; there is no skip of nodes already reached
  from the same parent; `FindPath` runs the sliced search to its end (one search for both);
- the closest point of a polygon to a position outside it is the closest in 3D on its boundary
  (Unity's: on a slope it moves along the edge), not in the xz plane;
- rasterized spans take the cell above the surface (`floor + 1`), as Unity's voxelization does;
- crowd agents as Unity's (`tests/projects/avoidance`): each update first steps the velocity
  towards the previous update's plan (`DtCrowdAgent.pvel`; a braking agent's speed follows it at
  once), then steers with that velocity in 3D (a corner above slows them in the plane) and plans;
  they brake once the end is nearer than two radii scaled by their speed over full speed, losing
  v²/2d per second, steer to the end however close, and land on it when a step reaches it; they
  reach an off-mesh link from where the update's step took them and begin to cross it in that
  update (`tests/projects/navlinks`); `DtCrowdAgent.NextVelocity` gives the next step's velocity
  without taking it (the agents turn towards it);
- crowd avoidance as Unity's (`tests/projects/avoidance`, navcost `loop.redirect*`, navlinks):
  the agents' velocities step towards the last plan before anything else; an agent that avoids
  gathers its neighbours from a proximity grid of the agents' positions whose cells, along x and
  z, are the mean over every agent (all agent types) of its diameter plus how far it goes within
  the prediction time along that axis, at least 1 m; it queries the cells around the square of its
  radius plus how far it goes at full speed within the prediction time (64 candidates) and keeps,
  of the agents as or more important than it whose heights meet it and that are not behind it by
  more than both radii along the way it last meant to go, those it could reach within the
  prediction time with both at full speed, the soonest 8; its NavMesh edges are the nearest 8 of
  16 polygons within its radius plus half the way it goes within the prediction time, none while
  the corner it last steered by is the end of its path or a link within that reach (a path given
  between updates keeps that corner until the agent steers again; steeringTarget reports it);
  sampling: with nothing to avoid the desired velocity is kept as it is; else, in the horizontal
  plane relative to the agent, the desired velocity is the first candidate, then Detour's adaptive
  rings (the presets 5/2/1, 5/2/2, 7/2/3, 7/3/3, every other ring turned half a step, in order of
  angle) around 0.7 of the desired velocity offset right by 0.04 of it; candidates are weighed 0.2
  by their distance from the desired velocity, 0.1 from the current one (over the planning
  speed: braked at the end of a path) and 2 / (0.1 + time to impact / prediction time); a
  neighbour is met at the candidate's velocity against its own velocity shared by responsibility
  (the neighbour's alone when it is more important or does not avoid the agent in turn, half each
  between equals that avoid each other), an overlapping one at -2·tmin/tmax; an edge the agent is
  behind does not count, one nearer than 5 cm counts 2.5 cm in front of it, and an edge's time
  to impact counts double; an agent about to take a link keeps its desired velocity;
- crowd separation and collisions as Unity's: a push of 1.4·(1 − (d/2.5r)²) from more important
  neighbours (half from equals), overlaps resolved by the less important agent alone (half each
  between equals);
- off-mesh links as Unity's (`tests/projects/navlinks`): the crossing goes from its start on the
  NavMesh to the link's own end point (`DtNavMesh.GetOffMeshConnectionPolyEndPoints` also gives
  the connection's own end), taking half the time its 3D length takes at full speed, and plans
  along the link at full speed once past the first 15%; the update that ends it carries the agent
  on past the end at the crossing's velocity for the rest of the update, onto the landing
  polygon, and the agent leaves at full speed in the direction it moved from the link's end; a
  straight path starting at a connection's start marks that start as the connection's corner (an
  agent standing there takes it); an agent reaching a link another agent is on
  (`DtCrowd.tryEnterConnection`) plans to stand, without avoidance, until it is free;
- `DtOffMeshConnection.costModifier` holds a link's cost modifier, which the engine's query filter
  uses per metre in place of the link's area cost when it is zero or more (navlinks `costhigh`);
- `FindStraightPath` uses Unity's funnel (`tests/projects/navpath`, navcost route sweeps): the portal
  ends are offsets from the apex with components within 1e-4 counted as zero, put in funnel order;
  a corner is made only where the portal lies strictly beyond a side (a path through a portal end
  exactly has none), both sides are tested before either narrows, and a side narrows to an end on
  it or within it; off-mesh connection ends are added where the segment to the next corner
  crosses onto or off a connection (`AppendLinkCrossings`), so they stay corners also in line; a
  start within 2 mm of the first portal begins past it;
- `DtNavMeshQuery.GetPathToNode` is visible to the engine (partial paths end at the reachable
  polygon nearest to the target);
- `DtNavMeshQuery.Raycast` (`DotRecast.Detour/DtNavMeshQuery.cs`) computes the hit wall edge's z
  extent as its end's z minus its start's z (the port subtracted the start's x), so the hit normal
  is perpendicular to the wall that stops the ray (`NavMesh.Raycast`'s and `NavMeshAgent.Raycast`'s
  `NavMeshHit.normal`);
- `DtNavMeshQuery.Raycast` records the polygon beyond the hit edge that the filter excludes
  (`DtRaycastHit.blockedRef`; it finds the link holding the crossing before applying the filter),
  which `NavMesh.Raycast` reports as `NavMeshHit.mask`.
