# 2D joints probe

Unity 6000.0.71f1, Linux. All nine components are measured: Distance, Spring, Hinge, Fixed,
Friction, Wheel, Slider, Relative and Target. The 1401 records cover defaults, setter limits,
serialized prefabs/cloning, static/kinematic/dynamic connections, reflected nonuniform parents,
automatic anchors/angles/offsets/targets, enabling, suspended simulation, reconnecting, changing
destinations, reaction forces, motors, equal/reversed limits and every break action.

1382 records match at the original 2e-4 tolerance. Nineteen records have separately reported
iterative-solver rounding residuals. These allow angular velocity/force/torque differences up
to 0.001 and limitState changes within 0.001 degrees of the same limit. Wheel angular comparisons
also propagate the relative tolerance of the two body velocities being subtracted. Positions,
settings, callbacks, nonfinite values and larger errors still fail. Eleven comparator tests
exercise these boundaries. The API check reports zero errors in 2251 hand-written types.

```bash
JOINTS2D_PROBE_OUT=/tmp/joints2d-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/joints2d" -executeMethod ProbeRunner.Record \
  -logFile /tmp/joints2d-editor.log
JOINTS2D_PROBE_OUT=/tmp/joints2d-unbound unbound run \
  -p tests/projects/joints2d --headless 30000 --no-watch
python3 tests/projects/joints2d/compare.py tests/projects/joints2d/Reference /tmp/joints2d-unbound
python3 -m unittest discover -s tests/projects/joints2d
unbound api-check -p tests/projects/joints2d
```

ProbeRunner.Create recreates the empty scene and nine prefabs through the Editor. Initial
Distance/Spring snapshots record body/settings only: before the first step Unity returned zero
versus NaN in two identical runs. Forces are checked after simulation steps.

Measured details:

- Anchors use rigidbody physics poses and signed lossy scale, excluding shear. Physics rotations
  preserve authored collider shapes/inertia. Polygon inertia retains a centered reference point.
- Distance/Spring use bounded hard-distance corrections and effective constraint mass for spring
  coefficients. A zero-frequency Spring behaves as a hard distance joint.
- Setting Hinge motor/limits enables that setting. Reversed Hinge limits disable the constraint;
  Slider clamps max to min. Slider jointSpeed converts linear speed to degrees. Its coupled limit
  solver retains positional slop and disables the motor for equal limits.
- Wheel needs connectedBody and stores autoConfigureConnectedAnchor without configuring it.
  Its suspension/motor structs retain raw values. Suspended joint queries still reflect motion
  of connected bodies. Queries update only the requested joint.
- Relative auto-offset and Target auto-target recapture on enable. Explicit offset/target setters
  retain their values while the automatic flag is set. Relative.target uses the connected body's
  desired owner rotation. An inactive Relative returns zero for target.
- Target rejects connectedBody and enableCollision setters with warnings. All 38 diagnostic
  severities and their order match. It reports force on the owner; its zero-frequency solver
  uses 0.0001 internally while the property reports zero. It accepts an explicit local anchor.
- CallbackOnly sends OnJointBreak2D every exceeding step. Disable/Destroy send it before their
  action. Suspended bodies retain inertia and joint impulses.

The altered Box2D 2.3.1 coupled slider solver retains its original license and attribution in
`managed/UnityEngine/Physics2D/Box2D/Dynamics/Joints/PrismaticJoint.Unity.cs`.

The broader physics probe retains 8 existing scenario differences; its Hinge, Spring and Distance
scenarios match. ConstantForce remains 608/608, CharacterController 23/23, and 3D joints retain
one known settling-force difference. These probes do not establish complete physics parity.
