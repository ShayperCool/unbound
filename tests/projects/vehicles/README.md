# Vehicle probe

How WheelColliders drive a Rigidbody: `VehicleProbe` records the WheelCollider's defaults and a
wheel the scene serialized, then scenarios with one wheel (on a body whose rotation is frozen) or
a four-wheeled car on a flat or sloped ground: suspension (drop, rest, spring settings, expansion
limit, wheel mass, force point), motor, brake, rolling, sliding, steering, friction curves, the
ground's material, wheels in the air, sprung masses, substeps; then extras (what adding, changing
or disabling wheels does to the others' inputs, rest loads, turned, tilted, offset and scaled
wheels, dynamic, trigger and ignored ground, a kinematic body) and queries. Each step records the
body's pose and velocities and every wheel's state: grounded, rpm, sprung mass, world pose and
ground hit.

Measured: Unity runs PhysX 4.1's PxVehicleNoDrive (see `managed/UnityEngine/Vehicles/Vehicles.cs`)
with its own tire model (a Hermite friction curve per axis times the filtered tire load, the pair
kept in the ellipse of the extremum forces) and set-up (the wheel's center is its position at full
compression, the suspension follows the body's axes, the tire rest load is the sprung mass plus
the wheel's, 5 substeps below 5 m/s and 1 above); adding a wheel clears the other wheels' motor,
brake and steer.

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/vehicles \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/vehicles-create.log
    VEHICLES_PROBE_OUT=$PWD/tests/projects/vehicles/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/vehicles \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/vehicles.log

Run Unbound and compare:

    VEHICLES_PROBE_OUT=/tmp/vehicles unbound run -p tests/projects/vehicles --headless 300000
    python3 tests/projects/vehicles/compare.py tests/projects/vehicles/Reference /tmp/vehicles

24 records match; 9 differ as expected (driven or steered wheels whose slip flips sign every step:
chaotic, though the bodies' final positions agree within centimeters); 20 are known differences
of the rigid bodies underneath (Box3D's sleep timing and contacts), to go with the move to PhysX.
