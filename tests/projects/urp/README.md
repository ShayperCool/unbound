# URP probe

The Universal Render Pipeline 17 (the Editor's built-in package, unmodified) rendering a lit red cube,
an unlit green sphere and a lit grey ground under one directional light, read back from a 320x180
render texture by the same script in the Editor and in Unbound. Gamma color space, Forward renderer,
render graph (the native render pass compiler), no post-processing.

`Editor/ProbeSetup.Create` made the pipeline asset, renderer, materials and scene in the Editor.

Record Unity's reference:

    URP_PROBE_OUT=$PWD/tests/projects/urp/Reference/unity-6000.0.71f1.png DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/urp \
        -executeMethod ProbeRunner.Record -logFile /tmp/urp.log

Run Unbound:

    URP_PROBE_OUT=/tmp/urp_unbound.png unbound run -p tests/projects/urp --no-watch --frames 30 --size 320x180

## Scenarios

With `URP_PROBE_DIR` the probe records every scenario into that directory (an image and the
lighting constants URP set): `base`, `shadows`, `lights` (point and spot), `lights_shadows`,
`spot_shadows`, `point_shadows` (additional light shadows), `skybox`, `postfx` (bloom, ACES,
vignette, color adjustments) and `postfx_bloom`, `postfx_tonemap`, `postfx_vignette`,
`postfx_color` (one effect each), `transparent`, `fog`, `msaa`, `render_scale` (0.5),
`camera_stack` (an overlay camera), `state_block` (a renderer feature drawing with a
RenderStateBlock), `override_shader_0`, `override_shader_1`, `override_shader_default` and
`override_shader_and_material` (the same renderer list with a shader override retaining the
original materials' properties; -1 selects pass 0; a simultaneous material override takes
precedence and logs an error), `forward_plus` and `deferred` (the lights scenario on those rendering paths;
the Editor's OpenGL device falls back from deferred to forward), `structured_buffer` (a shader
reading structured and byte address buffers set on a material and a property block),
`depth_opaque`, plus
`shadow-matrices.jsonl` (what CullingResults computes for directional shadows over lights, splits,
resolutions, distances and camera settings), `conservative-shadow-matrices.jsonl` (its culling
spheres with conservative enclosing spheres, as URP's asset asks for, over cameras, distances,
cascades and iteration counts: within 0.3% of the exact sphere Unbound takes), `local-shadow-matrices.jsonl` (the same for point
light cube faces and spot lights) and `sky-mesh.jsonl` (the
sky mesh's vertices, read through a flat-shading probe shader). The Editor's are in
`Reference/scenarios/`:

    URP_PROBE_DIR=$PWD/tests/projects/urp/Reference/scenarios ... -executeMethod ProbeRunner.Record
    URP_PROBE_DIR=/tmp/urp unbound run -p tests/projects/urp --no-watch --frames 30 --size 320x180
    for f in base shadows lights skybox postfx; do python3 tests/projects/urp/compare.py tests/projects/urp/Reference/scenarios/$f.png /tmp/urp/$f.png; done

## Lighting scenarios

`Editor/LightingSetup.Create` (`-executeMethod LightingSetup.Create`) authored the textures in
`Assets/Lighting` and three scenes the probe loads after the scenarios above, baked by the
Editor's CPU lightmapper (no denoising, filtering or compression): `Reflections` (a room seen by
a baked box-projected reflection probe and a custom-cubemap probe of intensity 2 and higher
importance that overlap; mirror spheres in each, in the overlap, outside both, with
reflectionProbeUsage Off and Simple), `Shadowmask` (directional lightmaps, a mixed sun, a baked
point light, light probes; Lit, Simple Lit and Baked Lit static and dynamic objects, a dynamic
cube casting a realtime shadow, a sphere using a Light Probe Proxy Volume, which URP does not
support) and `Subtractive` (the same content, non-directional). Scenarios:

- `cookie_directional` (a grayscale Cookie texture, alpha from the gray scale, tiled by the URP
  light data's cookie size and offset), `cookie_directional_opaque` (an opaque Cookie texture
  with alpha from the input: a white cookie), `cookie_spot` (a coloured Default texture),
  `cookie_point` (a coloured cubemap, through URP's cookie atlas);
- `reflections_simple` (the URP asset's probe blending and box projection off), `reflections`
  (both on), `reflections_forward_plus` (both on, Forward+: URP's reflection probe atlas);
- `lightmap_shadowmask`, `lightmap_distance_shadowmask` (QualitySettings.shadowmaskMode),
  `lightmap_subtractive`;
- `per-object-constants.jsonl`: what each renderer of those scenes draws with
  (`Shaders/PerObjectDump.shader` on a copy of it, read back from a float target): the reflection
  probe constants (unity_SpecCube0/1 boxes, positions, decode values, a cubemap sample), light data,
  lightmap scale/offset, probe occlusion and SH. `compare_constants.py` compares it (exit 1 on any
  difference; 2e-3 relative, Unbound's float targets are half floats).

Two Editor recordings gave identical images and constants (the jsonl files' _TimeParameters
differ). Unbound renders every new scenario within a mean of 0.65; the remaining differences are
the glossy mips of the reflection cubemaps (sphere rims in `reflections*`) and a few lightmap
texels in the lightmap scenarios (see docs/COMPATIBILITY_GAPS.md).
