# Joint probe

How the 3D joints move bodies: `JointProbe` records each joint type's defaults, then scenarios —
hinges (a pendulum, limits with bounciness, a motor with and without free spin, a spring, a door
between bodies, a chain, mass scales), fixed joints (holding, breaking: OnJointBreak), spring
joints (a range, between bodies), a character joint (twist and swing limits) and configurable
joints (a free axis along a slanted direction, linear limits hard and soft, position, slerp and
angular drives, collision on and off between the bodies) — recording every five steps the bodies'
poses and velocities and the joint's readings (current force and torque; a hinge's angle and
velocity).

Measured on PhysX (Unbound runs the same PhysX 4.1, see `managed/UnityEngine/Physics/Joints.cs`):
hinge, character, configurable and fixed joints are D6 joints, spring joints distance joints; the
joint's own body is actor0, the connected body (or the world) actor1; drive limits are forces; a
hinge's limits are negated, its spring a twist drive to -targetPosition, its motor a velocity drive
(damping 1e10); twist limits of character and configurable joints pass as they are; angular limit
contact distances of 0 are 0.01 rad on hinges and PhysX's default elsewhere, linear ones stay 0;
bounce thresholds are at least 0.2; mass scales scale the inverse masses; a new joint's anchor is
the top of the object's mesh, or its origin without one.

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/joints \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/joints-create.log
    JOINTS_PROBE_OUT=$PWD/tests/projects/joints/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/joints \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/joints.log

Run Unbound and compare:

    JOINTS_PROBE_OUT=/tmp/joints unbound run -p tests/projects/joints --headless 300000
    python3 tests/projects/joints/compare.py tests/projects/joints/Reference /tmp/joints

Every record matches but the force of one settling step.
