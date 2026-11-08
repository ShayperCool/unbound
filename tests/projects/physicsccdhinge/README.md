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
The complete contact trace is a strict **report**, not a contact-parity gate.
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

For the extended capture, the raw comparison retains 2441 differences across
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
Sequential constraint processing explains the sensitivity to pair order, but
callback order alone does not prove the solver's input order. Exact matching of
the actor/shape lifetime and constraint construction remains open. No global
sort, child reversal or fixture-dependent solver rule is added.

Contact-content differences remain visible independently of motion. For example,
`geometry/static/1/0/3/1/bullet/event/0` has four contacts on both sides. Unity
reports the four face corners; Unbound retains an interior point and three
corners. The shared normal, separation and body motion match. This is a reduced
manifold difference, not a slot permutation or an insignificant-contact claim.

The supplemental controls retain 24 impulse-vector differences. Each has equal
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

The far-origin control retains its complete strict contact/motion report as a
separate numeric compatibility limit. Its body readings must remain finite,
integrate their reported velocity, and supply the actual incoming velocity in
callbacks. The contact and motion quantization rule at large local coordinates
remains open. It retains 25 raw differences: four body readings, one bounds
reading and 20 contact-trace rows. The maximum position difference is about
0.001075 units, the maximum velocity difference is 0.0390625 units/s, and the
bounds center differs by 0.0009766 units. Its failures are not relabeled as
ordering or removed from the raw report. All original Q06, geometry and supplemental gates keep their
assertions and tolerances.

Validation: release build; 3575 managed tests; seven compatibility-profile Rust
tests; 23 probe-verifier tests; all four CCD profiles. Twenty-four physics probes
were run before and after. Their results are retained, including the existing
2D dynamics and query-parity failures. The new ordering profile reports 1872 raw
differences and zero capture/native consistency failures; its 25 far-origin rows
remain explicitly open.
