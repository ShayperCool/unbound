# Grass (terrain details) probe

Two terrains with the same detail prototypes (a grass texture, the same as billboards, a cube mesh
drawn without and with instancing) and painted layers: `Counts` scatters by instance counts,
`Coverage` by coverage. After their images the probe loads the scene `Density` additively (so that
nothing of it reaches those images): `DensityCounts` and `DensityCoverage` hold prototypes scaled by
detailObjectDensity (also recorded at a third density), a lopsided slab mesh fully and partly
ground-aligned, and two prototypes the Editor refuses; `Refusals` holds one refused prototype per
check; these three terrains have one terrain layer. Then the probe loads the scene `Aligned`: `Tilted`
is steep, with grass textures fully and half aligned to the ground and one of zero width. Then it
makes a terrain itself (no image): two grass textures of noiseSeed 0 written in different orders
per patch, a count over 16, and a scatter mode switch after coverage values. Then it loads the scene
`Instanced`: two instanced slab layers share their patches, and the scene `Coloured`
(an image only): grass textures and billboards whose healthy and dry colours lie outside [0, 1]
with alpha below 1, and a slab with vertex colours of its own drawn without instancing in the
VertexLit and Grass render modes. Then it records the terrain settings the
quality level's overrides replace. What `Validate` says of each prototype is
recorded, the density terrains' instances again with QualitySettings.useLegacyDetailDistribution,
and the messages the terrain API logs. `GrassProbe` records Mathf.PerlinNoise samples, each
terrain's detail data (resolution, patches, prototypes, layers read back, coverage), every patch's
and layer's instances from `ComputeDetailInstanceTransforms` at two densities, and an image of each
terrain (waving off in the data).

`Assets/Editor/GrassExperiments.cs` holds the edit-mode experiments that measured the placement
(`-executeMethod GrassExperiments.Run` with `GRASS_EXPERIMENT_OUT`, and `GrassExperiments.Perlin`).
The grass meshes were measured with a scratch copy of the project overriding the hidden detail
shaders (`Hidden/TerrainEngine/Details/*`) by shaders that write every vertex they see to a
random-write buffer.

Make the assets once (they are committed; `ProbeRunner.CreateDensity`, then `CreateRefusals`, make
the scene `Density`, and `CreateAligned`, `CreateInstanced` and `CreateColoured` the scenes `Aligned`,
`Instanced` and `Coloured`, without touching the others) and record Unity's reference: `record.py` runs the Editor on the GPU in
copies of the project under the validation cache, twice, and publishes only identical captures whose
committed rows and images are unchanged:

    python3 tests/projects/grass/record.py create --method CreateDensity --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateRefusals --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateAligned --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateInstanced --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateColoured --release 6000.0.71f1
    python3 tests/projects/grass/record.py record --release 6000.0.71f1
    python3 tests/projects/grass/record.py publish --release 6000.0.71f1

Run Unbound and compare:

    GRASS_PROBE_OUT=/tmp/grass unbound run -p tests/projects/grass --frames 600 --size 320x180 --no-watch
    python3 tests/projects/grass/compare.py tests/projects/grass/Reference /tmp/grass
    python3 tests/projects/particles/compare_images.py tests/projects/grass/Reference /tmp/grass

Instance counts and coverage match exactly (positions, sizes, turns, bounds). The Editor draws grass, billboards
and meshes drawn without instancing at other places than ComputeDetailInstanceTransforms reports
(its patch meshes have their own placement), and Unbound draws them the same way. The detail
textures' atlas samples as the Editor's (measured by rendering the Editor's atlas into a grass
quad): clamped at its edges, without blending mip levels, nothing but transparent texels beside
the textures. Before Q533 Unbound's atlas repeated at its edges and held a white block beside the
first texture, so distant blades lost their edges (the differences once taken for the layerless
terrains' checker, which matches).

Suite gate (6000.0.71f1, Linux GPU, two identical runs of 2026-11-17): `compare_images.py --gate image-residuals.json` holds each of the eight images to
its recorded pixel count and largest difference: six texture-grass images differ by one level
over part of their blades and in isolated pixels at the edges of distant blades (573 to 9549
pixels, up to 139/255; 4215 to 10523 before the details' colours were quantized in bytes, Q553;
the quads' and painted slabs' vertex colours were checked by rendering them in the Editor), `Refusals` in one pixel by one level (its slab meshes drawn
in the Grass render mode take the terrain's normal at every vertex, as the Editor draws them, since
Q533; they differed in 2153 pixels up to 133/255), `Instanced` within 1/255. Any other difference
fails.
