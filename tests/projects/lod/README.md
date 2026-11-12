# LOD probe

Which level of a LODGroup a camera draws: three cubes (red, green, blue: LOD 0, 1, 2 at 0.6, 0.3,
0.1) seen from distances 0.5 to 24.5 (steps of 0.02, a frame each), with fields of view 30, 60
and 90, LOD biases 0.5, 1 and 2, the group scaled by 2 or sized 3, QualitySettings.maximumLODLevel
1, ForceLOD(2) and ForceLOD(0), from aside, and orthographic sizes 0.2 to 6.2 (steps of 0.01);
the drawn level is the color of a 9x9 render's center pixel. Also the group's API values.

Record Unity's reference with the driver (two independent windowed Editor captures on the GPU,
published only when both are identical; `ProbeRunner.Create` made the scene):

    python3 tests/projects/lod/run.py editor  --release 6000.0.71f1 --label <label>
    python3 tests/projects/lod/run.py publish --release 6000.0.71f1 --label <label>

Sample distances and orthographic sizes are one division of whole numbers, so that every scripting
runtime gives the same float. Each render waits until 2 ms of real time have passed since the
camera's previous render: in the Editor a render that comes sooner can draw the levels chosen for
the camera's previous place (measured with `run.py editor --hold`, which adds `hold` rows and is
not published: on that machine a wait of 0.5 ms still drew the old level, 0.75 ms and more the new
one). Unbound chooses the level at every render and does not model that hold. The previous
reference was a batch-mode capture, whose renders came sooner than that; windowed captures give
the same records with or without the wait, and with the former sample sums.

Run Unbound and compare (sweeps up to a distance of 15: farther the cube covers less than a pixel):

    LOD_PROBE_OUT=/tmp/lod unbound run -p tests/projects/lod --frames 20000 --size 320x180 --no-watch
    python3 tests/projects/lod/compare.py tests/projects/lod/Reference /tmp/lod

Unbound matches every record of the reference. The level rule: a level is drawn while the
camera's distance times 2 tan(fov / 2) / lodBias (for an orthographic camera twice its size over
the bias) is less than the group's world size over the level's transition height; at equality
the next level is drawn (the fov 90 sweep's last level ends at 5.0, the orthographic one at
size 5.0).
