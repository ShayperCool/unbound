# Terrain probe

A terrain made by the Editor (`ProbeRunner.Create`: three generated textures, three terrain
layers, `Assets/Terrain/ProbeTerrain.asset` with a 65² heightmap over 40×8×40 units, 64² splat
maps and a square hole, and the scene holding its Terrain and TerrainCollider) and what
`TerrainProbe` records of it: the TerrainData read from the (binary) asset, its height, steepness,
normal and alphamap queries, Terrain.SampleHeight, raycasts against the TerrainCollider (through
the hole too), three spheres dropped on it, and five images with the default terrain material
(built-in renderer: default and full-detail pixel error, a close-up, the basemap near the camera,
from the top).

Make the assets (once; they are committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/terrain \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/terrain-create.log

Record Unity's reference (the Editor needs a display):

    TERRAIN_PROBE_OUT=$PWD/tests/projects/terrain/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/terrain \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/terrain.log

Run Unbound (in a window: the images need a GPU):

    TERRAIN_PROBE_OUT=/tmp/terrain unbound run -p tests/projects/terrain --frames 300 --size 320x180 --no-watch

Compare:

    python3 tests/projects/terrain/compare.py tests/projects/terrain/Reference /tmp/terrain
    python3 tests/projects/particles/compare_images.py tests/projects/terrain/Reference /tmp/terrain

What still differs: two steepness values on nearly flat ground (the arc cosine of a normal whose
last digit differs: 0.8673 vs 0.8675), the dropped spheres (Box3D integrates in four sub-steps,
PhysX in one: a free fall lags by 3/8 g dt² a step), and the images by about 2/255 on average
(the Editor compresses the imported layer textures to BC1; Unbound keeps their pixels).
