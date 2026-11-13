# CCD, hinge and backface probe

The reference targets the Unity 6000.0.71f1 Linux Editor. The runner creates private
projects from the physics fixture, replaces its runtime scripts, and keeps logs,
repeats and engine results outside Git. The committed reference is published only
after two complete, byte-identical GPU Editor captures.

```sh
python3 tests/projects/physicsccdhinge/run.py prepare --release 6000.0.71f1 --label native-a
python3 tests/projects/physicsccdhinge/run.py native --release 6000.0.71f1 --label native-a
python3 tests/projects/physicsccdhinge/run.py prepare --release 6000.0.71f1 --label native-b
python3 tests/projects/physicsccdhinge/run.py native --release 6000.0.71f1 --label native-b
python3 tests/projects/physicsccdhinge/run.py publish --release 6000.0.71f1 --label native-a --repeat-label native-b
python3 tests/projects/physicsccdhinge/run.py gate --release 6000.0.71f1 --cli "$UNBOUND"
```

Every simulation step records pose, linear/angular velocity, callback phase,
relative velocity, impulse, contact count, points, normals and separations. Hinge
cases also record angle, velocity, force and torque. Cases cover sphere, box and
capsule bodies crossing a thin static or kinematic wall in all four detection
modes, a rotating bar, an edge grazing contact, moving kinematic plates in local
scenes, and an automatic FixedUpdate control. Stationary registration steps
precede prescribed motion. Kinematic movers use the two supported modes,
Discrete and ContinuousSpeculative.

Hinge cases cross both signs of the motor target and initial velocity, switch
motor settings on a live joint, connect two dynamic bodies, rotate through two
complete turns, apply ordinary and extended limits, cross the angle wrap, and
combine four contact distances with zero/nonzero restitution. Query cases cover
both sides of a single triangle and a convex tetrahedron using single, All,
NonAlloc, collider, and batch ray/sphere casts. The convex rays avoid shared
vertices so a tied face normal is not required. Batch backface overrides are
crossed with both global values and the global is checked after each command.

## Contracts and measured rules

These official Unity 6000.0 pages were checked, including their limitations:

