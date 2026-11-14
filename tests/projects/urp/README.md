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
precedence and logs an error), `subpass_mrt` (`MrtProbeFeature`: a render graph raster pass after
the opaques writes two color attachments, the camera color and a target the graph clears; the
Editor's OpenGL Core device then clears the camera color too, to that target's color) and
`subpass_mrt_cleared` (both attachments new targets that clear: each to its own color) and
`subpass_mrt_mixed` (three attachments: the camera color, kept, and two new targets that clear),
`forward_plus` and `deferred` (the lights scenario on those rendering paths;
the Editor's OpenGL device falls back from deferred to forward), `structured_buffer` (a shader
reading structured and byte address buffers set on a material and a property block),
`depth_opaque`, plus
`shadow-matrices.jsonl` (what CullingResults computes for directional shadows over lights, splits,
resolutions, distances and camera settings), `conservative-shadow-matrices.jsonl` (its culling
spheres with conservative enclosing spheres, as URP's asset asks for, over cameras, distances,
cascades and iteration counts: Unbound computes the same iterative sphere, within 2e-4 of every
record, managed/UnityEngine.Tests/CascadeCullingTests), `slice-shares.jsonl` (the culling spheres and near planes of one and four cascades over orthographic cameras, fields of view 20, 60 and 170, far planes 50, 200 and 1000 and culling parameters' shadowNearPlaneOffset of 5 and -2, with and without conservative spheres: Unbound's slices match every record, the same tests), `local-shadow-matrices.jsonl` (the same for point
light cube faces and spot lights) and `sky-mesh.jsonl` (the
sky mesh's vertices, read through a flat-shading probe shader). The Editor's are in
`Reference/scenarios/`:

    URP_PROBE_DIR=$PWD/tests/projects/urp/Reference/scenarios ... -executeMethod ProbeRunner.Record
    URP_PROBE_DIR=/tmp/urp unbound run -p tests/projects/urp --no-watch --frames 60 --size 320x180
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
the glossy mips of the reflection cubemaps (sphere rims in `reflections*`) and the edge of a
sphere's realtime shadow in the lightmap scenarios (see docs/COMPATIBILITY_GAPS.md).

`texture-formats.jsonl` (compared by `compare_constants.py`) records the `format` of the cookie
textures, of two Single Channel textures (Alpha and Red) and of the baked lightmaps, with each
lightmap channel's largest stored value (read back through a float target).

## Renderer feature scenarios

`Editor/FeatureSetup.Create` (`-executeMethod FeatureSetup.Create`) authored `Assets/Features`: a
decal base map and normal map, a material of URP's own Decal Shader Graph and one of a copy of it
with angle fade enabled, and the Single Channel textures. After the lighting scenes the probe
loads the Probe scene again, moves the camera closer and records, each scenario with a new
renderer feature (one setting changed) that is removed after it:

- Screen Space Ambient Occlusion, with a lit wall behind the objects and a bright ambient probe:
  `ssao_off` (the control), `ssao` (the defaults: depth-normals source, blue noise, high blur),
  `ssao_depth`, `ssao_depth_normals_high` (depth source, high normal samples), `ssao_interleaved`,
  `ssao_samples_high`, `ssao_blur_medium`, `ssao_blur_low`, `ssao_after_opaque`,
  `ssao_downsample`, `ssao_params` (intensity, radius, falloff, direct lighting strength),
  `ssao_forward_plus`, and `ssao_lightmap` (the Subtractive scene: lightmapped and probe-lit
  objects);
- Decals, a projector above the ground in front of the cube (configured before it is enabled, as
  a serialized one: `renderingLayerMask`'s setter does not update an enabled projector):
  `decal_screen_space`, `decal_normal_blend_medium`, `decal_normal_blend_high`,
  `decal_automatic`, `decal_dbuffer`, `decal_size_pivot`, `decal_uv`, `decal_fade_factor`,
  `decal_draw_distance`, `decal_angle_fade`, `decal_layers`, `decal_forward_plus`.

On the Editor's OpenGL Core device URP's Decal feature turns the DBuffer technique away (it
draws no decals; Automatic picks Screen Space) and does not enable decal layers (`decal_layers`
is `decal_screen_space`), so the DBuffer surface data options are not measured here.

## Post-processing scenarios

`Editor/PostFxSetup.Create` (`-executeMethod PostFxSetup.Create`) authored `Assets/PostFx/Lut.png`,
a 32-size Color Lookup strip imported as a LUT is (linear, no mipmaps, uncompressed). After the
feature scenarios the probe records, with the main camera disabled (it renders only when the
probe renders it), post-processing on and a global Volume, one setting each:

- color grading, the URP asset's Low Dynamic Range mode: `postfx_lut` (Color Lookup, contribution
  0.8), `postfx_tonemap_neutral`, `postfx_white_balance`, `postfx_channel_mixer`,
  `postfx_lift_gamma_gain`, `postfx_shadows_midtones_highlights`, `postfx_split_toning`,
  `postfx_curves` (master S-curve, hue versus saturation); High Dynamic Range grading:
  `postfx_grading_hdr` (post exposure, saturation, contrast, ACES), `postfx_lut_hdr`;
- lens: `postfx_dof_gaussian`, `postfx_dof_gaussian_hq`, `postfx_dof_bokeh`,
  `postfx_lens_distortion`, `postfx_chromatic_aberration`, `postfx_panini`, `postfx_film_grain`;
- motion blur over two frames (a render, then the move and the recorded render):
  `motion_blur_camera` (camera only, low quality), `motion_blur_camera_high`, `motion_blur_objects`
  (camera and objects, the cube moved);
- the camera's anti-aliasing: `aa_fxaa`, `aa_smaa_low`, `aa_smaa_medium`, `aa_smaa_high`, `aa_taa`
  (eight frames, one render each, before the recorded one);
- render scale upscaling: `upscale_point`, `upscale_linear` (0.5), `upscale_auto_fractional`
  (0.7), `upscale_fsr`, `upscale_fsr_sharpness` (0.75), `upscale_stp` (0.5, eight frames);
- `inline_sampler`: a screen-filling quad (`Shaders/InlineSampler.shader`) sampling a 4x4
  Point/Repeat texture through an inline linear clamp sampler and a Bilinear/Clamp one through an
  inline point mirror sampler, outside their 0..1 range;
- skinned motion (Camera and Objects motion blur, a two-bone ribbon in front of the camera, one
  render, the change, a frame, the recorded render: Unity skins after the scripts' updates):
  `motion_blur_skinned` (the upper bone bends, the renderer stays), `motion_blur_skinned_off`
  (the same with skinnedMotionVectors off), `motion_blur_skinned_moved` (the ribbon moves whole),
  `motion_blur_skinned_root` (only the root bone moves and turns), `motion_blur_skinned_renderer`
  (the renderer moves, its bones in another hierarchy stay) and `motion_blur_skinned_root_scale`
  (only the root bone's scale changes).

Last, `light_data_block` (the Probe scene, a point and a spot light): a MaterialPropertyBlock that sets
unity_LightData to zero on the cube (a renderer), on a Graphics.RenderMesh cube above it, and no block
on a RenderMesh cube above the sphere (the control). The block wins over the per-object light data
in both draws (no main or additional light). It runs in its own last frame: queued draws stay for
the rest of a frame, and an earlier extra frame would shift the post-processing scenarios' frame counts.

Their constants record `frameCount` (film grain, dithering and TAA jitter follow it).

## Terrain light layers

`terrain_light_layers` (after the light order): with the asset's Use Rendering Layers, two terrains
(URP's Terrain Lit, one white layer) side by side under the scene's sun (Default) and a point light
on Layer 1 only: the left terrain's `renderingLayerMask` is Default (lit by the sun only), the right
one's Layer 1 (lit by the point light only).

## Light order

After every scenario (or alone with the other late scenarios, `URP_PROBE_LATE=1`) `LightOrderProbe` writes
`light-order.jsonl` (compared by `compare_constants.py` with `Reference/scenarios/light-order.jsonl`):
with the scene's lights off, a camera of its own renders one small quad (`Shaders/LightOrderDump.shader`)
under a set of named lights per case and records the culling's visible lights in order, URP's main
light, the quad's `unity_LightData` and its per-object lights in order. The cases: `luminance`
(equal lights of ten colours), `attenuation` (distance against range; one light reaches the quad's
bounds but not its centre, one does not reach it), `render_mode` (Important, Auto, Not Important),
`directional` (a sun and other directional lights with and without shadows against point lights),
`limit_main` (ten point lights and a main light), `main_mask` (a main light whose culling mask leaves
out the quad's layer), `culling_mask` (a light's culling mask against the quad's and the camera's
layers; lights of intensity 0, 0.0005 and 0.002), `spot_reach` (spot lights pointing at the quad,
away from it and beside it), `visible_order` (spot and point lights at different distances from the
camera) and `visible_limit` (36 point lights). Two Editor recordings were identical.

What they show (Unity 6000.0.71f1, OpenGL Core): an object's lights are ranked by the luminance of the
colour as set (0.3 r + 0.59 g + 0.11 b) times the intensity, a local light's divided by
1 + 25 d² / range² (d from the bounds' centre), a directional light's times 16 when it casts shadows,
plus 1000 for Important and minus 1000 for Not Important; `unity_LightData.y` counts every light that
reaches the object (10 for ten lights; `unity_LightIndices` holds the first eight) and `.z` is 0 without
a main light or when its culling mask leaves out the object's layer. A spot light reaches only what its
spot touches (pointing away or beside the quad, it does not). Visible lights need an intensity of at least 0.001 and a culling mask that shares a layer
with the camera's; the local ones come spot lights first, then point lights, each the nearest to the
camera first, and with URP's limit (33 here) the nearest stay. The directional lights' order among
themselves is not recorded (it is not sorted).
`texture-samples.jsonl` (compared by `compare_constants.py`) records what the Single Channel
textures sample as (blitted into a float target): uncompressed and compressed (Alpha stays
Alpha8, Red becomes BC4; the compressed controls hold only values every block format keeps),
and a Red one marked sRGB (still R8_UNorm). Authored by `PostFxSetup.CreateSingleChannelControls`. Two Editor recordings gave identical images and
constants for these scenarios. On the Editor's OpenGL Core device inline sampler states do not
apply (each texture samples through its own state: Point upscaling equals Linear,
`inline_sampler`), and a skinned mesh moves as its renderer does: bending adds no motion, with
skinnedMotionVectors on or off (the project deforms meshes on the CPU; set to GPU (Batched) the
Editor recorded the same bend, not committed); its object motion follows its root bone's position
and rotation, neither the renderer's transform nor the root bone's scale. URP's C# runs in Mono's double precision (0.7
scales the target to 223x125), which Unbound does not (see docs/COMPATIBILITY_GAPS.md).

## Rendering path scenarios

With `URP_PROBE_DEFERRED_DIR` (a run of its own; `UrpProbe.Deferred.cs`) the probe records each
scene with the Universal Renderer's Forward and Deferred modes (`<name>_forward`,
`<name>_deferred`) and `modes.jsonl`: per recording the device, `renderingModeActual` and the
state of the path's global keywords after the frame (URP clears some of them, such as
`_GBUFFER_NORMALS_OCT` and `_SCREEN_SPACE_OCCLUSION`, at the end of the camera's render, and leaves
others, such as `_RENDER_PASS_ENABLED`, to the next one). Scenes: `opaque` (Lit metal, Lit dielectric and Simple Lit spheres beside
the Lit cube and the Unlit sphere), `lights` (a point and a spot light), `shadows` (main, spot and
point light shadows), `many_lights` (twelve point lights, more than Forward's per-object limit of
4, around a Lit transparent sphere), `light_layers` (the asset's Use Rendering Layers: the point light on Layer 1 lights the
spheres, not the cube or the ground), `accurate_normals` (the renderer's Accurate G-buffer
normals), `transparent` (a Lit transparent sphere), `msaa` (4 samples), from the feature
scenarios' closer camera `ssao`, `decal_screen_space`, `decal_dbuffer`, `decal_layers` and
`decal_dbuffer_layers` (Decal Layers: only the cube shares the projector's Layer 2), and the
baked scenes' `lightmap_shadowmask`, `lightmap_distance_shadowmask`, `lightmap_subtractive`.
`ProjectSettings/TagManager.asset` defines rendering layers Layer 1 and Layer 2: URP masks light
and decal layers with the project's defined rendering layers, so with Default alone a light or
projector on another layer lights or decals nothing.

The 6000.0 Manual (Choose a rendering path in URP) documents that Unity falls back to a
different rendering path on a device that does not support the selected one (it does not say
which), that Deferred has no MSAA and no limit on the realtime lights per opaque object. URP's C#
does not render Deferred on OpenGL devices; measured here, the fallback is Forward. So the
references are recorded on two devices, two identical recordings each (the `_TimeParameters`
constants differ):

- `Reference/unity-6000.0.71f1/paths-opengl`: the Editor's OpenGL Core device on the GPU, as
  every other reference here (the `ProbeRunner.Record` recipe above). Each Deferred request
  renders Forward, pixel for pixel the Forward recording. Unbound's device is this one: the suite
  compares Unbound's run with this set.
- `Reference/unity-6000.0.71f1/paths-vulkan`: the same Editor with `-force-vulkan` on the GPU (a
  Vulkan 1.1 device on the RTX 4060; batch mode needs no presentable display), where
  `renderingModeActual` is Deferred.

Without `-force-vulkan` the Editor on the same GPU-backed display picks OpenGL Core (not
committed). Device against path: the Forward recordings of both devices are identical except the
decal scenarios that need DBuffer or Decal Layers, which the OpenGL device refuses (DBuffer draws
nothing; Decal Layers are not enabled, so the decal lands on everything), and
`decal_screen_space`, 217 pixels on the decal's edges, 79 of them over 8. On Vulkan, Deferred
against Forward differs in `many_lights` (Deferred lights each pixel with all twelve lights,
Forward with each object's 4; the transparent sphere, drawn forward in both, gets the same
lights), `msaa` (Deferred renders without MSAA), the screen space decals
(`decal_screen_space`, `decal_layers`: Deferred draws them into the G-buffer, before lighting) and
elsewhere by at most a mean of 0.2 (not investigated; the G-buffer's 8-bit channels are a
hypothesis). Unbound has no Vulkan device, so the
Deferred recordings have no Unbound counterpart (docs/COMPATIBILITY_GAPS.md).

    URP_PROBE_DEFERRED_DIR=/tmp/paths unbound run -p tests/projects/urp --no-watch --frames 200 --size 320x180
