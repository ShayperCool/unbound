# Motion status and query dependency handoff

The 45-value motion objective is not complete. No contact-phase, constraint-order,
proxy-order or rotation candidate is production code. Their full captures,
union-key comparisons, source patches and artifact hashes remain in Evidence.
The only runtime change on this branch is measured BoxCollider2D skin capture.

## Contact phase evidence

Two independent CPU Unity 6000.0.71f1 processes agree on every raw value in each
motion recording pair. The complete-sequence pair also reproduces all 29
historical broad scenarios exactly. Original physics inputs and references were
not edited.

At original bounce step 49, Native records the incoming body position
(0.9599996, 0.385377526), callback point (0.9599996, -0.05231124), separation
-0.124622464 and incoming relative velocity (-1, 9.4176). The callback body
position is already (0.9721025, 0.539198339), with solved normal impulse
17.30484. GetContacts returns that same evaluated geometry after simulation.
Normals on rotating polygon contacts follow the later orientation. Caching the
whole world manifold therefore changes accepted normal observations.

Candidate2/source.patch keeps evaluated point, separation and point velocity,
with a live normal. It resolves the 17 assigned enter-geometry observations in
the broad comparator, but introduces 15,066 observations in the first expanded
motion suite. Those failures include values near the comparator boundary in
already divergent stacks. The report preserves all of them; this is not an
accepted patch proposal.

Candidate3/source.patch additionally sorts an island's constraints by real
contact creation order. It resolves 44/45 original motion values and introduces
zero original broad failures, retaining angularVelocity at kinematic step50.
It introduces 51,488 observations in the complete-sequence expanded suite and
is rejected. Candidate4 sorts pending pairs by their real proxy IDs, retains
27 stack failures again and introduces 495,804 expanded observations. It is
also rejected. These rules cannot be certified from the original broad stack
alone. There is no demonstrated requirement to edit a file outside motion
ownership to finish the investigation.

A separate 36,000-value Native pair distinguishes rotation-based shortest arcs
from degree DeltaAngle at full turns. The rotation candidate raises exact
matches from 12,417 to 27,384, with all 36,000 passing the unchanged comparator
in both cases. It still leaves ball.s50.angularVelocity at 9.062741 rather
than Native 9.052233 and resolves no broad failing keys. It is retained as an
unshipped experiment. Its source patch and assemblies are private under
rotation1-artifacts and are hashed in Evidence/extra-artifacts.json.

## Captured box skin

Root supplied the independently agreeing 46-case, 644-observation pair with
SHA256 ac3955120dd650216f983a642d825d28eb16e40696053b175e6d249adb12d8ba.
The source and both raw captures are preserved in Evidence/skin-dependency.
The 46 new managed cases verify the actual fixture skin across those exact
transitions. The first skin draft missed pending transform changes at Rebuild;
its six test failures, source and binaries are retained. The corrected patch
passes all 46 transitions and all 384 original integrated tests.

BoxCollider2D now captures the base skin on registration, actual size/offset or
trigger changes, measured transform changes and body attachment. Enable,
activation and clones use their real registration path. Deferred Rebuild uses
that captured skin plus authored edge radius. Assigning unchanged size/offset
or scale, syncing, material changes and density changes retain the skin.
This patch does not change circles, capsules, polygon/edge/chain skin capture,
query construction, GJK/TOI or global settings.

The old query helper still produces the same 171 failures in the 644-value
root diagnostic. Baseline and final have identical failure keys and all failures
are retained in Evidence/validation/summary.json. The query candidate was not
merged or used as an accepted baseline. Combining it with this owner patch
still needs the query owner's Native suite replay.

Remaining query-owned integration points:

* Physics2DQueries.cs must consume the actual fixture radius when the query
  offset differs from the collider's captured skin. The old implementation
  uses the current global PolygonRadius in its cast calculations.
* PhysicsShapes.cs currently subtracts current global PolygonRadius from a
  polygon fixture radius at line 282. With captured box skin that difference
  can include stale/global skin, rather than authored extrusion. The Box
  collider's edgeRadius is available to the query owner. Any GetShapes or
  GetShapeGroup change needs its own Native observations; the supplied 644
  values measure casts and bounds, not these APIs.
* Polygon, edge, chain, composite, nonzero extrusion and further hierarchy or
  tiny transform transitions remain unmeasured here. This branch does not
  generalize the Box lifecycle rule to those types.

No file outside the stated ownership was edited. No query candidate, PR, merge,
allocator padding, fabricated contact state, tolerance change or failure mask
was used. Older releases remain unmeasured. No full-engine parity claim.
