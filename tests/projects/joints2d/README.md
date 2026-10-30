# 2D joints probe

Unity 6000.0.71f1, Linux. DistanceJoint2D and SpringJoint2D are measured with fixed,
kinematic and dynamic connected bodies, anchors under reflected nonuniform parents,
automatic anchor/distance configuration, spring frequency zero, disabling/re-enabling,
changing the connected body, and all four break actions. The 214 records include
serialized prefabs and clones, API limits, reaction forces for several time steps,
and OnJointBreak2D callbacks.

```bash
JOINTS2D_PROBE_OUT=/tmp/joints2d-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/joints2d" -executeMethod ProbeRunner.Record \
  -logFile /tmp/joints2d-editor.log
JOINTS2D_PROBE_OUT=/tmp/joints2d-unbound unbound run \
  -p tests/projects/joints2d --headless 30000 --no-watch
python3 tests/projects/joints2d/compare.py tests/projects/joints2d/Reference /tmp/joints2d-unbound
```

ProbeRunner.Create recreates the scene and both serialized prefabs in the Editor.

Distance and Spring match all 214 records, including body inertia and reflected hierarchy
scale after simulation. A physics rotation preserves the collider's original shape and inertia.
Spring frequency uses the constraint's effective mass, including anchor offsets. Unity bounds
hard distance position corrections. The connectedAnchor setter retains the automatic anchor
when autoConfigureConnectedAnchor is enabled. CallbackOnly reports every exceeding step;
Disable and Destroy send the callback before applying their action.

The expanded fixture covers HingeJoint2D, FixedJoint2D and FrictionJoint2D too, for 691 records.
It measures motors, equal/reversed limits, high initial angular velocity, suspended simulation,
useConnectedAnchor and hard/soft fixed joints. Before the first Spring step, Unity can return
uninitialized reaction data: two identical Editor runs returned zero versus NaN. The initial
Distance/Spring snapshot therefore records body/settings only; forces are checked after steps.

The comparator retains the 2e-4 tolerance. Known iterative-solver angular-velocity, force and
torque residuals below 0.001, and limitState changes within 0.001 degrees of the same limit,
are reported separately. Positions, settings, callbacks and larger differences still fail.
Run its nine validation cases with `python3 -m unittest discover -s tests/projects/joints2d`.
