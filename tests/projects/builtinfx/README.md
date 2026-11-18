# Built-in render pipeline features probe

What the built-in pipeline does beyond forward lighting (`Assets/Scripts/FxProbe*.cs`, linear color
space, the Ultra quality level: MSAA 2, soft particles and realtime reflection probes on; cameras
render 200x150 textures with `Camera.Render`, a few cases wait for the player loop):

* **Projectors** (`projector_*`, `projector.*`): Standard Assets' Projector/Multiply and
  Projector/Light (in `Assets/Shaders`), orthographic and perspective, `ignoreLayers`, disabled,
  without a material; the `unity_Projector` and `unity_ProjectorClip` matrices dumped by a shader
  for a perspective, an orthographic and a scaled projector; a new projector's defaults and one
  loaded from the scene.
* **Replacement shaders** (`replace_*`): `SetReplacementShader` by RenderType and with no tag, a tag
  no shader has, `ResetReplacementShader`, `RenderWithShader` once (and with a null tag).
* **Planar reflections** (`mirror*`, `oblique.*`): `CalculateObliqueMatrix` values, a mirror script's
  reflection camera with and without `GL.invertCulling`, `CommandBuffer.SetInvertCulling` (which
  stays set after the camera), `SetTargetBuffers` with one and two color buffers,
  `RenderDontRestore`.
* **Cubemaps** (`cubemap_*`): `RenderToCubemap` into a `Cubemap` and a cube render texture, with
  face masks, from a rotated camera.
* **Skyboxes** (`sky_*`): a camera's `Skybox` component against `RenderSettings.skybox`.
* **Reflection probes** (`probe_*`, `probe.*`): realtime probes by script with each time slicing
  mode (when `IsFinishedRendering` turns true), into a texture of one's own, OnAwake, EveryFrame,
  realtime probes off in the quality settings, `BlendCubemap`, two custom probes with blend
  distances.
* **Flares and halos** (`flare_*`): a light's halo, a Halo component, a LensFlare, a light's flare,
  with and without a FlareLayer, `RenderSettings` strengths.
* **Rendering paths** (`path_*`, `paths.*`): `actualRenderingPath` for every path (perspective and
  orthographic), Deferred Shading (HDR and LDR), Forward and Vertex Lit images, the camera events of
  a deferred and a forward render.
