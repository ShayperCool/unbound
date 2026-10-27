# CharacterController probe

How a CharacterController moves: `ControllerProbe` records its defaults, then scenarios of one Move
(or SimpleMove) per fixed step — walking on flat ground with and without a downward motion, a
fall, a straight and a slanted wall, ramps of 30° and 60° (walkable and not), a low and a high step,
a ceiling, SimpleMove from rest, off a ledge and mixed with a Move, a scaled controller, a trigger,
a dynamic box, the minimum move distance, a custom shape, a transform moved with and without a
sync, two controllers and an ignored wall — recording after each call the position, isGrounded,
the returned and the stored collision flags and the velocity, the OnControllerColliderHit
messages of the call and the trigger messages of the step before; then a raycast and an overlap
that find the controller.

Measured on PhysX (Unbound runs the same PhysX 4.1, see
`managed/UnityEngine/Physics/CharacterController.cs`): a PxCapsuleController with the skin width
as its contact offset, the cosine of the slope limit, constrained climbing, non-walkable slopes
preventing the climb, PhysX's kinematic actor (0.8 of the capsule) for queries, triggers and
bodies; the radius scales with the larger horizontal scale, the height with the vertical one;
SimpleMove keeps its own velocity (the speed taken on the ground, gravity added each call); the
controller only follows its transform at a sync. One patch to PhysX matches Unity: the walk
experiment (the second try after a non-walkable hit, which skips the up pass) does not undo a step
offset in its down pass.

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/controller \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/controller-create.log
    CONTROLLER_PROBE_OUT=$PWD/tests/projects/controller/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/controller \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/controller.log

Run Unbound and compare:

    CONTROLLER_PROBE_OUT=/tmp/controller unbound run -p tests/projects/controller --headless 300000
    python3 tests/projects/controller/compare.py tests/projects/controller/Reference /tmp/controller

Every record matches. Hit points on faces are compared on the face's plane only: where a capsule
lies along a face, PhysX takes the point from whichever of the face's triangles wins a tie, and
that differs with the last bits of the arithmetic. Such ties (a step's edge exactly a radius away)
also decide whether a step is touched at all, which is why the probe's steps sit off round
distances.
