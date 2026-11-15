# URP terrain

A terrain drawn by the Universal Render Pipeline 17 (the Editor's built-in package; a linear-space
project, the pipeline asset and renderer of `tests/projects/urp`) with the pipeline's default
terrain material: five layers in stripes that blend neighbouring layers (plain colors in two
shades; metallic 0, 0.5 and 1; smoothness 0 to 0.8; the second layer's diffuse map without alpha,
the fourth with a constant smoothness), lit by a directional light, beside a smooth metallic
sphere drawn with the pipeline's Lit shader. `UrpTerrainProbe` records, from above, the terrain on
its splat passes (`splat`), on its base map (`base0`, base map distance 0) and half on each
(`half`, 120, the terrain split into its four patches: the far two); from low over the ground on splats (`grazing`) and on the base map
(`grazingbase`); then the base map render textures the terrain made (name, size, format, mips,
sampler); then the same scene without lighting data, low over the ground (`nodata`); last the
`Masks` scene (the probe scene's light and lighting data): a five-layer terrain whose layers have
mask maps with remapped ranges (one layer without a mask, its smoothness from its diffuse alpha;
one with a normal map and metallic squares in its mask; one with opacity as density) and a
four-layer terrain of the first four, from above on the splat passes (`masks`) and on the base maps
(`masksbase`), and low over the ground facing the sun (`masksgrazing`).

What it shows (Unity 6000.0.71f1, NVIDIA): the base maps follow the same rule as the built-in
pipeline's (the Basemap Gen shader's passes and tags); a layer's metallic value is made linear for
the splat material and, as URP's terrain shaders flag `_Metallic0-3` `[Gamma]`, linear once more;
the sphere reflects the default sky of the scene's built-in lighting data, and without lighting
data the black cube (the sphere and the metallic layers black). Each splat pass reads its layers'
mask maps (`_LayerHasMask0-3`, `_MASKMAP` when any layer of the pass has one), and every pass knows
the terrain's layer count (`_NumLayersCount`: opacity as density applies up to four layers); a
layer without a normal map gets the flat normal texture, linear data, so it stays flat in a pass
whose other layers have normal maps.

Make the assets once and record Unity's reference (two GPU captures, published when they agree):

    python3 tests/projects/urpterrain/record.py create --release 6000.0.71f1
    python3 tests/projects/urpterrain/record.py create --release 6000.0.71f1 --method ProbeRunner.CreateMasks
    python3 tests/projects/urpterrain/record.py record --release 6000.0.71f1
    python3 tests/projects/urpterrain/record.py publish --release 6000.0.71f1

The same in a gamma-space copy of the project (`--color-space gamma` for `record`, `publish` and
`check`; its reference in `Reference/unity-<release>/gamma/`): the same records, and images that
Unbound matches as closely as the linear project's.

Run Unbound and compare (records exact; image means printed for the suite to track):

    UNBOUND=/path/to/unbound python3 tests/projects/urpterrain/run.py check
