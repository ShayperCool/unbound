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
sampler).

What it shows (Unity 6000.0.71f1, NVIDIA): the base maps follow the same rule as the built-in
pipeline's (the Basemap Gen shader's passes and tags); a layer's metallic value is made linear for
the splat material and, as URP's terrain shaders flag `_Metallic0-3` `[Gamma]`, linear once more;
the sphere reflects the default sky of the scene's built-in lighting data.

Make the assets once and record Unity's reference (two GPU captures, published when they agree):

    python3 tests/projects/urpterrain/record.py create --release 6000.0.71f1
    python3 tests/projects/urpterrain/record.py record --release 6000.0.71f1
    python3 tests/projects/urpterrain/record.py publish --release 6000.0.71f1

Run Unbound and compare (records exact; image means printed for the suite to track):

    UNBOUND=/path/to/unbound python3 tests/projects/urpterrain/run.py check
