# Wind probe

How wind zones move particles through the External Forces module: `WindProbe` records the
WindZone's defaults, then for each scenario a particle system (no gravity, no emission) with five
particles at rest and one wind zone, simulated in five 0.1 s steps: velocities and positions after
each step. Scenarios cover directional zones (strength, rotation, distance, the module's
multiplier, disabled module, the List influence filter, a local-space system), spherical zones,
turbulence and pulsing. Finally the pulse over ten seconds for three magnitude/frequency pairs.

What it measured: a directional zone accelerates along its forward axis by windMain × pulse at any
distance; a spherical one along the direction away from its center by
windMain × pulse × (1 − d²/r²), nothing past its radius; turbulence moves no particles; wind zones
pass the influence filter by layer only (a List filter gives no wind); the pulse is
1 + magnitude × 0.333 × (cos(πx) + cos(3π/8 x) + cos(π/20 x)) with x = frequency × Time.time.

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/wind \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/wind-create.log
    WIND_PROBE_OUT=$PWD/tests/projects/wind/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/wind \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/wind.log

Run Unbound and compare:

    WIND_PROBE_OUT=/tmp/wind unbound run -p tests/projects/wind --frames 100000 --size 320x180 --no-watch
    python3 tests/projects/wind/compare.py tests/projects/wind/Reference /tmp/wind

Every record matches except the two pulsing scenarios, which run at whatever Time.time the frames
reached and so differ between runs (the pulse records check the pulse itself).
