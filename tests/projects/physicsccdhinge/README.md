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
