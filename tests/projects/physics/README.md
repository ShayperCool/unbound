# Physics probe

Scenarios over every 2D and 3D physics component (bodies, forces, materials, contacts, triggers,
joints, queries), run identically in the Unity Editor and in Unbound; each writes its values to a JSON
file and `compare.py` lists the differences.

Record Unity's reference (the Editor runs in batch mode, enters Play mode, writes and exits):

    PHYSICS_PROBE_OUT=/tmp/unity.json ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -nographics \
        -projectPath tests/projects/physics -executeMethod ProbeRunner.Record -logFile /tmp/unity.log

Run Unbound and compare:

    PHYSICS_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/physics --headless 600
    python3 tests/projects/physics/compare.py Reference/unity-6000.0.71f1.json /tmp/unbound.json

`Reference/` keeps the recording of the release named in its file name.
