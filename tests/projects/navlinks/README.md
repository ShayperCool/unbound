# Off-mesh link probe

`NavLinksProbe` plays NavMesh agents over off-mesh links at a fixed 0.02 s step for 400 frames and
records every agent's state in every frame (in LateUpdate): position, rotation, next position,
velocity, desired velocity, steering target, remaining distance, path flags and corners,
destination and path end, `isPathStale`, `navMeshOwner`, `currentOffMeshLinkData`/
`nextOffMeshLinkData` and the link's `occupied`/`activated`. Each lane of `Lanes.cs` lies in its own 40 m cell: two 10 m platforms with
a 2 m gap only a link crosses (B 1.5 m lower in the drop lanes, a bridge in front in the cost
lanes). The level and its baked NavMesh are committed (`Assets/Scenes`); one lane's NavMeshLink and
another's legacy OffMeshLink are serialized in the scene, the other links are added by the probe.

| lanes | what they vary |
| --- | --- |
| `serialized`, `auto`, `legacy`, `oneway`, `reverse`, `reverseoneway`, `drop`, `climb`, `high` | the link components, directions, heights |
| `fast`, `slow`, `radius1`, `baseoffset`, `norotation`, `noposition`, `diagonal`, `turn` | agent speed, radius, base offset, updateRotation/updatePosition, turning onto and off a link |
| `startonlink`, `endatlink`, `shortafter`, `nobraking` | paths starting or ending at a link |
| `snap`, `snapnear`, `snapfar`, `wide` | link ends off the NavMesh (which connect), a 4 m wide link |
| `costlow`, `costhigh`, `areajump`, `areamask`, `query` | cost modifier, link area cost and mask; `NavMesh.CalculatePath` over a link |
| `two`, `inactive`, `inactive130`, `deactivatemid`, `deactivatewait` | occupancy; activating and deactivating links |
| `manualwait`, `manualmove`, `manualcurve`, `manualtoggle`, `autooffmid` | `autoTraverseOffMeshLink = false`: waiting, scripts moving the agent, `CompleteOffMeshLink` |
| `warpwait`, `warpauto`, `stopmid`, `resetmid`, `redirectmid`, `speedmid`, `completeauto`, `disableagent` | Warp, isStopped, ResetPath, SetDestination, speed, CompleteOffMeshLink, disabling the agent on a link |
| `offedge`, `island`, `aheadoff`, `pendingold`, `incall` | destinations beyond the NavMesh's edge (standing, walking ahead and behind), on another island and over the gap; the current path while a new one is pending; a path given in the SetDestination call |
| `targets` | `NavMesh.CalculatePath`, `NavMeshAgent.CalculatePath` and `SetDestination` to targets beyond an edge, a corner, above, below and over the gap, each bisected (17 steps, one frame per `SetDestination` step) for where it stops finding a path; agents of radius 0.25, 0.5, 0.75 and 1 and height 4 |
| `widetype` | the same for an agent of the second agent type ("Wide", radius 1.5, height 5, `ProjectSettings/NavMeshAreas.asset`) on a NavMesh built at run time |
| `low`, `lowmanual`, `crosstile` | links whose ends lie 0.3 m below the NavMesh (crossed, completed by script); a link across a NavMesh tile edge, crossed both ways (occupancy) |
| `turning`, `staleowner` | turning at another speed and angularSpeed (1.5 m/s and 40°/s, 6 m/s and 400°/s); areaMask and the agent's own area cost changed, a new destination |

`run.py` makes the level, records the Editor and runs Unbound, each run on a fresh copy of the
project under `$XDG_CACHE_HOME/unbound/validation/runs/navlinks/<label>` (labels are never
reused; Editors run through the shared heavy-job launcher on the GPU and exit by themselves):

    python3 tests/projects/navlinks/run.py setup   --release 6000.0.71f1 --label L   # level + NavMesh
    python3 tests/projects/navlinks/run.py editor  --release 6000.0.71f1 --label L   # two Editor process/copy captures from one setup
    python3 tests/projects/navlinks/run.py publish --release 6000.0.71f1 --label L   # identical? -> Reference/
    UNBOUND_CLI=.../unbound python3 tests/projects/navlinks/run.py unbound --release 6000.0.71f1 --label L
    python3 tests/projects/navlinks/compare.py tests/projects/navlinks/Reference/unity-6000.0.71f1.jsonl \
        ~/.cache/unbound/validation/runs/navlinks/L/unbound/values.jsonl --report ~/.cache/unbound/validation/runs/navlinks/L/report.jsonl