* **Light probe proxy volumes** (`lppv*`), **material variants** and `Lerp`,
  `CopyMatchingPropertiesFromMaterial`, `ComputeCRC` (`material*`), **quality settings**
  (`unity_ShadowColor`, the billboard keyword, soft particles, skin weights, the texture mipmap
  limit), **raster states** (AlphaToMask under MSAA and without, ZClip False, `GL.wireframe` and
  `CommandBuffer.SetWireframe`, `GL.sRGBWrite`), **additional vertex streams**, **billboards**
  (`BillboardAsset`/`BillboardRenderer`, both windings), **occlusion components**, **static
  batching** (which renderers a combine takes, moving the root and a child), **cameras** (HDR without
  image effects, a culling matrix set from script, `CopyFrom`, the scene camera's settings).
* **Built-in shader settings** (`settings_*`, `settings.*`, recorded last): for deferred shading
  (HDR and LDR), deferred reflections, screen-space shadows, depth-normals, motion vectors, the
  light halo and the lens flare, an image in each mode set by `GraphicsSettings.SetShaderMode` and
  `SetCustomShader` (built-in, custom with a shader of `Assets/Shaders/Settings*.shader` that draws
  a constant color, custom without a shader, No Support), the mode and shader read back, the
  rendering path and what the render logs; the depth-normals and motion vector textures are drawn
  over the image by `Probe/Settings/ShowGlobal`. Depth-normals and motion vectors, which no earlier
  part renders, take the modes without a shader first. `settings_halo_unrendered`: a custom shader
  set and unset again before any render.

`ProbeRunner.Create` made the scene (a camera with a Skybox and a FlareLayer, a projector, lights
with a halo and a flare, a lens flare, a halo, occlusion components, a proxy volume) and the assets
in `Assets/Resources` (the Editor wrote them).

Record Unity's reference (the Editor needs a display; it renders on the GPU):

    BUILTINFX_PROBE_OUT=$PWD/tests/projects/builtinfx/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/builtinfx \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/builtinfx.log

The built-in shader settings part changes the graphics settings while it runs: record it from two
copies of the project (`Assets`, `Packages`, `ProjectSettings`) outside the repository, through
`tests/editor_runner.py` on the GPU, and publish what is new only where both captures agree byte for
byte (the settings images and rows were recorded so; every earlier file stayed as it was).

Run Unbound and compare:

    BUILTINFX_PROBE_OUT=/tmp/builtinfx unbound run -p tests/projects/builtinfx --frames 2000 --size 640x480 --no-watch
    python3 tests/projects/builtinfx/compare.py tests/projects/builtinfx/Reference /tmp/builtinfx

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* A projector draws each renderer the camera sees whose bounds touch its frustum (its ignored layers
  left out) again with its material. `unity_Projector` is the frustum's clip space biased into 0..1
  (`(0.5, 0.5)` scale and offset on x and y) times the projector's world-to-local matrix without
  scale (z negated) times the object's matrix; `unity_ProjectorClip` maps the depth past the near
  plane over the depth range into x and z (the height along the projector's up, minus the near
  plane, into y).
* Replacement: the replacement's SubShader whose tag has the object's shader's value draws the
  object, with the object's material properties, sorted at the replacement's queue (a transparent
  object replaced by a queue-less SubShader draws with the opaque ones); objects without the tag or
  a matching SubShader are not drawn; no tag draws everything with the first SubShader;
  `RenderWithShader` lasts one render.
* `CalculateObliqueMatrix` is Lengyel's oblique near plane and leaves the camera's projection;
  `GL.invertCulling` and a command buffer's `SetInvertCulling` stay set across renders.
  `SetTargetBuffers` empties `targetTexture`, the pixel rect follows the buffers; with two color
  buffers, shaders that write one output also fill the second (the GL driver's broadcast).
  `RenderDontRestore` leaves the camera's target active.
* `RenderToCubemap` renders from the camera's position along the world's axes (its rotation does not
  matter), faces outside the mask untouched (a new `Cubemap` is 205 gray), stored sRGB-encoded;
  false for a 2D render texture. `new Cubemap(size, TextureFormat, ...)` is linear.
* A camera's Skybox with a material replaces `RenderSettings.skybox`; without one (or disabled) the
  settings' sky draws.
* Realtime probes render before the cameras (none with realtime probes off: the probe reflects
  black); `IsFinishedRendering` turns true 1, 3 and 8 frames after the render for NoTimeSlicing,
  AllFacesAtOnce and IndividualFaces; `realtimeTexture` is an ARGBHalf cube of the resolution; a
  render into one's own texture makes it the probe's `texture`. `BlendCubemap` lerps the faces
  (alpha 1). A custom probe's texture shows from the next frame (black in the frame it is set).
* Halos draw with or without a FlareLayer; lens flares need one.
* `actualRenderingPath`: legacy deferred lighting and orthographic deferred shading render forward;
  UsePlayerSettings follows GraphicsSettings (Forward here). Deferred events: Before/After GBuffer,
  Reflections, Lighting, FinalPass, then the opaque image effects; no depth texture events.
* Material variants keep only their overrides; the rest, queue, keywords, instancing and GI flags
  come from the parent, also when it changes at run time. `parent` set from script keeps the
  material's own values. `Lerp` interpolates colors and `[Gamma]` floats in linear space and texture
  scales and offsets, leaving textures; `CopyMatchingPropertiesFromMaterial` copies what the
  destination's shader declares.
* `unity_ShadowColor` and the `BILLBOARD_FACE_CAMERA_POS` keyword change at the next frame, not when
  set; skinning follows `QualitySettings.skinWeights` (Auto quality) once the player loop skins.
* AlphaToMask dithers coverage under MSAA and is ignored without; ZClip False clamps depth;
  wireframe draws edges; `GL.sRGBWrite` off stores linear values into sRGB targets.
* A billboard wound away from the camera is culled. Static batching combines renderers of the same
  vertex channels in groups of two or more (a plane next to spheres stays alone); combined
  renderers move with the root but not with their own transforms. A culling matrix set from script
  does not cull `Camera.Render`'s renderers. `allowDynamicResolution` reads false.

## What differs

The camera follow-up now makes `hdr_half_target` pixel-identical to Native. It is
checked normally, bringing the verified image count to 92 with 11 inherited
expected images. The comparator exits nonzero for unexpected values, missing or
wrong-size images, pixel errors and an empty reference. Seven failure-case tests
in `test_compare.py` check those outcomes and preserve existing expected values.

* Halos and lens flares draw as tests/projects/flares measured them (`flare_*` within a mean of
  0.05) except `flare_HaloLight_strength1`, where the light is enabled and haloStrength raised
  right before `Camera.Render` in the same frame (the Editor's halo is larger but about half as
  bright as Unbound's; not measured in isolation), and `flare_LensFlareObject`, a flare's first
  render after it is enabled, which fades in by the frame's unfixed delta time.
* `mip_limit_1`: textures are sampled from their first mip whatever the limit (Unbound uploads one
  level of imported textures; it warns).
* `target_buffers_mrt1`: Unbound leaves a second color buffer alone where a shader writes one output;
  the Editor's GL driver broadcasts it.
* `path_deferred*`: within a mean of 3.6 (shadow edges and the point and spot lights' highlights).
* `probe_blend_distance_now`: probes created the same frame (mean 4.3).
* Built-in shader settings, as the Editor draws them: each effect with the setting's shader when
  it has one, whatever the mode, else with the shader it last drew with (SetCustomShader(null) and a
  shader unset before any render keep it); depth-normals and motion vector textures whose setting
  never had a shader are not made. No Support switches off screen-space shadows (with the warning
  "Screenspace cascaded shadow map support is disabled in graphics settings") and deferred
  reflections; deferred shading, halos and flares still draw. Unbound draws them alike
  (`GraphicsSettings.RendererShader`), with these differences: `settings_deferred_builtin` and
  `settings_deferredReflections_*` are within the `path_deferred*` means (3.6);
  `settings_deferred_{custom,customNull,disabled}`: the Editor draws a custom shader's light pass
  over the scene's pixels only, Unbound over the whole view (it warns);
  `settings_deferredLdr_*`: the Editor decodes an LDR camera's light buffer with the shader's second
  pass, here still the custom one; Unbound's light buffer is linear and has no such pass (it warns);
  `settings_motionVectors_custom`: the Editor draws the moved sphere's object pass in a second
  `Camera.Render`, Unbound takes it to be still (Camera.Render keeps no previous matrices, so the
  built-in pass would write no motion there); `settings_lensFlare_builtin`: as `flare_LensFlareObject`,
  a flare's first render fades in by the frame's delta time.
* `static_batching.mesh`: Unbound keeps each renderer's own mesh; `material.crc.values`: checksums
  are Unbound's own.

## Metal (macOS)

On macOS arm64 the compare steps read `Reference/metal`, two identical captures of the 6000.0.71f1
macOS Editor (Metal, Apple M2 Pro), and two more identical to them, recorded with
`tests/flat_reference.py record builtinfx --variable BUILTINFX_PROBE_OUT` and `publish`. A lens
flare fades in over real time, and three of four Metal captures drew `flare_LensFlareObject`
differently, so on Metal the probe records `flare_LensFlareObject.skipped` instead (the Linux path
is unchanged). Unbound on Metal matches 302 of 305 values and 112 of 115 images within a mean of 2
(299 values and all 115 images of the Linux reference). Open: `raster.sRGBWrite_False` (Unbound
reads the linear colour where the Metal Editor reads the sRGB-encoded one) and
`probe_own_target_0/4/5`.