| API or mechanism | Documented contract | What this capture establishes |
| --- | --- | --- |
| [ContinuousSpeculative](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CollisionDetectionMode.ContinuousSpeculative.html), [CCD overview](https://docs.unity3d.com/6000.0/Documentation/Manual/ContinuousCollisionDetection.html), [speculative CCD](https://docs.unity3d.com/6000.0/Documentation/Manual/Speculative-CCD.html) | CCD supports box, sphere and capsule colliders. Speculative CCD supports angular motion and kinematic bodies, but can miss collisions and create ghost contacts. Sweep CCD predicts linear motion. | Per-step motion and contact callbacks in these geometries. ContinuousDynamic sweeps a Discrete kinematic target; Continuous does not. Stay callbacks expose the velocity entering the step. |
| [JointMotor.freeSpin](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JointMotor-freeSpin.html) | A free-spinning motor accelerates without braking. | A nonzero target permits coasting past that target in the same direction. A zero target still brakes. One-sided drive impulse bounds implement the observation. |
| [HingeJoint.extendedLimits](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HingeJoint-extendedLimits.html), [angle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HingeJoint-angle.html), [JointLimits](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JointLimits.html) | Extended limits permit a solver range of ±360 degrees. The angle is relative to the initial rest pose. Contact distance activates the limit inside its bound; restitution and bounce velocity control bounce. | The public angle still wraps at ±180. Solver limits clamp to ±177 or ±357 while getters retain assigned values. With no bounce, a zero contact distance requests the default angular pad. With bounce, the pad is zero regardless of the assigned contact distance. These solver configuration rules use Compat for this measured release. |
| [Physics.queriesHitBackfaces](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-queriesHitBackfaces.html), [QueryParameters.hitBackfaces](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/QueryParameters-hitBackfaces.html) | Backface triangle hits are disabled by default for rays and shape sweeps. | Scene and collider raycasts read the global value; batch commands read their own value. Convex hull exterior hits do not depend on triangle backfaces. All paths share the same query flag helper. |

The observations do not establish universal collision outcomes for all speeds,
geometry, releases or solver settings. In exploratory repeats a lone speculative
kinematic plate sometimes passed through a Discrete sphere and sometimes pushed
it. The committed pair records the pushing outcome twice with identical bytes;
the earlier raw repeats remain outside Git. This is a retained repeatability
limit, consistent with the documented possibility of speculative tunneling.

No importer or serialized cache output changes. The shim version changes with
its collider-ray signature and drive flag semantics. The motor patch adds no
constraint storage, scan, allocation or per-step configuration.

## Extended geometry capture

`--geometry` selects `Sources/GeometryEntry.cs` and the additional reference
`Reference/unity-6000.0.71f1/geometry.tsv`. Use the commands above with
`--geometry` on every invocation, including preparation and publishing. Each
capture records callback counts as well as every callback and contact, so a
missing callback remains visible even when its expected count is zero.

The shape matrix contains a sphere control, a convex box-shaped mesh, two child
spheres, two child convex meshes, and a convex mesh with a trailing sphere.
Static and kinematic boxes and triangle meshes are crossed with all four modes.
Terrain cases use a flat 33-by-33 heightfield and downward motion. Dynamic pairs
approach at 100 m/s each, with all 16 mode combinations and five shape pairings.
Moving kinematic hulls and compounds use `MovePosition` in Discrete and
ContinuousSpeculative modes. Stationary kinematic hull and compound targets are
also crossed with fast dynamic bodies in every mode. Automatic FixedUpdate
controls cover convex meshes and compounds. Every body disables gravity,
damping and sleeping; these translation cases lock rotation.

Tuning cases cross contact offsets 0.001 and 0.05 with default maximum
depenetration velocities 0.5 and 10, four body geometries, and all four modes.
They include both fast approaches and bodies created already overlapping a
wall. A live-default control records the settings of bodies created before and
after changing the global default, followed by a per-body override.

The additional Unity 6000.0 contracts checked for this capture are
[Rigidbody.collisionDetectionMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-collisionDetectionMode.html),
[Continuous](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CollisionDetectionMode.Continuous.html),
[ContinuousDynamic](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CollisionDetectionMode.ContinuousDynamic.html),
[Physics.defaultMaxDepenetrationVelocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-defaultMaxDepenetrationVelocity.html)
and [Collider.contactOffset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-contactOffset.html).
Continuous promises detection against static mesh geometry and eligibility as a
ContinuousDynamic target. ContinuousDynamic covers other bodies with CCD
enabled. The maximum depenetration speed must be positive; small values can
prevent full correction and large values can cause instability. Contacts are
generated inside the sum of the colliders' positive contact offsets.

The documented box/sphere/capsule support list is a limitation of the public
guarantee. The 6000.0.71f1 captures also show convex mesh and compound sweep CCD
against static meshes and terrain. This observation is confined to the measured
release and cases. Speculative convex meshes can still pass through the terrain
in this matrix, consistent with the documented possibility of missed contacts.
Q06 has no geometry exclusion in its mode mapping, body flags or pair filter;
its earlier reference measured only the three documented collider shapes.

`--geometry-controls` selects the supplemental
`Reference/unity-6000.0.71f1/geometry-controls.tsv`. It crosses stationary,
slowly approaching and fast opposing Discrete dynamic targets with both sphere
and convex geometry and target masses 1 and 1000. It also switches a triangle
mesh between dynamic and kinematic on a root and child collider, then crosses
a pending convex edit with a body-type change. Reference extensions use
`publish --extend-reference`, which refuses any changed or missing existing key.

| Mechanism | Measured result and implementation cause | Status |
| --- | --- | --- |
| ContinuousDynamic versus Discrete dynamic targets | The target can be swept, including when stationary. A sufficiently fast Discrete target can leave the projectile's swept bounds and be missed. Its destination bounds participate in broadphase, while an admitted CCD pair uses relative motion. The bounds explanation is an implementation inference from the crossed speeds and the PhysX path. | Fixed through Compat for 6000.0.71f1. Other releases retain the previous exclusion and remain unmeasured. |
| The step after a dynamic CCD impact | The Discrete target can move to the impact pose too. Its old cached shape transform then produced false penetration on the next step. | Fixed by refreshing the shapes of moved Discrete bodies from the existing CCD update list; see `native/physx/UNBOUND_PATCHES.md`. |
| Non-convex mesh body-type changes | Dynamic triangle meshes have no collision support; returning to kinematic restores both ray hits and CCD contacts. A pending convex edit must detach the old triangle shape before the actor becomes dynamic. | Fixed in the structural flush with one collider pass. |
| Native pair orientation and contact manifolds | Native pair direction changes impulse sign and the surface used for a separated contact witness. Convex manifold clipping/reduction also produces different points, slot order and sometimes contact counts. Those fields are observable API residuals; a complete general rule for matching them is unresolved. | Open contact-generation follow-up. Every raw mismatch, missing row and extra row remains in the strict report. |
| Speculative compound contact processing | Six symmetric contexts give the same full body readings after reflecting position-y and velocity-y. Their child contact order selects opposite lateral directions. | 43 reflected body rows are identified separately. The capture does not establish a universal contact-order guarantee. |
| Mixed hull/sphere body against a two-sphere kinematic target | `geometry/kinematic-target/4/2/3` has a different speculative manifold and motion magnitude. One child-pair exit moves from step 1 to step 2 for both bodies. Different clipping and contact processing order are the suspected cause, not a settled algorithm. | Open: five body readings and two callback-count readings. This is a significant residual, not float roundoff. |

The geometry gate checks all 2768 body readings and callback-count readings,
except that it explicitly reports the reflected readings and the one open mixed
compound context. The latter checks forward progress between x=-0.6 and x=-0.15,
consistent per-step position/velocity integration, and the measured delayed-exit
sequence. Exact motion magnitude remains open.
The complete contact trace remains a strict **report**. Q177 additionally gates
unmatched payload, body and callback-count differences, as described below.
Its records must be finite and structurally complete, including every declared
callback and contact. `verify_geometry.py` does not sort or rewrite captures,
alter tolerances, or report contact residuals as insignificant. The original
Q06 gate and supplemental controls still compare all fields, with the existing
verifier's separately retained native pair impulse directions.

Callback phase, collider identity and incoming relative velocity are checked as
unordered complete callback sets. A reflection uses one child mapping across
every step and receiver, derived from the authored y-separated compound children;
the full body trajectory must match that reflection. Contact counts remain
exact except for Stay callbacks of convex pairs in the two named speculative
contexts, which admit only their measured two/three-point alternatives. Sphere
contacts and Enter/Exit cardinalities remain exact. Contact payload identities
must agree within each pair and occur in the same
scenario/receiver's reference. The open mixed-compound context admits only its
reference callback pattern or the measured delayed-exit pattern, across all
five steps and both receivers.

## Results and retained differences

This section records the Q166 baseline and its construction analysis. The Q177
section below gives the current results and the added content gate.

The strict comparator is shared with physicsbatch and keeps its absolute 1e-4 /
relative 1e-5 tolerances. The engine matches 6708/6814 rows. All 106 raw failures
remain in the comparison JSON. The separate gate verifier identifies 97 hinge
angle readings with at most 0.005834882 degrees of quaternion roundoff, three
resting speed readings with at most 0.000341510 degrees/s, and six opposite native
pair impulse vectors in `ccd/thin/0/True/3/{0..5}/event/0`. The latter retain the
same contact set, normal, separation, magnitude, relative velocity and motion;
the native pair's impulse direction is not a self-facing contact normal. No
pair order or sign guarantee is added. Counts, missing/extra rows, contact data,
callback phases, body poses and all other numeric fields still gate at the
unchanged strict tolerances. Four verifier tests reject mutations of those fields
and out-of-bound numeric differences.

Validation: release build; all 3268 managed tests; engine-only gate with zero
unresolved checks. Existing physics, joints, physicsbatch and contactmodify3d
probes retain their results. The latter two retain their previously recorded
mesh slot permutations and ten contact-modification differences respectively.

At the Q166 baseline, the extended raw comparison retained 2441 differences across
12440 keys, including extra/missing contact slots. Of these, 2391 are contact
trace rows, 43 are reflected body readings, and seven belong to the explicitly
open mixed-compound motion/lifetime context. All other body and callback-count
readings match the unchanged strict tolerances. The supplemental controls match
all motion and contact data, retaining 24 opposite native pair impulse vectors.
The extended gate therefore certifies CCD geometry coverage and the fixed
mechanisms; it does not claim full speculative compound/contact-payload parity.

Extended validation: release build, stub generation, 3529 managed tests,
affected Rust tests and all three engine-only CCD profiles pass. Existing
physics, joints, physicsbatch and contactmodify3d gates retain their results.
The geometry verifier has mutation controls for changed motion/counts, missing
payloads and inconsistent reflections across bodies or steps.

## Contact ordering and content analysis

Q166 checked the Unity 6000.0 documentation for
[Collision.GetContacts](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision.GetContacts.html),
[Collision.contacts](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision-contacts.html),
[ContactPoint](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ContactPoint.html),
[ContactPairPoint](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ContactPairPoint.html)
and [Physics.ContactEvent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ContactEvent.html).
They define contact data and retrieval without specifying contact-slot, native
pair or compound-child order. GetContacts writes into the supplied array or
list; a list can grow. ContactEvent supplies read-only headers, pairs and points,
excludes triggers, runs after transform synchronization, and requires consumers
to finish using its buffers before the next simulation. None of these rules
establishes a solver-order guarantee. The CCD limitations in the contract table
above still apply, including speculative ghost contacts and tunneling.

`analyze_contacts.py` annotates the unchanged strict comparison. It matches
complete callback/contact multisets within one step and receiver, including
collider identities, normals, separations, relative velocity and impulse. A
callback cannot explain two different callbacks. A point on the opposite surface
witness, displaced by the signed separation along the normal, has its own
category. Those points are not classified as slot permutations.

```sh
python3 tests/projects/physicsccdhinge/analyze_contacts.py \
  tests/projects/physicsccdhinge/Reference/unity-6000.0.71f1/geometry.tsv \
  "$HOME/.cache/unbound/validation/runs/physicsccdhinge/geometry/engine.tsv" \
  --output "$HOME/.cache/unbound/validation/runs/physicsccdhinge/geometry/contact-analysis.json"
```

The retained 2441 strict differences partition as follows:

| Category | Raw rows | Meaning |
| --- | ---: | --- |
| Order only | 812 | The complete callback/contact payload matches after one-to-one slot matching. |
| Native pair impulse orientation | 172 | Contact payloads match; the pair impulse has the opposite direction and equal magnitude. |
| Opposite surface witness | 186 | The normal, separation and identities match, but a reported point lies on the other surface witness. Impulse orientation can also differ. |
| Unmatched payload | 1221 | Complete matching fails. This includes manifold points/counts, solver impulse distribution, and contacts after different body motion. These are not certified as order only. |
| Body readings | 48 | The six reflected contexts contribute 43 rows; the mixed compound contributes five. |
| Callback counts | 2 | The mixed compound's child exit moves from step 1 to step 2, observed by both receivers. |

The six reflected contexts are `geometry/dynamic/3/{0,1,3}/3`,
`geometry/kinematic/2/1/3`, `geometry/kinematic/3/0/3`, and
`geometry/kinematic-target/2/2/3`. Including the mixed compound gives seven
distinct scenario keys. In `geometry/kinematic-target/4/2/3`, step 0 has the same
four initial contact points, normals and separations, with a different impulse
allocation among the child pairs. The reference bullet velocity is
`25.4600449,7.784088,0`; Unbound reports `25.7584381,-5.54619646,0`.
The normal-response replay below isolates the initial divergence to the contact
sequence. Q177 corrects the actor/shape lifetime below. No global sort, child reversal or fixture-dependent engine rule
is added.


`replay_compound_order.py` reconstructs the first step in all seven affected
contexts from captured contact normals, separations, identities, and the
fixture's prescribed incoming motion. Its six position iterations and one
velocity iteration use the fixture settings; the 0.8 normal-response fraction
is the one used by the vendored PhysX contact preparation. Bodies have unit
mass or are kinematic, rotations are locked, and the measured contacts are
separated. The model is confined to that context.

At the unchanged comparator tolerances, the model reconstructs all 14 reference
body readings and all 14 engine body readings. Replaying the engine contacts in
the reference's child-pair sequence reconstructs all 14 reference readings too.
For the mixed compound, exchanging the middle two inputs changes predicted
velocity from `25.7584400,-5.5461922,0` to `25.4600525,7.7840900,0`; the latter
matches the Editor's `25.4600449,7.784088,0`. Predicted position differs from the
reference by less than 0.0000002 units. The original contact values and raw
comparison remain untouched. The gate saves the complete replay report beside
the contact analysis; this diagnostic adds no normative contact-order assertion.
Q177 resolves this construction/lifetime cause below, without sorting contacts.

The Q166 baseline had contact-content differences independently of motion. For example,
`geometry/static/1/0/3/1/bullet/event/0` has four contacts on both sides. Unity
reported the four face corners; Unbound retained an interior point and three
corners. Q177 corrects this manifold lifetime, as described below. The shared normal, separation and body motion match. This is a reduced
manifold difference, not a slot permutation or an insignificant-contact claim.

At the Q166 baseline, the supplemental controls retained 24 impulse-vector differences. Each has equal
magnitude and the opposite sign, with all contact geometry, incoming velocities
and body motion matching. The additional native controls below show that legacy
callbacks on both receivers preserve the native pair impulse, while contact
normals face their own receiver. Flipping every callback impulse toward its
receiver would violate this measured convention.

## Native orientation and convex frame controls

`--geometry-ordering` selects `geometry-ordering.tsv`. Record and publish it with
the same two-repeat workflow above. Its 64 construction cases cross four shape
types, Rigidbody-before-collider construction, child creation order, actor
creation order, and a dynamic or kinematic target. Native ContactEvent rows
record collider orientation, per-point impulses and the pair impulse sum.
`verify_ordering.py` requires native/legacy agreement for both receivers and
checks every declared payload, including finite values and missing reports.
The native and legacy sequences remain strict reports; the verifier adds no
ordering guarantee.

The frame controls cover a translated hull, nonuniform scale, rotation, a mirror,
a sheared hierarchy, and a hull whose local vertices are offset by 10000 units.
The Unity 6000.0 [MeshCollider reference](https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshCollider.html)
and [convex limitations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MeshCollider-convex.html)
were also checked. The documented convex limit and mesh-readability requirements
do not specify exact manifold reduction or floating-point results.

`frames/4` measures a convex child rotated 20 degrees under scale `(2,1,0.5)`.
Its reference bounds size is `0.4685012,0.443691134,0.200000033`; the previous
full affine hull produced `0.581089139,0.350311875,0.2` and stopped about 0.056
units farther back. Projecting the affine scale onto the authored rotation
matches the measured shape. Convex collider construction now applies that rule
before cooking. This is a rule for convex transforms, independent of CCD mode,
body count, child count, or asset name. Ordinary, mirrored and nonuniform frame
controls retain their body/bounds checks. No serialized cache format or vendored
PhysX source changes.

The Q166 baseline retained 25 far-origin raw differences: four body readings,
one bounds reading and 20 contact rows. Its maximum position difference was
about 0.001075 units, velocity difference 0.0390625 units/s and bounds-center
difference 0.0009766 units. Q177 resolves all of them through the cooking frame
correction below. Original gates, references and tolerances are preserved.

Q166 validation covered the release build, 3577 managed tests, seven
compatibility-profile Rust tests, 26 probe-verifier tests and all four CCD
profiles. Twenty-four physics probes retained their baseline results, including
the existing 2D dynamics and query-parity failures.

The deferred-frame controls also check Unity 6000.0
[autoSyncTransforms](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-autoSyncTransforms.html)
and [SyncTransforms](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.SyncTransforms.html).
With automatic synchronization disabled, Transform edits are deferred until the
physics synchronization point or an explicit SyncTransforms call. Six controls
cross dynamic/kinematic bodies with trigger, contact-offset and material edits
after an unsynchronized child rotation. Before synchronization, bounds and an
overlap query retain the previous frame; after synchronization, both use the
rotated frame. Shape reconstruction retains the authored rotation alongside its
cached affine matrix, so property edits cannot combine two different frames.
The reference extension preserves every earlier key/value and has two identical
GPU Editor captures. Two managed regression tests exercise the same contract
using the built-in cube mesh.


## Q177: constrained publication, cooking frames and collider lifetime

The original broad oracle, comparator, tolerances and motion verifier are unchanged.
The strict contact analysis now reports **485 differences, all contact-slot
permutations**. Every complete callback/contact multiset, body reading and callback
count matches the Unity 6000.0.71f1 reference. No contact sort or solver-order rule
was introduced. The seven compound first-step replays now reconstruct both sides
without exchanging inputs, including the asymmetric mixed compound.

| Strict class | Q166 baseline | Constrained publication | Collider-local cooking | Collider lifetime |
| --- | ---: | ---: | ---: | ---: |
| Order only | 812 | 690 | 690 | 485 |
| Pair orientation | 172 | 176 | 176 | 0 |
| Surface witness | 186 | 186 | 186 | 0 |
| Unmatched payload | 1221 | 601 | 596 | 0 |
| Body | 48 | 48 | 48 | 0 |
| Callback count | 2 | 2 | 2 | 0 |
| Total | 2441 | 1703 | 1698 | 485 |

`analyze_contacts.py` also groups every unmatched raw row by authored collider-type
pair and scenario, without changing its category or dropping missing/extra data.
A second annotation distinguishes complete matching geometry with different
impulses from unmatched geometry/metadata: the baseline splits 236/985; after
publication 268/333; after collider-local cooking 268/328; finally 0/0.
Thus an unmatched complete callback was not always evidence of a different point
or normal; its impulse could prevent the original whole-callback match.

| Authored pair (ambiguous slots shown explicitly) | Before | After |
| --- | ---: | ---: |
| box/convex | 581 | 0 |
| box/convex or box/mixed | 8 | 0 |
| box/sphere | 38 | 0 |
| convex/convex | 122 | 0 |
| convex/convex or convex/mixed | 4 | 0 |
| convex/convex or convex/sphere | 8 | 0 |
| convex/sphere | 250 | 0 |
| convex/sphere or sphere/sphere | 8 | 0 |
| convex/triangle_mesh | 127 | 0 |
| mixed/sphere | 6 | 0 |
| mixed/sphere or sphere/sphere | 2 | 0 |
| sphere/sphere | 64 | 0 |
| sphere/triangle_mesh | 3 | 0 |

These are the measured mechanisms and their limits:

| Mechanism | Implementation and confirming control |
| --- | --- |
| Angular constraints and contact-state lifetime | Constrained dynamic bodies project angular velocity in the current mass frame and republish their pose after simulation. Pose publication refreshes their cached contacts. It applies to ordinary overlapping Discrete bodies as well as speculative CCD. Sleeping and kinematic bodies are left asleep/kinematic. Partial constraints use inertia-space moments, rather than additional world-space angular locks. `geometry-constraints.tsv` crosses individual/all angular locks, no angular lock, a position lock, Discrete overlap and speculative approach; it also checks sleeping publication. |
| Assigned velocity and constraints | Frozen position components are removed in world space. Angular assignments project through the assigned inertia rotation, including a rotated body and nonidentity inertia frame. Releasing the constraints retains the projected velocity. Cached principal axes avoid allocating on repeated assignments. The same reference records all position/angular axes, a rotated control and the values immediately after releasing constraints. |
| Convex cooking frame | Cook scaled vertices in collider-local space and keep the rigid pose on the shape. Do not shift the vertices to their centroid during this cooking path. Existing translated, nonuniform, mirrored, sheared and deferred-frame controls retain their checks. All 25 original far-origin differences resolve exactly, including body, bounds and contact data; they were a transform/cooking-frame difference, not accepted float residuals. |
| Collider construction and lifetime | Update same-kind shapes in place and retain them across attachment transfers. Apply authored geometry changes in place and create a Rigidbody when its component joins the world. Body-attached colliders join through the structural flush. A standalone mesh collider replaces its static actor when switching triangle/convex kind. An empty scene without rigid actors defers SDK simulation and pending removal work. These rules correct the first constraint sequence in all seven compound contexts. Existing construction/child-order/actor-order controls and new `ordering/idle/{0,1,3}` traces record native and legacy contacts after zero, one or three empty steps. Their order remains a report, not a public ordering guarantee. |

All three measured mechanisms are selected by Compat for 6000.0.71f1. Other
releases retain the previous cooking, publication and collider-lifetime paths;
those paths remain unmeasured and can be wrong for those releases. There is no
serialized cache output change. Shim ABI 19 covers the changed structures and
functions. No vendored PhysX source/header patch is added.

The following Unity **6000.0** documentation was checked, including limitations:

| Source | Documented guarantee / limit |
| --- | --- |
| [MeshCollider](https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshCollider.html) | Cooking options control simulation preparation, cleaning, welding and the fast midphase. Convex colliders have a 255-triangle limit. Nondefault cooking and some transform cases require a readable mesh. These options do not specify exact contact reduction or slot order. |
| [defaultContactOffset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-defaultContactOffset.html), [contactOffset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-contactOffset.html) | The default initializes new colliders; each offset must be positive. Contacts can be generated inside the sum of the two offsets. The existing 0.001/0.05 offset and live-default controls retain their values; this task changes neither contact/rest offsets nor reporting thresholds. |
| [Collision.GetContacts](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision.GetContacts.html) | The array overload does not allocate; the list can grow if capacity is insufficient. No contact-slot ordering guarantee is documented. |
| [Rigidbody.constraints](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-constraints.html) | Position constraints use world axes; rotation constraints use inertia space relative to `inertiaTensorRotation`. The exact publication and assignment arithmetic above is an observation of this release and the named controls. |
| [PublishTransform](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody.PublishTransform.html) | Publishes the simulated Rigidbody pose to its Transform. Child transforms are not updated by this method; callers must account for hierarchy order. The capture checks that publishing a sleeping constrained body does not wake it. |

`--geometry-constraints` has one canonical 633-row reference from two identical
GPU Editor captures. It gates the measured angular-constraint/assignment/sleep
cases at the original tolerances. At Q177, its no-angular-constraint controls were explicit
negative controls: 58 strict rows remained visible (11 body, 44 payload, 3 slot
order), with counts, identities and finite values still checked. Their face-contact
constraint sequence produces small residual spin: maxima over five steps are
0.000324803 position units, 0.000841626 quaternion components, 0.00700853 units/s
linear velocity and 0.0481988 radians/s angular velocity. This is a separate
free-rotation numeric trace, not full solver parity or an insignificant-residual
acceptance. The original broad capture locks rotation and has no such residual.

The new idle controls are appended only after two identical GPU Editor captures
that preserve every earlier ordering-reference key/value. The supplemental CCD
controls improve from 1143/1167 to 1167/1167. Q06 improves from 6708/6814 to
6714/6814: only its 97 hinge-angle and three resting-speed numeric residuals
remain. The broad geometry trace has 485 slot permutations; the construction
profile retains its full raw sequence/orientation report and native/legacy
consistency checks. Far-origin differences are 25 -> 0.


Q177 validation: release build; 3667 managed tests; seven compatibility-profile
Rust tests; 34 verifier tests; regenerated stubs with no generated diff. All 24
physics probes were run before and after with no regressions: 21 pass, two
existing 2D failures and one contact-event report. Contact-event profiles retain
their baseline results, including static-only scenes. All five CCD profiles
pass their declared gates. Their raw failures and the new free-rotation
negative-control trace remain available in the runner outputs.


The constraint-transition controls append 80 rows while preserving all original
553 rows. They assign velocity before applying full/partial angular or position
locks, with identity and rotated body/inertia frames, then simulate one step.
Changing constraints immediately projects existing linear velocity in world
axes and angular velocity in the current world mass frame. The projection is
shared with constrained pose publication and runs before the next integration;
full rotation freezing therefore preserves the initial rotation on that step.
These controls resolved 26 new strict failures (16 immediate-velocity and ten
body rows), with 575/633 strict matches and the same 58 free-rotation diagnostic
rows afterward. Both extended GPU Editor recordings are byte-identical. Managed
regressions also check full/partial first-step rotation, the rotated mass frame,
position locks and allocation-free repeated constraint assignments.


## Q189 free-rotation numerics

`--geometry-rotation` records `geometry-rotation.tsv`, 718 rows from two
byte-identical GPU Editor captures. It checks ten free-flight shape/scale
combinations, two asymmetric explicit-inertia clamp controls, and sixteen face
contacts. The contacts cross Discrete overlap/speculative approach, no lock/a
position-Y lock, and automatic convex inertia/a primitive box/explicit inertia/
12 position plus 4 velocity iterations. Every earlier reference stays unchanged.

The original strict failures separate as follows. The row counts refer to whole
records, so a row with several numeric fields is counted once.

| Capture / scenario | Before | After | Quantity and maximum remaining error |
| --- | ---: | ---: | --- |
| Q06 `hinge/motor` | 40 | 40 | Angle, 0.005834882 degrees |
| Q06 `hinge/motor-toggle` | 4 | 4 | Angle, 0.00056935 degrees |
| Q06 `hinge/limits` | 33 | 33 | Angle, 0.00054944 degrees |
| Q06 `hinge/wrap` | 8 | 8 | Equivalent wrapped angle, 0.000121 degrees |
| Q06 `hinge/contact` | 15 | 15 | Twelve angles, 0.00027148 degrees; three resting speeds, 0.000341510 degrees/s |
| `constraints/0/True` | 20 | 20 | Four body and sixteen contact/callback payload rows; pose/velocity bounds below |
| `constraints/4/False` | 19 | 0 | Position-constrained Discrete contact |
| `constraints/4/True` | 19 | 0 | Position-constrained speculative contact |

The shared `ConstrainedBodyStateMeasured` path now publishes a dynamic body's
pose for position constraints as well as angular constraints. The existing
world-axis linear projection and mass-frame angular projection are reused.
Publication does not wake sleeping bodies or change kinematic bodies. This is
one constraint-state mechanism, independent of geometry, CCD mode, iteration
counts or probe names. The constraint verifier now requires position-lock
controls to match strictly, removing their former diagnostic exception.

The explicit-inertia contact control isolates that change. Both engines have
identical mass properties and a byte-identical first step. Without position
publication they diverge on the next step. Publishing constrained poses makes
all four explicit-inertia contexts pass. The primitive control also passes in
all four contexts. This establishes the observed correction; refreshing the
mass-frame pose before the next contact computation explains it, but is an
implementation inference rather than a documented Unity algorithm.

| Candidate cause | Control result |
| --- | --- |
| Angular integration / gyroscopic terms | Every freely spinning sphere, convex, compound and scaled body passes the original strict comparison, with constant world angular velocity in the measured five-step flights. The asymmetric assigned tensor/rotation also passes. There is no evidence for adding gyroscopic terms. |
| Maximum angular speed | Both engines expose 50 rad/s by default in this fixture. Explicit 7/50 limits, a 100 rad/s assignment and an acceleration-mode torque step match. The clamp is a pre-integration speed bound; no post-solver clamp is added. |
| Automatic / assigned inertia | Sphere and two-sphere properties agree exactly. Hull and box properties have a few float32 ulps of arithmetic difference; the unscaled hull moments differ by at most 2.8e-9, and the scaled mixed control by 1.5e-8. With the same explicitly assigned moments, unconstrained convex contacts match. This identifies automatic-mass arithmetic as a seed of the remaining contact numerics, without claiming a different physical inertia rule. |
| Solver counts | Both engines start new bodies at 6 position/1 velocity iterations. The explicit 12/4 controls read back identically. Higher counts reduce residual velocity errors, but do not remove every pose/manifold reading difference. Changing defaults would change the problem rather than fix it. |
| Sleeping | All contact and flight controls have `sleepThreshold=0`. Q177's explicit sleeping-publication checks continue to pass. Sleep thresholds cannot explain the active-body residuals. |
| Shim float order / constrained publication | The position-constrained controls pass after general pose publication, including identical explicit inertia. Unconstrained bodies do not take that path. No per-step allocations or new native ABI, cache format, or vendored PhysX patch is introduced. |

The twenty remaining original diagnostic rows belong only to the unconstrained
speculative hull. Across its five steps, maximum absolute differences are
1.88486e-5 position units, 9.17684e-5 quaternion components, 0.000489614 units/s
linear velocity and 0.00484274 rad/s angular velocity. Its first-step angular
velocity differs by only 3.172e-5 rad/s; repeated nearly parallel face contacts
amplify that seed. There is no changed contact count, phase, collider identity,
contact lifetime, collision outcome or visible trajectory in this trace.
These are recorded insignificant residuals in `COMPATIBILITY_GAPS.md`, not full
free-rotation solver parity or a universal error bound.

The new higher-iteration unconstrained controls retain 33 further diagnostic
rows, three body and thirty payload rows. Their maxima are 6.06526e-5 position
units, 0.000253485 quaternion components, 9.61944e-6 units/s linear velocity and
4.51996e-5 rad/s angular velocity. Planar manifold selection is ill-conditioned:
complete point sets can select locations about 0.15 units apart on the same
flat face, despite normals differing by at most 0.000112159 and separations by
0.000211649. Such contact positions are not mislabeled as ulp-sized errors or
slot permutations. The tiny input/pose differences select another clipped face
witness; the measured motion, callbacks and identities retain their outcomes.
The full raw point, normal, separation, impulse and velocity records remain in
the strict comparison. Exact manifold payload parity is not claimed.

The rotation gate requires free flight, configuration, primitive contacts,
explicit-inertia contacts and every position-constrained contact to match at
the unchanged strict tolerances. It reports the automatic-inertia unconstrained
contacts separately, bounds their body differences by quantity, and still
requires finite numeric fields, complete rows, exact counts, callback phases
and collider identities. Diagnostic impulse components are bounded by 5e-4 at
6/1 iterations and 2e-5 at 12/4; relative velocity uses the matching body bound.
Normals and separations are bounded by 2e-4 and 3e-4. Different planar witnesses
must preserve the face extents within 1.5e-4 and mutual plane distance within
1e-4; the gate
checks both point sets without rewriting their slots. Mutation tests reject
large impulses, normals, separations, moved faces and nonnumeric payloads. Original Q06 angle
and resting-speed bounds, all raw comparators and all existing oracle values
remain unchanged.

These Unity **6000.0** contracts were checked for Q189, including limitations:

| Documentation | Guarantee and relevance |
| --- | --- |
| [Rigidbody.maxAngularVelocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-maxAngularVelocity.html) | Limits speed before simulation; the solver may subsequently exceed it. The page's stated default of 7 differs from the measured fixture default of 50, so the capture records the actual setting. |
| [Rigidbody.inertiaTensor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-inertiaTensor.html), [inertiaTensorRotation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-inertiaTensorRotation.html) | Principal moments use the body's rotated mass frame. All attached colliders contribute to automatic inertia. Assigned zero moments prohibit rotation on those axes. These guarantees do not specify float accumulation order. |
| [Physics.defaultSolverIterations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-defaultSolverIterations.html), [defaultSolverVelocityIterations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-defaultSolverVelocityIterations.html) | Positive position iterations trade cost for joint/contact accuracy. Defaults initialize new bodies; changing the global does not update existing bodies. Velocity iterations affect restitution and joint response. |
| [CCD overview](https://docs.unity3d.com/6000.0/Documentation/Manual/ContinuousCollisionDetection.html), [speculative CCD](https://docs.unity3d.com/6000.0/Documentation/Manual/Speculative-CCD.html) | CCD predicts collisions between steps and is not a guarantee of exact physical outcomes. The public support guarantee covers box, sphere and capsule colliders. Speculative contacts can be missed or produce ghost collisions. The additional convex observations remain specific to this reference. |
| [Rigidbody.constraints](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-constraints.html), [PublishTransform](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody.PublishTransform.html) | Position locks use world axes and angular locks use inertia axes. PublishTransform publishes the simulated body pose to its Transform. Neither specifies the contact-cache arithmetic isolated by the controls. |

Q189 validation: release build; 3706 managed tests; eight compatibility-profile
Rust tests; 40 CCD verifier tests. All 24 physics probes run before/after retain
their results, 21 pass, two pre-existing 2D failures and one contact-event report.
All six CCD profiles pass their gates. Raw results are Q06 6714/6814,
supplemental 1167/1167, broad geometry 11915/12400 with 485 slot permutations,
construction 3842/4554 with zero consistency failures, constraints 613/633 and
rotation controls 665/718. No missing/extra constraint or rotation rows, or new lifetime/count failures.
The broad report still includes 65 missing and 65 extra raw contact slots within
its 485 complete-payload permutations.

## macOS arm64

Measured on an arm64 Mac (macOS 26) with the Unity 6000.0.71f1 macOS arm64
Editor in batch mode (`-nographics`) and Unbound built for arm64. These
captures are not committed; the canonical reference stays the Linux Editor's.
Two Editor captures of the default suite were byte-identical. Counts use this
probe's own comparators and verifiers, with the macOS Editor capture in place
of the reference where named.

| Suite | macOS Editor against the reference | Unbound (macOS) against the macOS Editor | Unbound (macOS) against the reference |
| --- | --- | --- | --- |
| default | 6809/6814, 5 unresolved | 6711/6814, 3 unresolved | 6706/6814, 8 unresolved |
| `--geometry-controls` | 1167/1167 | 1167/1167, every value equal | 1167/1167 |
| `--geometry` | 12331/12335, 4 content differences | 11915/12400, no motion or content difference | 11911/12400, 4 content differences |
| `--geometry-ordering` | 4554/4554 | 3842/4554, no consistency failure | 3842/4554, no consistency failure |
| `--geometry-constraints` | 633/633 | 613/633, none unexpected | 613/633, none unexpected |
| `--geometry-rotation` | 714/718, 4 unexpected | 665/718, 1 unexpected | 661/718, 5 unexpected |

The Unity Editor itself computes different physics on arm64: 1955 of the
6814 default values differ from the Linux reference, and five of them,
`ccd/ghost/3/{1..5}/body` (an angular velocity of opposite sign), exceed the
tolerances. Unbound on macOS gives those five values exactly as the macOS
Editor does. In `ccd/rotating`, `ccd/ghost`, `ccd/kinematic-moving`,
`ccd/fixed` and `query/*`, every value Unbound records on macOS equals the
macOS Editor's, just as Unbound on Linux equals the Linux reference there.
The four `--geometry` content differences and four of the five rotation
differences are likewise the macOS Editor's own.

Unbound compiles PhysX's NEON vector math on arm64. A build that ran PhysX's
SSE2 vector math there instead, with every SSE2 operation reproduced
bit for bit, matched the Linux reference (6714/6814, 0 unresolved) but moved
those five families away from the macOS Editor. Exact division and square
root in place of NEON's reciprocal refinement moved them away as well, while
the summation order of dot products, the signed zero rules of min, max and
negation, and flushing denormals made no difference to those families
(floating-point contraction is already off for the whole vendored build). So arm64
keeps NEON, and the Linux reference is not the target there.

Open on macOS (measured, cause not found):

- `ccd/thin/2/{False/1,False/2,True/2}/0/event/0/0`: the capsule's Continuous
  and ContinuousDynamic sweep against the wall. The sweep's first support
  point comes from the other end of the capsule segment, so Unbound reports one
  contact at y = 0 where both Editors report y = −0.0952381. The end is
  decided by the sign of a rotation matrix entry of about 1e-7 for the Y-axis
  capsule frame, which PhysX's NEON and SSE2 quaternion-to-matrix formulas give
  with opposite signs. Using the SSE2 formula on arm64 fixes these rows but
  changes `contactmodify3d` away from the macOS Editor, and a capsule frame
  that fixes them changes capsule query and contact rows of `contactevents3d`
  away from it. Neither is used.
- `rotation/contact/3/False/False/4/bullet/event/0/2`: a contact witness on the
  other side of the face (y = 0.150 against the Editor's −0.150).

So `run.py gate` on macOS reports, against the committed reference, the 8
unresolved default rows (the 5 `ccd/ghost/3` rows the macOS Editor shares and
the 3 capsule rows), 4 `--geometry` content differences and 5 rotation
differences; the land gate runs these probes on Linux.
