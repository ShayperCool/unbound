# Grass (terrain details) probe

Two terrains with the same detail prototypes (a grass texture, the same as billboards, a cube
mesh drawn without and with instancing) and painted layers: `Counts` scatters by instance counts,
`Coverage` by coverage. `GrassProbe` records Mathf.PerlinNoise samples, each terrain's detail data
(resolution, patches, prototypes, layers read back, coverage), every patch's and layer's
instances from `ComputeDetailInstanceTransforms` at two densities, and an image of each terrain
(waving off in the data).

`Assets/Editor/GrassExperiments.cs` holds the edit-mode experiments that measured the placement
(`-executeMethod GrassExperiments.Run` with `GRASS_EXPERIMENT_OUT`, and `GrassExperiments.Perlin`).
The grass meshes were measured with a scratch copy of the project overriding the hidden detail
shaders (`Hidden/TerrainEngine/Details/*`) by shaders that write every vertex they see to a
random-write buffer.

Make the assets and record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/grass \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/grass-create.log
    GRASS_PROBE_OUT=$PWD/tests/projects/grass/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/grass \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/grass.log

Run Unbound and compare:

    GRASS_PROBE_OUT=/tmp/grass unbound run -p tests/projects/grass --frames 600 --size 320x180 --no-watch
    python3 tests/projects/grass/compare.py tests/projects/grass/Reference /tmp/grass
    python3 tests/projects/particles/compare_images.py tests/projects/grass/Reference /tmp/grass

Instance counts match exactly (positions, sizes, turns, bounds) but for ground-aligned meshes'
turns. Coverage mode matches where samples hold at least three quarters of a footprint across;
narrower samples, and low coverage values, are approximated (the listed expected differences).
The images differ mostly in the layerless terrains' shading: the Editor's checker comes out
bluer and a little darker than Unbound's (the ambient probe is the same; how the Editor lights a
terrain without layers is not known yet).
