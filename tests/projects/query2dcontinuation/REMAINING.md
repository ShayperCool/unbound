# Remaining query work and integration limits

The starting commit is an isolated diagnostic candidate. This continuation does
not merge or cherry-pick the active motion or ContactEvent owners' work.
The accepted baseline remains 88a53c477dc43e646c21522f6c402c0cec98c6c1.

The main recording's remaining 3,192 failures all concern points or normals of
initial-overlap hits. They repeat across directions and lateral offsets because
the fixture starts each of those casts at the same target center. The remaining
breakdown is capsule queries 1,152, circle queries 1,104 and box queries 936.
These are actual failing observations, including repeated values, not unique
geometries. No key was removed or treated as an expected failure.

For example, in `box.23.1`, `0.0.True.circle.normal` is Native
(-0.3907312, 0.9205049), while the starting and final candidate give approximately
(0.390731126, -0.920504868). Their points are respectively
(0.2785098, -0.14937219) and (0.321490228, -0.250627726). This is a contact-feature
tie, not a failed sweep. A blanket flip would break the cases already matching.
The existing geometry recording covers support vertices at tolerance but does
not establish a general near-center tie rule. Further changes need controls
that displace query centers on both sides of each tie and vary body/collider
frames. Query-specific contact reconstruction remains eligible for future work.

There is also a possible numeric dependency outside query ownership.
`Physics2DWorld.ToBodyMatrix` computes inverse body TRS times collider TRS, even
when the collider and its static body share a frame. `Collider2D.BuildShapes`
then consumes that matrix. Cancelling rotations can leave float residuals that
matter at exact feature ties. This is a source-based investigation lead, not a
Native-certified cause or authorization to change those files. Do not force a
query-space offset or change shared solver tolerances to hide it.

The confirmed nonowned dependency remains captured polygon skin. Root's two
Native skin lifecycle recordings agree on all 644 observations, including
refresh on changed Transform pose, activation, trigger and body transitions.
The motion owner owns that correction. The inherited selection, fine-selection,
boundary and skin failures stay visible in this stage's complete reports; passing
broad gates does not establish integration acceptance. After an authorized skin
integration, replay all these suites again without changing their tolerances,
captures or checkers.

The query-only changes leave common GJK, common manifolds, solver TOI, collider
construction, World, Runtime, 3D and native code unchanged. Older Unity releases
remain unmeasured. No full engine or full query parity claim is made.
