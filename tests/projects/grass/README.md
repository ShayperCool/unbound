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
VertexLit and Grass render modes; and the scene `Textured` (an image only): a slab whose material
shows an uncompressed checker, drawn without instancing in both modes, beside grass; and the
scene `Waving` (images only): a terrain sloping up across x whose grass waves (strength, amount
and speed apart, a tint) and whose detail distance is short, with grass, billboards and slabs
(white, painted, textured) in the Grass render mode and one in the VertexLit mode aligned to the
slope, drawn at a fixed frame time (Time.captureDeltaTime 1/30) in 30 frames, `Waving1` after
the first and `Waving30` after the last (the waving's phase grows in each frame drawn). Then it
records the terrain settings the quality level's overrides replace, and Texture2D.PackTextures'
layouts (the detail atlas is packed the same way) for sets of textures made in the script. What `Validate` says of each prototype is
recorded, the density terrains' instances again with QualitySettings.useLegacyDetailDistribution,
and the messages the terrain API logs. `GrassProbe` records Mathf.PerlinNoise samples, each
terrain's detail data (resolution, patches, prototypes, layers read back, coverage), every patch's
and layer's instances from `ComputeDetailInstanceTransforms` at two densities, and an image of each
terrain (waving off in the data but `Waving`'s).

`Assets/Editor/GrassExperiments.cs` holds the edit-mode experiments that measured the placement
(`-executeMethod GrassExperiments.Run` with `GRASS_EXPERIMENT_OUT`, and `GrassExperiments.Perlin`).
The grass meshes were measured with a scratch copy of the project overriding the hidden detail
shaders (`Hidden/TerrainEngine/Details/*`) by shaders that write every vertex they see to a
random-write buffer.

Make the assets once (they are committed; `ProbeRunner.CreateDensity`, then `CreateRefusals`, make
the scene `Density`, and `CreateAligned`, `CreateInstanced`, `CreateColoured`, `CreateTextured` and `CreateWaving`
the scenes `Aligned`, `Instanced`, `Coloured`, `Textured` and `Waving`, without touching the others) and record Unity's reference: `record.py` runs the Editor on the GPU in
copies of the project under the validation cache, twice, and publishes only identical captures whose
committed rows and images are unchanged:

    python3 tests/projects/grass/record.py create --method CreateDensity --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateRefusals --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateAligned --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateInstanced --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateColoured --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateTextured --release 6000.0.71f1
    python3 tests/projects/grass/record.py create --method CreateWaving --release 6000.0.71f1
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

Suite gate (6000.0.71f1, Linux GPU, two identical runs of 2026-11-17): `compare_images.py --gate image-residuals.json` holds each of the eleven images to
its recorded pixel count and largest difference: six texture-grass images differ by one level
over part of their blades and in isolated pixels at the edges of distant blades (382 to 9525
pixels, up to 139/255; 4215 to 10523 before the details' colours were quantized in bytes, the
atlas laid out as the Editor's and the waving tint made linear once, Q553; the quads' and painted
slabs' vertex colours were checked by rendering them in the Editor; `Tilted`'s atlas is
compressed in the Editor, not in Unbound), `Textured` in 97 pixels up to 56/255 (its Grass-mode
slabs drawn with the grass shader since Q553: 570 up to 194 before), `Waving1` and `Waving30` in
4598 and 4611 pixels, one or two levels apart in colour over the waving details and up to 68/255
at the faded far edge (not traced), `Refusals` in one pixel by one level (its slab meshes drawn
in the Grass render mode take the terrain's normal at every vertex and the grass shader, as the
Editor draws them, since Q533 and Q553; they differed in 2153 pixels up to 133/255), `Instanced` within 1/255. Any other difference
fails.

## Metal (macOS)

On macOS arm64 the compare steps read `Reference/metal`, two identical captures of the 6000.0.71f1
macOS Editor (Metal, Apple M2 Pro), recorded with `tests/flat_reference.py record grass --variable
GRASS_PROBE_OUT` and `publish --require 'grass.jsonl:"overrides":"ignored"'` (the probe's
completion row). Unbound on Metal matches all 1224 records. Its images differ from the Metal
Editor's at blade edges, more than on Linux, in 10 of 11 images (pixels differing, worst channel):
Coloured 943/193, Counts 2919/168, Coverage 8870/141, DensityCounts 3461/177, DensityCoverage
3954/159, Instanced 365/75, Textured 1087/188, Tilted 4361/147, Waving1 5093/156, Waving30 5000/165
(on Linux, for example, Coloured 382/48 and Counts 599/47); `Refusals` passes on its Linux
residual. Of the pixels differing by more than 8 (Coloured 402, Coverage 154, Tilted 222,
Waving1 244), 75 to 80% hold, within one pixel, the colour Unbound drew there: a blade edge or two
blades' crossing one pixel apart, sub-pixel geometry and not a colour, order or coverage rule; the
rest are blades' shading one level apart, as on Linux. Consistent with arithmetic differences in the
blades' vertex positions (a hypothesis: Unity's Metal shader compiler against Unbound's shader
translation, arm64 floating point); recorded, not chased. They are not declared: the suite's image gate still applies
`image-residuals.json`, the Linux list, to the Metal comparison. Against the Linux reference on the
Mac about 44000 pixels per image differ.
