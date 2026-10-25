# LOD probe

Which level of a LODGroup a camera draws: three cubes (red, green, blue: LOD 0, 1, 2 at 0.6, 0.3,
0.1) seen from distances 0.5 to 24.5 (steps of 0.02, a frame each), with fields of view 30, 60
and 90, LOD biases 0.5, 1 and 2, the group scaled by 2 or sized 3, QualitySettings.maximumLODLevel
1, ForceLOD(2) and ForceLOD(0), from aside, and orthographic sizes 0.2 to 6.2 (steps of 0.01);
the drawn level is the color of a 9x9 render's center pixel. Also the group's API values.

Record Unity's reference (the Editor needs a display; `ProbeRunner.Create` made the scene):

    LOD_PROBE_OUT=$PWD/tests/projects/lod/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/lod \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/lod.log

Run Unbound and compare (sweeps up to a distance of 15: farther the cube covers less than a pixel):

    LOD_PROBE_OUT=/tmp/lod unbound run -p tests/projects/lod --frames 20000 --size 320x180 --no-watch
    python3 tests/projects/lod/compare.py tests/projects/lod/Reference /tmp/lod

What still differs: in the Editor some transitions come a sample or two later than the height
rule gives (one transition of a sweep, not the others: 1.48 for 1.4434), and at 0.8 the cube's
front face lies on the near plane, which the Editor draws and Unbound clips.
