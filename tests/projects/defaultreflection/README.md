# Default reflection

What smooth surfaces reflect without a reflection probe in the built-in render pipeline, in two
scenes with the same settings (no light, the default procedural sky): `Data` points at the
built-in lighting data a new scene of this release gets (`unity_builtin_extra`, 20201), `NoData`
has none (`m_LightingDataAsset` empty, written by hand). Each scene gets a row of spheres (the
Standard shader, metallic at smoothness 0.95 and 0.5; `Probe/SurfaceMetallic`, a surface shader
lit by the Standard model, metallic at 0.95 and 0.5 and dielectric at 0.95; `Probe/SurfaceSpecular`,
the StandardSpecular model), a nearer row of rough ones (the Standard shader, metallic at
smoothness 0 and 0.25 and dielectric at 0; the surface shader, metallic at 0 and dielectric at
0.25: the reflection's last mips), and behind them two flat terrains whose one layer is metallic and smooth,
the left drawn with its splat material (`Nature/Terrain/Standard`, a surface shader), the right
with its base map (base map distance 0), and behind the spheres two small terrains: a layer half
metallic on a diffuse texture without alpha, and one with a constant smoothness on a texture with
alpha. `defaultreflection.jsonl` records per scene
`ReflectionProbe.defaultTexture` (name, size), its decode values and the ambient probe's first
coefficients; `<scene>.png` is the view, rendered with `Camera.Render`.

What it shows (Unity 6000.0.71f1, NVIDIA): with the built-in lighting data every smooth surface,
surface shaders and terrains included, reflects the default sky's cubemap
(`Default-Skybox-Cubemap`, 128); without lighting data the default reflection is the black cube
and the ambient probe is zero, so the metallic surfaces are black. A terrain layer's splat
material gets its metallic value made linear (the slider is read as a gamma value) and, with the
smoothness taken from the diffuse alpha (a new layer's source) and a diffuse format with alpha,
a smoothness of at least 1, so the alpha alone sets it.

Record the reference (two GPU captures, published when they agree):

    python3 tests/projects/defaultreflection/record.py record --release 6000.0.71f1
    python3 tests/projects/defaultreflection/record.py publish --release 6000.0.71f1

Run Unbound and compare (records exact; each image's mean difference at most 0.25 levels and
at most 1% of its pixels over 8):

    UNBOUND=/path/to/unbound python3 tests/projects/defaultreflection/run.py check
