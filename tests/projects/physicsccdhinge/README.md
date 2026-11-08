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