`compare.py` holds positions, rotations, corners and distances to 1e-3 and velocities to 2e-3 (the
NavMesh probes' tolerances), everything else exactly, and counts and reports every missing, extra
or failing value. The probe raises `NavMesh.pathfindingIterationsPerFrame` so that every lane's
path is found in the first frame; the per-frame A* budget is not measured here.

The first 29090 records of `Reference/unity-6000.0.71f1.jsonl` have SHA-256
`3a85a43b489b4303f8837638b26d51d624427fe96a1fdd29c8b50b8fc6b7792c`: the 19254 records of
the earlier reference (source `982cee918eab8018bce2a5b723e76f2079033592`), unchanged, with
`isPathStale` (`stale`) and `navMeshOwner` (`own`) added to each agent record, and the lanes from
`offedge` on. Its two Editor captures (concurrent processes in distinct project copies from one
setup) agree byte for byte. In two earlier pairs of captures of the same lanes the second agent of
lane `two` planned another desired velocity from frame 4 in one capture of each pair (avoidance
between the two agents varies between Editor runs); the published values are the ones the earlier
reference recorded. Staging this canonical file does not admit the reference or a runtime. Keep
raw repeats outside Git.

Select the release explicitly as above. The runner uses `tests/probe_paths.py` to
select that release for Editor paths, project copies and `Reference/`; the
comparison command reads the canonical reference directly. Set `XDG_CACHE_HOME`
when the cache is elsewhere, and place comparator reports under that cache too.
The setup and publish modes are explicit fixture/reference authoring operations;
normal run logs, copies, captures and comparisons belong in the cache. A setup
can replace scene/NavMesh assets, and publish can replace the selected reference.
Neither operation is part of source staging or authorized by this source task.

Traversal follow-up for Unity 6000.0.71f1: automatic crossing advances by the
current speed on every step. `speedmid` changes from 3.5 to 1 m/s during the
approach, and `stopmid` holds its position and desired velocity while stopped,
then resumes the same crossing. Managed controls also exercise zero speed, a
speed increase and stop/resume on a longer link. These observations refine the
automatic traversal profile; the API documents stop/resume of the current path
([isStopped](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-isStopped.html)),
but does not specify an interpolation curve or crossing duration.

The first differing frames of the sheriff-S19-a6f0d8af2 capture are `speedmid`
86, `stopmid` 86, `nobraking` 110, `crosstile` 68 and `two` 83. The last two
first differ in desired velocity, before their later positional differences.
`nobraking` first differs in `hasPath` on landing and later overshoots its
destination on frame 127. The speed and stop changes address the first pair;
landing path state, arrival, crowd steering and route geometry remain visible
in the unchanged comparator. A link's approach length relative to agent radius
is not covered by the current reference.

Q361/Q342 movement follow-up reduces strict failures from 9327 to 1993. The
reference and tolerances remain unchanged. Crossing agents supply their planned
velocity to crowd avoidance, while `velocity` still reports the frame's actual
position change. A waiting agent permits zero steering speed, including the
separation calculation. Braking takes the whole slower plan, so a tilted landing
velocity can become a horizontal walking velocity on the next update. When the final horizontal movement segment reaches the destination, the path
ends with automatic braking disabled too. Scripted movement sideways or away
does not count as arrival.

`costlow` and `areajump` each fall from 869 to 2 failing values; `nobraking`
from 2423 to 1; `two` from 1678 to 65; `crosstile` from 2369 to 939. The
remaining `crosstile` divergence starts with desired velocity on frame 86; the
remaining `two` divergence starts with rotation on 111. No lane's failing-value
count increases. Some previously matching keys within these changing
trajectories differ, including three path-end reports near arrival; the full
reports retain them. They are not assertions of universal parity.

Horizontal arrival remains open. Simply replacing the internal step's 3D norm
with its horizontal norm adds 785 failures in `endatlink`: the agent lands past
its destination on frame 110 and must return to it on 111. The final change
retains that existing behavior. Managed controls exercise a destination at the
landing, arrival without braking, and braking that changes heading.

Final Q361 first-frame grouping (strict comparator): `speedmid` has no failures;
`nobraking` has one hasPath value at frame 110; `stopmid` has two values, first
hasPath at 147; `crosstile` has 939 values, first desired velocity at 86; `two`
has 65 values, first rotation at 111. These are case totals, not independent
causes per value. The reference remains byte-identical.

Q375 preserves an agent's waiting state when a changed NavMesh restores a route
that still approaches a link. If disabling the occupied link replaces that route
with a partial walking route, it clears waiting. Automatic-link rotation follows
the current phase velocity: the approach direction first, then the crossing
direction. A stationary approach keeps its orientation.

The original lanes retain 578 failing values, down from 1993. `crosstile` falls
from 939 to 124; its first difference is now hasPath on frame 102, after its
previous frame-86 desired-velocity divergence is removed. `two` falls from 65 to
3, `startonlink` from 153 to 2, `diagonal` from 117 to 2, `turn` from 92 to 3,
and `inactive` from 102 to 2. These remaining values concern path state, path-end
reports or later movement. No original lane has more failures.

Four `waitrebuild.*` controls run after the original lanes. They change an
unrelated link or disable the occupied link, both while approaching and after
waiting is established. `waitingBeforeChange` is false in the early controls
and true in both `alreadywait` controls. Unrelated-link controls match every
value. Disabling the occupied link retains 28 early-control and 31 established-
waiting values: path and staleness reports, not an admitted tolerance. These
59 values stay in the strict comparator. That reference had 29354 records;
two GPU Editor repeats agree byte-for-byte, and the original 29090 records are
an unchanged byte prefix. The Q375 result was 637/1805662 failing values.

The API documents whether rotation is updated and its maximum angular speed
([updateRotation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-updateRotation.html),
[angularSpeed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-angularSpeed.html));
the phase direction and waiting restoration are observations for 6000.0.71f1.

Q385 turns about the agent's local up axis from a signed heading error, including
small angles, with the measured squared-speed threshold of 0.001. Up-axis alignment
runs independently of heading updates, using the current NavMesh or link orientation.
The endpoint remains the current corridor's endpoint when `hasPath` turns false.
A stopping-distance request selects zero planned speed before automatic braking;
braking can replace it with a braking speed. Resetting a path clears the braking
plan and subsequent movement decelerates at the agent's acceleration limit.

The previous 637 failures fall to 374. `aheadoff`, `two`, `turning` and fourteen
other previously failing cases now match; no previously passing value fails.
The additional controls cover both small-turn directions, heading wrap, speed below
and above the threshold, tilted agents, disabled heading updates, quaternion sign,
angular-speed limiting, placed surfaces, tilted links, stopping with and without
braking, resetting during braking, resuming after a smaller stop radius, and vertical
and inverted up-axis inputs. A singular adjustment uses identity local rotation,
including beneath a rotated parent. Unchanged alignment leaves `transform.hasChanged`
false; a changed alignment sets it. The flag behavior follows
[hasChanged, Unity 6000.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-hasChanged.html):
rotation assignments set the flag even for an equal value, so the agent skips an unchanged write. The reference appends 1,453 rows and 81,473
values, all matching. Two GPU repeats agree and every previous reference
record remains an unchanged byte prefix. Comparator tolerances are unchanged.

These rules are selected through Compat for 6000.0.71f1. Other releases retain
unmeasured quaternion interpolation, arrival-dependent endpoint reporting and the
previous immediate stopping-distance cutoff. Their equivalence has not been established.

[updateUpAxis, Unity 6000.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-updateUpAxis.html)
documents alignment to the current NavMesh or link. The tilted controls change the
orientation before the update, and the link's up axis differs from both platforms.
[stoppingDistance](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-stoppingDistance.html)
specifies a stopping radius, without requiring an instantaneous halt at its boundary.
The phase, endpoint and braking order above are measured release behavior.

Q385 remaining cases and their first differing frames follow. Counts include every
field at the existing tolerances; all remaining raw discrepancies stay visible.

| Case | Before Q385 | After | First differing frame | First field |
| --- | ---: | ---: | ---: | --- |
| `crosstile` | 124 | 120 | 102 | hp |
| `endatlink` | 82 | 82 | 1 | c |
| `areamask` | 63 | 61 | 75 | st[0], st[2] |
| `waitrebuild.alreadywait.disabled` | 31 | 31 | 20 | c, dst[0], nxt.e[0], nxt.e[2], nxt.owner, nxt.s[0], nxt.s[2], nxt.valid, pe[0], ps, rd |
| `waitrebuild.disabled` | 28 | 28 | 8 | c, dst[0], nxt.e[0], nxt.e[2], nxt.owner, nxt.s[0], nxt.s[2], nxt.valid, pe[0], ps, rd |
| `costhigh` | 11 | 7 | 75 | st[0], st[2] |
| `redirectmid` | 7 | 6 | 85 | c, hp, pe[0], rd |
| `completeauto` | 5 | 4 | 85 | dv[0], hp, n[1] |
| `disableagent` | 2 | 2 | 102 | v[0] |
| `areajump` | 2 | 1 | 29 | hp |
| `autooffmid` | 2 | 1 | 112 | hp |
| `baseoffset` | 2 | 1 | 110 | hp |
| `climb` | 2 | 1 | 112 | hp |
| `costlow` | 2 | 1 | 29 | hp |
| `deactivatemid` | 2 | 1 | 110 | hp |
| `deactivatewait` | 2 | 1 | 112 | hp |
| `diagonal` | 2 | 1 | 117 | hp |
| `drop` | 3 | 1 | 112 | hp |
| `fast` | 1 | 1 | 50 | hp |
| `inactive` | 2 | 1 | 129 | hp |
| `legacy` | 2 | 1 | 110 | hp |
| `low` | 2 | 1 | 110 | hp |
| `lowmanual` | 2 | 1 | 92 | hp |
| `manualcurve` | 2 | 1 | 108 | hp |
| `manualtoggle` | 2 | 1 | 141 | hp |
| `nobraking` | 1 | 1 | 110 | hp |
| `noposition` | 2 | 1 | 110 | hp |
| `norotation` | 2 | 1 | 110 | hp |
| `radius1` | 3 | 1 | 94 | hp |
| `resetmid` | 1 | 1 | 85 | dv[0] |
| `reverse` | 2 | 1 | 110 | hp |
| `serialized` | 2 | 1 | 110 | hp |
| `shortafter` | 2 | 1 | 110 | hp |
| `slow` | 1 | 1 | 234 | hp |
| `snap` | 2 | 1 | 108 | hp |
| `staleowner` | 3 | 1 | 110 | hp |
| `startonlink` | 2 | 1 | 29 | hp |
| `stopmid` | 2 | 1 | 147 | hp |
| `targets` | 1 | 1 | 130 | stale |
| `turn` | 3 | 1 | 114 | hp |
| `warpauto` | 1 | 1 | 85 | stale |
| `warpwait` | 1 | 1 | 92 | stale |

Q401 reports `hasPath` and `steeringTarget` from the corners found again after each
walking step. Leading corners within a centimetre of the agent in the ground plane are
dropped, the path's end included, unless they start a link. `hasPath` is true while
such corners remain, while the corridor holds more than one polygon, or while the
agent is on a link. An agent reaching a link finds no corners until it walks through
an update again. On the update it lands, automatically or by `CompleteOffMeshLink`, a
corridor of one polygon therefore reports no path, and `steeringTarget` is the
corridor's end. A step ending within a centimetre of a corner moves `steeringTarget`
to the next corner in that update. Movement still steers by the corners found before
the step; only these reports change.

[hasPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-hasPath.html)
documents only whether a path to the destination exists.
[steeringTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-steeringTarget.html)
documents the next corner or the path's end, and the link's entry and exit positions.
The landing update and the one-centimetre corner change are observations for
6000.0.71f1, selected through `Compat.NavMeshCorridorReportingMeasured`. Other releases
keep their previous reports, unmeasured there.

The previous 374 failing values fall to 325 (Mac engine run against the unchanged
reference). The single `hp` value at the landing frame of 29 lanes now matches; `crosstile`, `endatlink`, `completeauto` and `redirectmid` also lose values.
`costhigh` (7) and `areamask` (61 -> 54) no longer differ in `st` at frame 75. No
previously passing value fails. The same report rule takes the avoidance probe from 34
differing configurations to 3. Its records include `steeringTarget`. Remaining cases
and their first differing frames:

| Case | After Q401 | First differing frame | First field |
| --- | ---: | ---: | --- |
| `crosstile` | 118 | 103 | c[0][0], n[0], p[0], rd, v[0], v[2] |
| `endatlink` | 81 | 1 | c |
| `areamask` | 54 | 102 | dv[0], dv[2] |
| `waitrebuild.alreadywait.disabled` | 31 | 20 | c, dst[0], nxt.e[0], nxt.e[2], nxt.owner, nxt.s[0], nxt.s[2], nxt.valid, pe[0], ps, rd |
| `waitrebuild.disabled` | 28 | 8 | c, dst[0], nxt.e[0], nxt.e[2], nxt.owner, nxt.s[0], nxt.s[2], nxt.valid, pe[0], ps, rd |
| `redirectmid` | 4 | 85 | c, hp, pe[0], rd |
| `completeauto` | 3 | 85 | dv[0], n[1] |
| `disableagent` | 2 | 102 | v[0] |
| `resetmid` | 1 | 85 | dv[0] |
| `targets` | 1 | 130 | stale |
| `warpauto` | 1 | 85 | stale |
| `warpwait` | 1 | 92 | stale |

Remaining causes after Q401, by case:

- `endatlink` (81, the corners of frames 1 to 81): the reference's straight path keeps the
  link's end at z = 40.0000038 and the path's end at z = 40 as two corners. Unbound's
  crossing point is exactly the path's end and merges into it. Merging only exactly
  equal points was tried and changes nothing, because Unbound's crossing is exact.
- `crosstile` (118): from frame 103, when the occupied link frees and the waiting
  agent starts its approach, the approach direction differs by about 1e-4.
- `areamask` (54): the trajectory differs below a millimetre from frame 6; the desired
  velocity differs beyond tolerance from frame 102 (a flipped avoidance sample choice is
  a hypothesis, not traced).
- `waitrebuild.*` (59): path, endpoint and staleness reports after disabling the
  occupied link, as described above.
- `redirectmid`, `completeauto`, `disableagent`, `resetmid` and three staleness values
  remain as listed in the table above.

An arrival rule tested on the remaining arrival cases (a braking agent arriving when the
straight distance to the end is below its previous step) adds failures (325 -> 501,
and two configurations each in avoidance arrival, arrival-start and runs), so it was not kept.

Q426 updates link activation in place for 6000.0.71f1. A path query excludes a
newly deactivated link immediately. An agent retains its old corridor and next-link
data for the rest of that frame, with `isPathStale` set and the next link's
`activated` false. At the next AI update an affected walking route is repaired,
clearing its stale flag. This repair also occurs with `autoRepath` false. An
unaffected route keeps its stale flag, and re-enabling a link does not immediately
replace a distant partial route. Disabling and re-enabling before the update keeps
the complete route. Existing occupied traversals continue through activation changes.

The six `activation.*` controls have no occupying agent and cover near and far
approaches, both `autoRepath` settings and a same-frame toggle. All 2,034 added
values match, compared with 48 differences on the previous runtime. Two independent
GPU Editor captures agree byte for byte and retain all previous 30,807 records as
an unchanged byte prefix. The engine no longer rebuilds unrelated corridors on an
activation change. Paths completed by script retain the link's own end as their
first corner until the following walking update, as the existing manual controls
require.

Strict original failures fall from 325 to 258, with no newly failing value.
Both disabled occupied-link controls now match all values, previously 59 failures.
`redirectmid`, `resetmid` and `completeauto` also match. Remaining counts are
`crosstile` 118, `endatlink` 81, `areamask` 54, `disableagent` 2, and one each in
`targets`, `warpwait` and `warpauto`. Endpoint projection alone was tested and left
the strict count at 325; it was not changed. Other releases retain the previous
immediate rebuild and remain unmeasured for this activation timing.

[isPathStale, Unity 6000.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-isPathStale.html)
documents staleness after link changes.
[autoRepath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshAgent-autoRepath.html)
describes acquiring a path after invalidation and at the end of a stale partial
route. The immediate-query and next-update reporting sequence above is measured
behavior, not a timing guarantee from those pages.

Two `activation.pending.*` controls change the link while a path request is still
pending under a one-iteration budget. A request can publish the complete route it
already found, marked stale even though a fresh query is partial. The following
AI update repairs that route and clears its stale flag. Both controls record that
following update; an indefinitely fresh complete result is a regression. All 272 values match, previously 26 differing values. Their
GPU Editor repeats agree and preserve the previously committed reference prefix.
