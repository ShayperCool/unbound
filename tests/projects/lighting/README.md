# Lighting probe

Small scenes lit by the built-in pipeline's forward lighting, rendered into PNGs: a ground plane,
two spheres and a cube with the Standard, Legacy Diffuse and VertexLit shaders under point, spot
and directional lights as pixel lights (ForwardAdd), vertex lights and SH lights, light render
modes and the quality level's pixel light count, cookies; and the light textures themselves
(`textures_*`: `Probe/AttenuationView` shows the attenuation texture and the spot cookie a
ForwardAdd pass samples, seen from above). `ambient.txt` notes the ambient probe before and after
the probe sets a flat ambient.

Record Unity's reference (the Editor needs a display):

    LIGHTING_PROBE_OUT=$PWD/tests/projects/lighting/Reference/images \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/lighting \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/lighting.log

The Vulkan Editor's capture (`Reference/unity-6000.0.71f1/vulkan`, two identical captures) is
recorded the same way with `-force-vulkan` on the GPU-backed display :0. Its images match the
OpenGL Core ones (mean difference at most 0.01); its shadow globals differ: the directional
`unity_LightShadowBias.x` is half the OpenGL Core one, negated (clip depth from 0 to 1, reversed),
as is a spot light's; a point light's is half of it; and the shadow matrices map depth reversed.

Run Unbound (in a window: the images need a GPU) and compare:

    LIGHTING_PROBE_OUT=/tmp/lighting unbound run -p tests/projects/lighting --frames 10 --size 320x180 --no-watch
    python3 tests/projects/particles/compare_images.py tests/projects/lighting/Reference/images /tmp/lighting

Shadows: `shadow_*` scenes (directional hard, soft, cascades, two shadowed suns, spot, point)
and the shadow variables their passes see: `shadow_globals*.txt` (Probe/GlobalsDump, the forward
base pass with a shadowed sun, at the project's cascades and with one, two or four, hard or soft,
other biases) and `shadow_globals_spot_*` / `shadow_globals_point_*` (Probe/GlobalsDumpAdd, the
ForwardAdd pass of a shadowed spot or point light). One vector per pixel; Unbound's float targets
are half floats, so its dumps agree to about three digits.
The directional scenes also write their screen-space shadow masks (`*_mask.png`), taken by a
light command buffer at `AfterScreenspaceMask`. `shadow_shadows_only`: a `ShadowsOnly` cube casts
its shadow and is not drawn; `shadow_two_sided`: of two quads turned away from the sun, only the
`TwoSided` one shades the ground.

`five_points` follows the forward lights' ranking and fades (tests/projects/builtinrp
`light_rank_*`). `textures_spot` and the spot cone's edge in `spot`/`shadow_spot` differ (the default spot
cookie is a fitted curve, within 2/255 on average, at worst 57/255 on `spot`, 48 on `shadow_spot` and
37 on `textures_spot`); the gate below lists every other difference.

Suite gate (6000.0.71f1, Linux GPU, two identical runs of 2026-11-16): both devices' images are gated by `compare_images.py --gate`, OpenGL Core with
`image-residuals.json` (28 of 31 images listed) and Vulkan with `image-residuals-vulkan.json` (24 of
27), each entry with its pixel count, largest difference and reason: the spot cone's edge above;
shadow edges (pixels along shadow boundaries); a point light's range fade (`point_range`, up to
12/255); small differences of light attenuation elsewhere (up to 6/255, mostly 1 to 3; not traced);
and VertexLit's silhouette pixels (13 to 19, up to 44/255; not traced). The shadow edges and the
silhouettes are open in docs/COMPATIBILITY_GAPS.md (built-in lighting images). Any other difference
fails.

VertexLit controls (`vertexlit_ambient`, `vertexlit_point`, `vertexlit_directional`,
`vertexlit_no_pixel_lights`: the ambient alone, one point light, one directional light, the two
point lights with no pixel lights allowed; OpenGL Core references only, two identical captures of
2026-11-16): the ambient alone is the flat ambient itself, so the fixed-function lighting takes the
ambient at half (the ambient color halved, times the material's) like each light, and
`Combine texture * primary DOUBLE` brings both back; `vertexlit_ambient` matches exactly.
