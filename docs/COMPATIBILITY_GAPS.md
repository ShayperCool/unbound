# Compatibility gap map (Unity 6000.0.71f1)

What Unbound does not yet do as the Editor does, found by auditing the engine itself rather than
waiting for a game to hit it. `ROADMAP.md` says what is done and probed; this file lists what is
left, ranked, so work can go down it.

Audit of 2026-10-29: read-only, every subsystem; nothing was measured against the Editor, so each
row still needs a probe before and after it is fixed. Paths are relative to `managed/UnityEngine/`
unless they start with `crates/` or `native/`.

## How gaps hide

Game runs keep hitting features that are missing *without a warning* (physical cameras,
`Camera.SetReplacementShader`). The generated stubs warn on first use; these do not:

| Pattern | Example | Why it is silent |
|---|---|---|
| Hand-written empty body | `Camera.SetReplacementShader { }`, `Animator.ApplyBuiltinRootMotion` | A hand-written member suppresses the generated stub, and with it the warning (516 empty public bodies in the hand-written sources, many legitimate) |
| Stored, never consumed | `Collider.hasModifiableContacts`, `Physics2D.simulationLayers` | Auto-properties and fields the frame/simulation never reads |
| Serialized field or import setting not read | `m_DisabledShaderPasses`, `m_RenderingPath`, terrain lightmap, ModelImporter clip masks/root motion, `m_ContactPairsMode` | `ReadSerialized` / the importers read a subset of keys and drop the rest |
| Message looked up, never sent | `OnAnimatorIK`, `OnParticleUpdateJobScheduled` (`Messages.cs` `s_NotSent` lists them) | Messages are not API calls: `api-gaps` cannot see them |
| Overload drops a parameter | `SetPropertyBlock(block, materialIndex)`, `SetPixels(..., mip)`, Blit depth slice, `RenderTextureSubElement` | Forwarded to the simpler overload |
| Asset class with a ClassId but no reader | `.renderTexture` (always 256×256 RGBA8, no depth) | `Loading.ReadAsset` default-constructs it |
| Stub default not neutral | `ScalableBufferManager.widthScaleFactor` = 0 (Unity 1), `CalculateObliqueMatrix` = zero matrix, `Microphone.devices` = null | Warns once, then the wrong value keeps flowing |
| Generated stub events | `TerrainCallbacks.*` | Generated as `add { } remove { }` without `NotImplemented` |
| Claimed capability | `SystemInfo.supportsMotionVectors`, `supports3DRenderTextures` = true | Packages enable a path that then draws nothing |
| Warn once, then silent | SpriteMask, reflection probes | A second scene shows nothing in the log |

## Tooling first: make every gap loud

Each of these turns a class of silent gaps into a list that keeps itself up to date:

- [x] `Stubs.Ignored(member)` (warn once, like `NotImplemented`) for knowingly ignored members,
      and an `[EditorFaithful("why")]` marker for real no-ops; every hand-written empty body,
      constant answer, dropped setter and never-raised event carries one of the two (or is
      implemented). `unbound api-check` fails (category `silent`) on any that carries neither.
- [ ] Stub generator: events warn on `add`; value-returning stubs take the Editor's answer from a
      small table where 0/null is wrong (`ScalableBufferManager`, `Microphone.devices`, ...).
- [ ] Message coverage: Unity's full MonoBehaviour/UIBehaviour message list against the messages
      the engine sends; `unbound doctor` lists the project's scripts that define one never sent.
- [ ] Unread serialized data: diff each class's native type tree (already dumped for settings
      assets) against the keys its reader consumes; `unbound doctor` lists non-default values of
      unread keys in the project's scenes, prefabs, assets and `.meta` import settings.
- [ ] ClassId coverage: Unity's class-ID table against registered readers; a ClassId without a
      reader fails loudly instead of default-constructing.
- [ ] `SystemInfo` answers follow what the renderer does.
- [ ] `api-gaps` noise: trivial attributes (`JetBrains.Annotations.*`, `ResourcePathAttribute`,
      `CategoryInfoAttribute`, `ShaderKeywordFilter.*`) implemented, so the coverage line counts
      real gaps; `InitializeOnLoad*` (honoured by name) stops showing as missing.
- [ ] Probes where there are none: `animator` (blend trees, layers, root motion), `audio` (mixer,
      filters, through `GetOutputData`), lifecycle messages (mouse, focus/pause, visibility,
      reparenting).

## The map

**S** = silent (no warning), **W** = warns (stub or "skipped"), impact **H**/M/L on real games.

### Core runtime and messages

Done: lifecycle and execution order, all `RuntimeInitializeLoadType`s, `[InitializeOnLoad*]`,
coroutines (measured timing), SendMessage family, parent/children/canvas messages, Transform API,
Instantiate, scenes (single/additive/async, events, merge/move), serialization (SerializeReference,
callbacks, FormerlySerializedAs), prefab variants/nesting/overrides, Time, PlayerPrefs.

| Gap | Where | | Impact |
|---|---|---|---|
| `OnMouseDown/Up/UpAsButton/Enter/Over/Exit/Drag` never sent | `Core/PlayerLoop.cs:110` (`SendMouseEvents` is an empty marker) | S | done (Unity's SendMouseEvents algorithm: tests/projects/messages) |
| `OnApplicationFocus` / `OnApplicationPause` never sent (Editor sends focus(true) at Play start) | `Runtime/Messages.cs:79,90`, `Core/Application.cs:65` | S | done (tests/projects/messages) |
| `OnBeforeTransformParentChanged` never sent | `Runtime/Components.cs:203-240` | S | done (tests/projects/messages) |
| FixedUpdate capped at 8 steps, leftover time dropped | `Runtime/PlayerLoopDriver.cs:316-330` | S | done (and the first step at fixed time 0: tests/projects/messages) |
| `InstantiateAsync`, `InstantiateParameters` | Generated stubs | W | M — Unity 6 code gets null |
| `Instantiate` of Texture2D/RenderTexture/AudioClip/TextAsset/Font/... throws | `Runtime/Instantiation.cs:42` | W | M |
| GameObject clone placement and initial Rigidbody pose | `Runtime/Instantiation.cs`, `Physics/Rigidbody.cs` | S | done for measured Unity 6000.0.71f1 cases: 160 body/placement records and 12 independent Transform-only records match two agreeing Native runs, including active/inactive clones, explicit poses, null-parent flags and both placement modes. Canvas retains 8671/8671. Further hierarchy/scale and component-clone cases remain unmeasured. |
| `HideFlags.DontSave` roots destroyed on a Single load | `Core/SceneManagement.cs:486-490` | S | done (`DontSaveInEditor` roots stay, in no scene: tests/projects/messages) |
| `StartCoroutine` on a disabled behaviour of an active object refused (verify) | `Core/MonoBehaviour.cs:50,63` | S | done (Unity starts it: tests/projects/messages) |
| No main-thread checks on engine API | only `Audio/AudioSystem.cs:117` | S | M — races instead of Unity's exception |
| `CullingGroup` | stub | W | L-M |
| `Resources.UnloadAsset` empty (warns); `UnloadUnusedAssets` only compacts | `Core/Resources.cs:82,91` | W/S | L-M |
| `Pose`, `LazyLoadReference<T>`, `GameObject.SetGameObjectsActive/InstantiateGameObjects` | stubs | W | L-M |
| `Application.OpenURL` logs only (`Application.Quit` done: ignored in Play mode, no wantsToQuit/quitting, as measured in tests/projects/messages) | `Core/Application.cs:170,174` | S | L-M |
| `[PostProcessScene]`, `IProcessSceneWithReport`, `[DidReloadScripts]` not run | `Editor/EditorInitialization.cs:20-22` | S | L-M |
| `PrefabUtility.IsPartOf*` / instance roots / `GetCorrespondingObjectFromSource`, `SerializedProperty` prefab-override info constant | `Editor/EditorApplication.cs`, `Editor/SerializedObject.cs` | W | L-M |
| `PlayerSettings.SetScriptingDefineSymbols*` (kept for the session), `CompilationPipeline.RequestScriptCompilation`, `EditorUtility.RequestScriptReload`: scripts not recompiled/reloaded | `Editor/EditorApplication.cs`, `Editor/CompilationPipeline.cs`, `Editor/EditorState.cs` | W | L-M — editor tools that toggle defines |
| `EditorApplication.ExecuteMenuItem` runs only the project's parameterless `[MenuItem]` methods (no built-in menus) | `Editor/EditorApplication.cs` | W | L |
| `Application.isLoadingLevel/buildGUID/consoleLogPath/HasProLicense` constants; `Debug.ExtractStackTraceNoAlloc/RetrieveStartupLogs` empty | `Core/Application.cs`, `Core/Debug.cs` | W | L |
| Profiler memory figures (`GetRuntimeMemorySizeLong`, `GetAllocatedMemoryForGraphicsDriver`, temp allocator) answer 0 | `Profiling/Profiling.cs` | W | L |
| `Debug.Break`, `EditorApplication.isPaused`/`Step` ignored | `Core/Debug.cs:256` | W | done (pause after the frame, Editor updates only; Ctrl+Shift+P in a window: tests/projects/messages) |

### Built-in render pipeline

Done: forward base/add lighting, vertex/SH lights, shadows (cascades, spot, point; casters from
fallback shaders), camera matrices and physical cameras, clear flags, MSAA, command buffers at
every forward event, OnPre*/OnPost*/OnRenderImage and [ImageEffectOpaque], depth, depth-normals
and motion-vector textures, sorting layers, SortingGroup and transparency sort axes, fog, ambient
modes, lightmaps and light probes, custom, baked and realtime reflection probes with blending/box
projection, keywords, GrabPass, LODGroup, SubShader LOD, disabled passes, `.renderTexture`
assets, Display 1 only, projectors, replacement shaders, oblique mirrors with inverted culling,
cubemap renders, camera skyboxes, deferred and vertex-lit paths, material variants, raster
states (tests/projects/builtinrp, builtinfx, display, baked, lighting).

| Gap | Where | | Impact |
|---|---|---|---|
| No frustum culling: every renderer in the mask is visible, `isVisible` always true, `OnBecameInvisible` never fires | `Graphics/FrameBuilder.cs:650-660` (the SRP path at `Srp/SrpContext.cs:749` does it) | S | done (frustum, layer cull distances, shadow casters, Unity's timing: tests/projects/messages; particles, skinned meshes and lines are still drawn when culled) |
| `ShadowCastingMode.ShadowsOnly` drawn visibly; TwoSided casting ignored | `Graphics/FrameBuilder.cs:650`, `FrameBuilder.Shadows.cs:226-246` | S | done (tests/projects/lighting shadow_shadows_only, shadow_two_sided) |
| `Camera.depthTextureMode` ignored: `_CameraDepthTexture` only exists as a by-product of directional shadows; no DepthNormals, no motion vectors | `Graphics/Camera.cs:197`, `FrameBuilder.Shadows.cs:108,174` | S | done (depth, depth-normals with Unity's replacement shader, camera and object motion vectors with Unity's shader: tests/projects/builtinrp; objects made and moved in the frame of their first motion render are taken to be still, the Editor moves some from where they were made; skinned meshes give camera motion only; soft particles alone ask for no depth texture, as measured) |
| Camera events never raised: Before/AfterDepthTexture, DepthNormals, GBuffer, Lighting, Reflections, FinalPass, HaloAndLensFlares | `Graphics/FrameBuilder.cs:714-811` | S | done (Unity's forward order with OnPreRender/OnPostRender/OnRenderObject, skybox events only with a skybox drawn: tests/projects/builtinrp events.*; GBuffer, Lighting, Reflections and FinalPass do not fire in forward, as measured) |
| `Material.color/mainTexture/mainTextureOffset/Scale` hard-coded to `_Color`/`_MainTex`; `[MainColor]`/`[MainTexture]` and other property attributes not parsed (`GetPropertyFlags` = None) | `Graphics/Material.cs:103-121`, `Shader.cs:161` | S | done (tests/projects/materials: `[MainColor] _BaseColor`, `[MainTexture] _BaseMap`, fallback names, flags and attributes) |
| `SetShaderPassEnabled` stored and answered but disabled passes still drawn (warns); `m_DisabledShaderPasses` not read | `Graphics/Material.cs`, `:530` | W/S | done (built-in forward passes and renderer lists skip disabled LightModes; the built-in shadow caster still casts, as measured: tests/projects/builtinrp disabled_passes) |
| Transparency sort mode/axis (GraphicsSettings, camera, 2D renderer data) ignored; `spriteSortPoint`, `rendererPriority` ignored | `Srp/SrpCore.cs:550`, `FrameBuilder.cs:189`, `Renderer.cs:82,462` | S | done (tests/projects/builtinrp sort_*, a project whose GraphicsSettings sort along (0, 1, 0): the setting is project-wide, so not in tests/projects/sorting; renderer priority sorts renderer lists that ask for it, the built-in pipeline ignores it as measured) |
| `.renderTexture` assets not read (always 256×256 RGBA8, no depth) | `Graphics/RenderTexture.cs:10-21`, `Runtime/Loading.cs:298` | S | done (every saved field: tests/projects/builtinrp rt.*, rt_asset; mipmaps and formats the renderer lacks are the separate rows below) |
| Baked reflection probe cubemaps not loaded from LightingData | `Graphics/ReflectionProbe.cs:40,234` | W once | done (tests/projects/baked: a baked probe and a mirror sphere in it) |
| `Projector` | stub; skipped from scenes | W | done (its material over the renderers in its frustum, `unity_Projector`/`unity_ProjectorClip` as measured, ortho and perspective, ignored layers: tests/projects/builtinfx projector*) |
| `SetReplacementShader` / `RenderWithShader` empty | `Graphics/Camera.cs:601-603` | W | done (the replacement's SubShader of the object's tag value, or the whole shader, at its queue; untagged objects dropped: tests/projects/builtinfx replace_*) |
| Planar reflections: `CalculateObliqueMatrix` stub (zero matrix), `GL.invertCulling`/`SetInvertCulling` ignored | stub; `Graphics/Immediate.cs:453`, `CommandBuffer.cs:586` | W/S | done (oblique matrices, inverted culling that stays set, `SetTargetBuffers` with one or more colors, `RenderDontRestore`: tests/projects/builtinfx mirror*, oblique.*, target_buffers*; a shader writing one output leaves a second buffer alone, the Editor's GL driver fills it) |
| `Renderer.SetPropertyBlock(block, materialIndex)` drops the index | `Graphics/Renderer.cs:224,232` | S | done (a slot's block replaces the renderer's: tests/projects/builtinrp property_blocks, blocks.*) |
| `Graphics.DrawMesh/RenderMesh*/DrawMeshInstanced`: no shadows, additional lights, probes | `Graphics/Immediate.cs:192-302`, `FrameBuilder.cs:827` | S | done (tests/projects/builtinrp draw_mesh, tests/projects/baked: light probes inside the hull, ambient outside; the instanced cubes' point-lit faces differ a little) |
| `[ImageEffectOpaque]` ignored | `FrameBuilder.cs:753-780` | S | done (tests/projects/builtinrp effect_*) |
| Main directional light ignores the light's `cullingMask` and its cookie | `Graphics/Lighting.cs:590-597` | S | done (per-object base light by culling mask, cookie suns in ForwardAdd: tests/projects/builtinrp main_light_*) |
| `Camera.targetDisplay` / `Canvas.targetDisplay` ignored (Display 2+ drawn over Display 1) | `FrameBuilder.cs:206-215`, `UI/Canvas.cs:157` | S | done (the Game view's Display 1 only: tests/projects/display) |
| Shader LOD (`maximumLOD`, `globalMaximumLOD`, SubShader `LOD`) | `Graphics/Shader.cs:41-52` | S | done (tests/projects/builtinrp lod.*) |
| Rendering path: deferred and vertex-lit absent, `actualRenderingPath` always Forward, `m_RenderingPath` not read | `Graphics/Camera.cs:195,655-680` | S | done (Deferred Shading with Unity's Internal-DeferredShading/Reflections and its camera events, Vertex Lit, `actualRenderingPath`, `m_RenderingPath`, the project's default path: tests/projects/builtinfx path_*, paths.*; deferred within a mean of 3.6/255; the light buffer is half float for LDR cameras too; tier settings are not read) |
| Realtime reflection probes, `RenderProbe`, `blendDistance`; SRP culling returns no probes | `Graphics/ReflectionProbe.cs:58,65`, `Srp/SrpContext.cs:717` | W/S | done (OnAwake/EveryFrame/ViaScripting, time slicing's finishing frames, quality switch, `BlendCubemap`, blend distance in deferred, SRP visible probes: tests/projects/builtinfx probe*; every face renders at once, IndividualFaces included) |
| `RenderToCubemap`, `SetTargetBuffers`, `layerCullDistances`, `cullingMatrix` setter | stubs; `Camera.cs:187-194` | W/S | done (every overload with face masks into cubemaps and cube render textures: tests/projects/builtinfx cubemap_*; a culling matrix set from script does not cull built-in renders, as measured, and is SRP culling's) |
| Per-camera `Skybox` component (`ScriptableRenderContext.DrawSkybox` draws `RenderSettings.skybox`) | stub | W | done (tests/projects/builtinfx sky_*) |
| LensFlare/Flare/Halo, `RenderSettings.haloStrength/flareStrength` | stubs | W | components and Flare assets load and keep their data (a FlareLayer is no longer skipped); halos and flares are not drawn and warn: Unity builds the halo falloff and flare meshes in native code, not in its resources (tests/projects/builtinfx flare_*) |
| `LightProbeUsage.UseProxyVolume` falls back to ambient (not even blend probes) | `Graphics/IndirectDraws.cs:172` | S | done for the component and uniform probes (settings, automatic resolution, `isFeatureSupported` as the Editor: tests/projects/builtinfx lppv); renderers take the probes at their centers, not the volume's 3D texture per pixel |
| Material variants (`m_Parent`), `enableInstancing`/GI flags not read; `Lerp`/CRC partial | `Graphics/Material.cs:155-170,517-526` | S | done (variants inherit live from their parent, flags read, `Lerp` in linear space with [Gamma], `CopyMatchingPropertiesFromMaterial`: tests/projects/builtinfx material.*; `ComputeCRC` is equal for equal settings, its values are Unbound's) |
| QualitySettings read but unused: soft particles, shadowmask mode, texture mipmap limit, billboards, skin weights | `FrameBuilder.cs:1063-1075` | S | done for soft particles, skin weights, the billboard keyword, `unity_ShadowColor` (tests/projects/builtinfx quality.*, skin_*, soft_particles_*); the mipmap limit (`activeMipmapLimit` answers; sampling waits for texture mips), shadowmask mode and `lightShadowCasterMode` (no shadowmask mixing) and `shadowNearPlaneOffset` warn |
| `AlphaToMask`, `ZClip`, `GL.wireframe`, `GL.sRGBWrite` | `crates/unbound-app/src/render/pipelines.rs:405` | S | done (also `CommandBuffer.SetWireframe`: tests/projects/builtinfx alpha_to_mask*, zclip, wireframe, raster.*) |
| `nonJitteredProjectionMatrix`, `previousViewProjectionMatrix` alias the current one | `Graphics/Camera.cs:302-303` | S | done (tests/projects/builtinrp matrices.*, motion*.previousViewProjection) |
| `additionalVertexStreams` (vertex paint) | `Graphics/Renderer.cs:404` | S | done (tests/projects/builtinfx vertex_streams) |
| BillboardRenderer, OcclusionArea/Portal, StaticBatchingUtility | stubs | W | done (billboards with Unity's billboard shaders, occlusion components as data with no baked occlusion, static batching's groups and frozen transforms: tests/projects/builtinfx billboard*, occlusion.*, static_batching*) |
| Static batching not done (`isPartOfStaticBatch` false, `MeshRenderer.subMeshStartIndex` 0) | `Graphics/Renderer.cs` | W | done as it shows (`isPartOfStaticBatch`, objects frozen relative to the root); meshes are not combined, `subMeshStartIndex` stays 0 |

### Render resources, low-level graphics, SRP

Done: CommandBuffer core (targets, MRT, temporaries, blits, compute, draws, renderer lists), RT
descriptors/pool/MSAA/cube/array, Texture2D CPU API and encoders, Texture2DArray/3D/Cubemap(Array)
at runtime, Mesh API (all channels, formats, topologies, MeshData), GL, ComputeShader/buffers,
AsyncGPUReadback, SRP culling/renderer lists, URP and URP 2D matching the Editor.

| Gap | Where | | Impact |
|---|---|---|---|
| SpriteMask masking not drawn; `maskInteraction` | `Graphics/SpriteMask.cs` | W once | done (stencil ranges, sorting groups, alpha cutoffs and URP 2D: tests/projects/resources and urp2d; tight outlines still use the full rect with alpha cutoff) |
| SpriteRenderer Tiled mode drawn as Sliced | `Graphics/Renderer.cs` | S | done (Continuous/Adaptive, borders, thresholds, sizes and flips: tests/projects/resources) |
| `spriteChangeEvent` / `RegisterSpriteChangeCallback` never raised | `Graphics/Renderer.cs` | S | done (raised on a sprite change through the setter; no probe yet) |
| Sprite secondary textures (`_NormalMap`, `_MaskTex`) | `Graphics/Sprite.cs`, `Renderer.cs` | S | done for import, creation, queries and per-draw texture binding (tests/projects/resources); URP 2D normal-map lighting is checked separately in tests/projects/urp2d |
| Runtime Texture2D mipmaps: one GPU level; `Get/SetPixels(mip)` use mip 0; `Apply(true)` ignored | `Graphics/Texture2D.Mips.cs` | S | done (full CPU/GPU chains, raw data, byte/float generation, NPOT, import CPU queries and API errors: tests/projects/resources mips.*) |
| Serialized/bundle textures drop every mip but the first | `Graphics/Texture2D.cs`, `crates/unbound-assets/src/bundle.rs` | S | done for serialized `.asset` chains and decoded compressed bundle chains (resources mips.*, bundlemips.*); DXT1/DXT5 bundles checked on CPU/GPU, encoded raw bytes and original format retained |
| RenderTexture mips (`useMipMap`, `autoGenerateMips`, `GenerateMips`), `ResolveAntiAliasedSurface` | `Graphics/RenderTexture.cs`, `crates/unbound-app/src/render/mipmaps.rs` | S/W | done for 2D, cube and array targets, manual/automatic/command-buffer generation and MSAA resolve (tests/projects/resources rtmips.*); 3D targets remain below |
| Camera quality MSAA adds edge coverage to a single-sample ARGBFloat target where Native leaves it single-sample | `Graphics/FrameBuilder.cs`, `Platform/SystemInfo.cs` | S | done for measured sample selection (explicit target samples, HDR-to-LDR quality intermediate, allowMSAA/HDR/buffer/effect boundaries and pre-quantization HDR resolve; all three clipping matrices now pass; tests/projects/cameramsaa) |
| Image-effect temporaries lose explicit-target precision/color conversion | `Graphics/FrameBuilder.cs`, `Graphics/RenderTexture.cs`, `Graphics/Texture2D.cs` | S | done for measured offscreen paths (source/destination precision/encoding/samples, first and chained destinations, explicit float-to-byte readback and Gamma constructor behavior; all former effect cases pass; cameramsaa follow-up) |
| Editor GameView and system-framebuffer readback source identity | `Graphics/Texture2D.cs`, `crates/unbound-app/src/render/` | uncertified | M (cameramsaa now proves distinct sources and alpha/content; retained strict failures do not justify a global gamma change; equivalent standalone/window-screen behavior remains unmeasured) |
| HDR camera with a partial viewport differs in placement/filtering | `Graphics/FrameBuilder.cs` | S | done for measured paths (camera-sized intermediates, full explicit-target filtered copy, buffer bypass, HDR at one sample, existing-color preservation for Depth/Nothing; focused oracle 481 observations/54 images per space passes) |
| Shader Color-property transfer above one in Linear color space | `Graphics/Material.cs`, `Graphics/Renderer.cs`, `Graphics/ShaderPasses.cs`, Rust material/default bindings | S | done for measured material/block paths (HDR/Gamma flags, RGB above one, negative getter round trips, source defaults/import/variants/copies, block Color/Vector distinction; Linear/Gamma EACH 2986 observations and 424 buffers pass at 2e-5; general Color/Mathf and camera/readback unchanged; arrays/shader-switch/nonfinite GPU behavior unmeasured) |
| Texture/RT formats collapse to RGBA8/half/11-11-10/depth (R16, R32F, RG float, integer, 10-10-10-2) | `crates/unbound-runtime/src/render_protocol.rs` | S | R/RG/RGBA half/full float retain their component count and precision on all runtime texture shapes and RTs (resources floatformats.*, exact bits); normalized 16-bit, integer and packed formats remain open |
| `RenderTextureSubElement.Depth/Stencil` binds the colour buffer | `Graphics/Material.cs:382`, `CommandBuffer.cs:502,578` | S | M |
| Imported `textureShape` 2DArray/3D (LUTs, flipbooks); Texture3D/2DArray/CubemapArray `.asset` readers | `crates/unbound-assets/src/texture.rs:193`, `TextureArrays.cs` | S | M |
| `CustomRenderTexture` | stub | W | M |
| `ScalableBufferManager` factors return 0 (Unity 1) | `Graphics/ScalableBufferManager.cs` | W | done (factors remain 1 after ResizeBuffers, as in the Editor: resources lowlevel.scale.*) |
| `DrawingSettings.overrideShader` (URP RenderObjects shader override) | `Graphics/FrameBuilder.cs` | S | done (original material properties, selected/default pass, material-override precedence and diagnostic: tests/projects/urp override_shader_*) |
| `CommandBuffer.DrawRenderer` draws a skinned mesh at bind pose, without its property block | `Graphics/CommandBuffer.cs` | S | done (current geometry and per-slot/renderer blocks at execution: tests/projects/resources command_early/late; late MSAA edge antialiasing differs, see README) |
| Built-in RT `CameraTarget`/`CurrentActive` resolve to null when the camera draws to the screen (verify) | `Graphics/CommandBuffer.cs:174,431` | S | M |
| Append/Counter buffers, `CopyCount`, `DispatchIndirect` | missing | W | M — GPU grass/culling |
| CommandBuffer stubs: `SetRandomWriteTarget`, `ConvertTexture`, `RequestAsyncReadback`, `GetTemporaryRTArray`, `CopyBuffer`, `SetBufferData(NativeArray)`, ... | Generated stubs | W | M (`GenerateMips` implemented, resources rtmips.*) |
| Graphics stubs: `RenderPrimitives*`, `RenderMeshIndirect`, `RenderMeshPrimitives`, `CopyBuffer` | Generated stubs | W | M |
| Blit depth slice ignored | `Immediate.cs`, `CommandBuffer.cs` | S | L-M (explicit render-target slices are implemented and probed in resources rtmips.layers.*) |
| `EnableScissorRect`; `ClearRenderTarget(Color[])` | `Graphics/CommandStream.cs`, `CommandBuffer.cs` | S/W | done (scissored draws, clears and blits, fractional/offscreen/empty rectangles, viewport intersection, distinct MRT clear colors and retained MRT binding: resources lowlevel.*) |
| LineRenderer/TrailRenderer: corner vertices, lighting data, Static texture mode | `Graphics/LineRenderers.cs:117-266` | S | L-M |
| `ScreenCapture.*`, `OnDemandRendering` | stubs | W | M |
| `MaterialPropertyBlock.CopySHCoefficientArraysFrom`, `LightProbesQuery` | stubs | W | L-M |
| 3D render targets (a 2D one stands in) | `Graphics/RenderTexture.cs:173` | W | L-M |
| `SetShadowSamplingMode`, `SetInstanceMultiplier`, `mipMapBias`, `wrapModeW`, `MirrorOnce` (`supportsTextureWrapMirrorOnce` claimed), mipmap limit | `Srp/Keywords.cs:133`, `crates/unbound-app/src/render/resources.rs:236` | S/W | L |
| Texture mipmap streaming not simulated (`streamingMipmaps*`, `desiredMipmapLevel`, ...); `Texture.totalTextureMemory/currentTextureMemory` 0 | `Graphics/Texture2D.cs`, `Cubemap.cs`, `Texture.cs` | W | L |
| `Texture2D.Compress` keeps the uncompressed format; `UpdateExternalTexture`, `GL.IssuePluginEvent` (native plugins) | `Graphics/Texture2D.cs`, `Immediate.cs` | W | L-M |
| Shader keyword declarations unknown (`LocalKeywordSpace.keywords`, `LocalKeyword.isDynamic/isOverridable`), `Shader.subshaderCount`, `Material.isVariant` | `Graphics/Srp/SrpContext.cs`, `Shader.cs`, `Material.cs` | W | L-M |
| `CullingResults` per-object light/probe index lists (`FillLightAndReflectionProbeIndices`, counts) | `Graphics/Srp/SrpContext.cs` | W | L — custom SRPs only |
| `SystemInfo` machine identity not read: `deviceModel`, `graphicsDeviceID/VendorID/Version`, `graphicsMemorySize`, `processorFrequency/Manufacturer`, battery on macOS | `Platform/SystemInfo.cs` | W | L — analytics, per-GPU workarounds |
| Renderer below the Editor's device (warn when queried): no geometry/tessellation shaders, sparse textures, conservative raster, graphics fences, RT array index from the vertex shader, MSAA 2D arrays; compute groups 256 (Editor 1024), 3D textures 2048, array slices 256 | `Platform/SystemInfo.cs`, wgpu limits | W | L-M — packages take their fallback path |
| BatchRendererGroup / GPU Resident Drawer | stubs | W | L (off by default) |

### Physics, particles, navigation, terrain

Done: PhysX 4.1 bodies, shapes, forces, queries (NonAlloc, Check, ComputePenetration), layer matrix,
simulation modes, collision/trigger messages, 3D joints, CharacterController, WheelCollider,
Cloth; Box2D bodies, colliders (incl. composite/tilemap), queries, materials; the particle modules
in ROADMAP; NavMesh queries/builds/agents/avoidance; terrain heights, collider, details.

| Gap | Where | | Impact |
|---|---|---|---|
| `RaycastHit.textureCoord/textureCoord2/lightmapCoord` | `Physics/PhysicsTypes.cs`, `PhysicsQueries.cs`, native query shim | W | done (3203 Editor records: ray/sweep barycentrics, cooked face remapping, submesh base vertices, live UV/mesh reads, UV2 fallback diagnostics and renderer lightmap transforms; tests/projects/rayhits) |
| Joint2D family (Hinge, Distance, Spring, Wheel, Slider, Fixed, Friction, Relative, Target) | `Physics2D/*Joints2D.cs`, Box2D solvers | W | done for all nine components (1401 Editor records, including serialization/cloning, limits/motors, reflected parents, body lifecycle, break actions and Target diagnostics; 19 separately reported solver-rounding records) |
| Effector2D family (Platform, Area, Buoyancy, Point, Surface); `usedByEffector` | `Physics2D/*Effectors*.cs`, Box2D contacts and post-step volume forces | W/S | done for all five components (2559 Editor records, 28 measured native contact-order records; defaults, limits, serialization/cloning, masks/lifecycle, private RNG, force selection/falloff, submerged geometry and drag) |
| 2D sensors evaluate poses one simulation late; mixed trigger/contact callbacks interleave | `Physics2D/Box2D/Dynamics/World.cs`, `ContactManager.cs`, `Physics2DWorld.cs` | S | done for measured Unity 6000.0.71f1 motion and mixed-order controls: the broad trigger enters at step 44 and exits at 56; 1343 previously failing keys resolve without new failures. Sensors update after integration and trigger callbacks precede solid contacts. Lifecycle/sleep limits are separate below. |
| 2D contact/trigger lifetime, teardown and sleep/static-transform behavior | `Physics2D/Physics2DWorld.cs`, Box2D body/contact lifecycle and 2D destruction hooks | S | done for measured Unity 6000.0.71f1 controls: all 376 prior failures resolve, timing18491/18491 and mixed order5720/5720 pass. New lifecycle25356 and receiver-order1152 observations are exact against independent agreeing Native pairs. Broader multi-pair reentrancy and hierarchy/scale cases remain unmeasured. |
| Remaining broad 2D contact geometry and stack motion | `Physics2D/Physics2DWorld.cs`, Box2D contacts/solver | S | partial: 45 original broad values remain across 2d.stack27, 2d.bounce9 and 2d.kinematic9. The 76 original query failures resolve; combined broad matches11581/11626 with zero introduced values. Expanded motion differences and rejected candidates remain in physics2dmotion; complete combined gates are in query2dskinmerge. |
| 2D query initial contact features, fine selection and shape grouping | `Physics2D/Physics2DQueries.cs`, shared Box2D collision algorithms | S | partial: main controls52347/55251, selection1439/1440, fine selection1722/1728, support geometry66/86 union and frame/center controls72679/75168. Exact segment/circle coincidence resolves528 existing query values without new failures. Independent contact-feature controls match174044/181440, retaining7396 differences. All nine suites retain their original thresholds; initial6256, box skin644, boundary64 and isolated48 controls pass. Further query and non-Box skin parity remain open. |
| Particle custom vertex streams (`SetActiveVertexStreams`, `m_VertexStreams`) | `Particles/ParticleSystemRenderer.Streams.cs`; 3544 main/trail Editor records plus 23646 noise observations for packed attributes, random/noise vectors, transformed baking, particle aging, tail expiry and ribbon joins | W/S | done for measured main/trail/noise streams and declarations, with three byte-identical GPU images; further renderer edge cases remain separate |
| `ParticleCollisionEvent.colliderComponent` always null | `Particles/ParticleTypes.cs:409` | S | done (answers the hit collider; no probe yet) |
| `NavMeshObstacle` does nothing (no carving, no avoidance obstacle) | `AI/NavMeshObstacle.cs:197-220` | S (carving warns) | **H** where used |
| Project physics defaults (solver iterations, sleep threshold, max angular speed, max depenetration) overwritten per body by hard-coded values | `Physics/Rigidbody.cs`, `Physics/Physics.cs` | S | done for Unity 6000.0.71f1 (creation snapshots, activation/allocation, scene/prefab loading, clone resets, setter boundaries and motion; tests/projects/physicsdefaults matches 582/582 with the original motion tolerance; older releases and invalid-value simulation unmeasured) |
| Collider contact-offset default lifetime and live assignment | `Physics/Collider.cs`, `Physics/Box3D.cs`, `native/unbound_physx.cpp` | S | done for measured cases (creation snapshots, clone resets, shape-dependent assignment, activation and live box contact callbacks; all 193 added observations equal Native exactly; controller motion with overrides and terrain tree shapes unmeasured) |
| DynamicsManager solver/friction/contact-pair flags and determinism | `Physics/Physics.cs`, `PhysicsWorld.cs`, `native/unbound_physx.cpp`, PhysX shape lifecycle | S | done for measured Unity 6000.0.71f1 workloads: 5055/5055 observations including all 28 prior island-history failures, with 318 managed tests and clean API-check. Trigger conversion/pairs and ignore lifetime have independent Native controls; SDKLifecycleEvidence retains all values. Older releases and other geometry lifecycle cases remain unmeasured. |
| Active Rigidbody MovePosition/MoveRotation and transform-sync wake timing | `Physics/Rigidbody.cs`, `PhysicsWorld.cs`, native pose/target bindings | S | done for measured Unity 6000.0.71f1 cases: 20700/20700 active-body observations equal two agreeing Native runs; extended triggers 1728/1728 and all three SDK controls pass exactly. All 162 prior targeted sleep differences resolve. SleepEvidence retains the complete measurements; older releases remain unmeasured. |
| Inactive Rigidbody pose/setter and reactivation behavior | `Physics/Rigidbody.cs`, `PhysicsWorld.cs`, `Runtime/Loading.cs` | S | done for measured Unity 6000.0.71f1 cases: 1050 original, 4668 sequence and 9312 serialized-loading records match exactly. Completed serialized fields/hierarchy are captured before caller edits; getter-triggered capture is removed. Inactive physics poses remain separate from Transform, activation takes Transform, and kinematic deactivation discards computed target velocity. Non-unit scales, other rotations and further loading mechanisms remain unmeasured. |
| Public Physics.ContactEvent / ContactPair API | `Physics/ContactEvent.cs`, `ContactPair.cs`, `PhysicsWorld.cs`, native contact reports | W | done for measured Unity6000.0.71f1 cases: all29628 observations pass the original physical bounds, capsule sweeps5610 are exact, with measured40/40/48-byte layouts, real SDK points/impulses/order, borrowed-array safety, mesh faces, callback mutation and DestroyImmediate restrictions. Capsule components default to height1; capsule/cylinder primitives explicitly use2. Two process-dependent hash rows remain visible. Local scenes, asynchronous lifetimes, non-unit capsule transforms and older releases remain unmeasured. |
| `LocalPhysicsMode` / `PhysicsScene(2D).Simulate`: local scenes join the main world | `Core/SceneManagement.cs:74-97` | S | M-H — trajectory preview |
| `autoTraverseOffMeshLink = false` ignored; link traversal speed is Detour's | `AI/NavMeshAgent.cs:68` | S | M-H |
| `NavMeshAgent.isPathStale` always false, `navMeshOwner` null | `AI/NavMeshAgent.cs` | W | L |
| CCD modes all map to sweep CCD (no speculative) | `native/unbound_physx.cpp:1409-1414` | S | M |
| `includeLayers`/`excludeLayers` + priority (3D) partial | `Physics/PhysicsWorld.cs:540-549` | S | M |
| EdgeCollider2D segments independent (no ghost vertices) | `Physics2D/Collider2D.cs:986-1049` | S | M — snagging on seams |
| `SetTriggerParticles` not written back (warns); collision/trigger sub-emitters; `colliderForce` | `Particles/ParticleTypes.cs:449`, `ParticleSystem.Simulation.cs:813` | W/S | M |
| `GetTrails/SetTrails`, `BakeTexture/BakeTrailsTexture` empty (`SetTrails`, `Bake*Texture` warn) | `Particles/ParticleSystemRenderer.cs`, `ParticleSystem.cs` | S/W | partial: BakeTrailsMesh now matches the trail oracle, including flags and native multi-ribbon bake failure; data round-trip and texture baking remain open |
| Particle mesh shape (triangle/edge, spawn mode, mesh colours); SkinnedMeshRenderer shape falls back to a box | `Particles/ParticleSystem.Simulation.cs:738-758` | S | M |
| Batch queries (`RaycastCommand`, `SpherecastCommand`, `Overlap*Command`, ...) | stubs | W | M |
| `ConstantForce`, `ConstantForce2D` | `Physics/ConstantForce.cs`, `Physics2D/ConstantForce2D.cs` | W | done (608 Editor records: fixed-script scheduling and native order-zero placement, manual accumulation, world/relative forces and torques, 2D angular units and zero-force waking, native multiplicity, serialization/cloning and body lifecycle; tests/projects/forces) |
| `ContactModifyEvent` (empty event stub) | Generated | S | M |
| Terrain baked lightmap import/rendering | `Terrain/TerrainRenderer.cs`, `Graphics/Lightmapping.cs`, `crates/unbound-shaders/src/surface.rs` | S | done for baked lighting (LightingData bindings, per-terrain indices/ST/UVs, directional/missing maps, original Diffuse/Standard and ordinary surface meshes in Linear/Gamma); realtime GI remains unsupported with a warning, shadowmask/add-pass mixing need more coverage |
| Realtime GI charts, dynamic lightmap textures and shader bindings | `Graphics/Lightmapping.cs`, `Terrain/TerrainRenderer.cs` | W/S | M-H (four retained realtime cases in tests/projects/terrainlightmaps; Terrain warns, other renderer paths need an audit) |
| Terrain auto-connect (`SetConnectivityDirty`), `normalmapTexture`, patch LOD overrides | `Terrain/Terrain.cs`, `TerrainData.cs` | W | L |
| HingeJoint `freeSpin`/`extendedLimits`, `queriesHitBackfaces`, cooking options | `Physics/Joints.cs`, `Physics/Physics.cs:191`, `Physics/Collider.cs` | S | L-M |
| 2D sub-stepping, `simulationLayers`, force send/receive layers, `Rigidbody2D.Slide` | `Physics2D/*` | S/W | L-M |
| Particles: `cullingMode`, stretched-billboard details, `safeCollisionEventSize`, vector fields (noise rotation/size now measured and implemented) | `Particles/*` | S | L-M |
| `ArticulationBody`, `Physics.BakeMesh`, `TerrainCallbacks`, `Tree`/SpeedTree wind | stubs | W/S | L |

### Animation, audio, video

Done: generic AnimatorController (layers, masks, sub-machines, all transition settings, triggers,
parameters, write defaults), Play/CrossFade API, override controllers, StateMachineBehaviour
enter/update/exit, update modes, 1D and Direct blend trees, generic root motion on the Animator's
own transform, legacy Animation, AnimationClip curves of every kind, AudioSource (all main
properties, scheduling, output/spectrum data), AudioClip creation/streaming/import, mixer
volume/snapshots/exposed parameters, `OnAudioFilterRead`, VideoPlayer.

| Gap | Where | | Impact |
|---|---|---|---|
| Humanoid: avatars, retargeting, muscle curves, IK (`SetIK*`, `OnAnimatorIK`), `GetBoneTransform` | `crates/unbound-assets/src/model.rs:506`, `Animation/Binding.cs:743`, stubs | W | **H** for 3D character games |
| Generic root motion: `ApplyBuiltinRootMotion`, velocities, `OnAnimatorMove`/`OnStateMove`, `rootPosition`/`rootRotation` | `Animation/Animator.RootMotion.cs`, `AnimatorPlayer.RootMotion.cs` | W/S | done (658 Editor records: callback order, disabled handlers, apply flag, exactly-once builtin motion, zero/backward/multi-loop updates, nested and negative scale, independent API root pose: tests/projects/animator) |
| Rigidbody/CharacterController root motion integration | `Animation/Animator.RootMotion.cs`, `Physics/CharacterController.cs` | W/S | done for attached bodies/controllers (animator root.body.*): dynamic velocity preserves its component along root up, kinematic pose, controller gravity, Normal/Fixed, frame time versus evaluation time, repeated evaluations and manual simulation; body collision cases need more coverage |
| Imported root node (`rootMotionBoneName`) and Bake Into Pose; `MatchTarget` | `Animation/AnimatorPlayer.RootMotion.cs`, model import | W/S | **H** — root-node extraction and matching still absent |
| AudioMixer effects dropped (lowpass, highpass, echo, reverb, compressor, duck, send/receive, ...); `SetFloat` on them does nothing | `Audio/AudioMixer.cs:12,374,422-440` | S | **H** |
| Audio filter components and `AudioReverbZone` | stubs; skipped | W | M-H |
| 2D directional blend trees weighted with Cartesian, not polar, gradient bands | `Animation/AnimatorPlayer.Blending.cs` | S | done (Simple Directional fans, idle/no-idle, irregular and degenerate layouts, Freeform Directional polar bands; text and compiled bundle controllers: tests/projects/animator blend.*) |
| Constraint components (Parent/Position/Rotation/Scale/Aim/LookAt) | `Animation/Constraints/*` | W | done for runtime and serialization, 13804 Editor records including all up modes, roll, activation, dependencies, weight limits, hierarchy rotation/scale and prefab cloning; degenerate aim frames and dependency cycles need more coverage |
| `isHuman`/`hasRootMotion`/`humanScale`/`isOptimizable`, `Avatar.isValid/isHuman` constant (warn); a model's generic avatar is read (`avatarSetup`: Animator and `<model>Avatar` only with one, tests/projects/modelimport) | `Animation/Animator.cs:90-92`, `AnimatorAvatar.cs` | W/S | M |
| Additive layers: reference pose and rotation composition | `Animation/AnimationPose.cs:245,300` | S | M |
| ModelImporter clips: curves (Animator parameters), masks, additive reference frame, mirror, loop pose, cycle offset | `crates/unbound-assets/src/model.rs:680-715`, `AnimationClip.cs:562` | S | M |
| `optimizeGameObjects` / exposed transforms | `crates/unbound-assets/src/model.rs` | S | L-M |
| Direct blend tree normalization | `Animation/AnimatorPlayer.Blending.cs`, `AnimatorController.cs`, `CompiledController.cs` | S | done (normalization flag, raw weights above one, clamped negatives and defaults below one; text and compiled bundle controllers: tests/projects/animator blend.Direct*) |
| Synced layer timing | `Animation/AnimatorController.cs` | S | L-M |
| `cullingMode` / `cullingType` stored only | `Animation/Animator.cs:124`, `Animation.cs:87` | S | L-M |
| `UnityWebRequestMultimedia.GetAudioClip` | stub | W | M |
| Playable root motion (`AnimationStream.velocity`, ...) | `Animation/Playables/AnimationJobs.cs:387-429` | W | L-M |
| Clip root-motion stats (`averageSpeed`/`apparentSpeed`/`averageAngularSpeed`, `hasMotionCurves`/`hasRootCurves`/`hasMotionFloatCurves`, `humanMotion`), `EnsureQuaternionContinuity`, `Animation.SyncLayer`, `AnimatorControllerPlayable.IsParameterControlledByCurve` | `Animation/AnimationClip.cs`, `Animation.cs`, `Playables/AnimatorControllerPlayable.cs` | W | L-M |
| Spatializer and ambisonic plugins not loaded (`GetSpatializerPluginNames` empty, `Set*Float` fail) | `Audio/AudioSettings.cs`, `AudioSource.cs` | W | L-M (VR audio) |
| VideoClip/VideoPlayer audio track info (channels, rate, language), `VideoClip.sRGB` | `Video/VideoPlayer.cs` | W | L-M |
| Tracker modules (`.xm/.it/.mod/.s3m`); `Microphone.devices` null | `crates/unbound-assets/src/audio.rs:28`, stub | W | L-M |
| `velocityUpdateMode`, VideoPlayer `timeReference`/`skipOnDrop` | `Audio/AudioSource.cs:199`, `Video/VideoPlayer.cs` | S | L |

### UI, text, input

Done: Canvas render modes, sorting, nested overrides, CanvasRenderer (meshes, materials, pop
materials, culling, alpha), CanvasGroup, RectTransformUtility, legacy fonts and TextGenerator,
FontEngine (raster/SDF/SDFAA, kerning), IMGUI, UI Toolkit (UXML/USS, layout, rendering, text,
ListView), legacy Input Manager axes, Input System keyboard/mouse/gamepads, InputForUI.

| Gap | Where | | Impact |
|---|---|---|---|
| `Cursor.lockState`/`visible` stored only, `SetCursor` empty (warns); no raw mouse delta while locked | `Core/Input.cs:134-136`, `crates/unbound-app/src/lib.rs:316` | S | done (window grab/visibility/image, raw motion while locked; the Editor's values: tests/projects/messages) |
| USS transitions and lifecycle events | `UIElements/Style/StyleTransitions.cs`, `TransitionEasing.cs`, `Events/TransitionEvents.cs` | S | done (253 Editor samples, 831 callbacks, all 23 easing modes, delays/reversal/interruption, inheritance/lifecycle and mixed transform units; cross-element ordering and callback retargeting need further coverage) |
| Canvas creation, driven RectTransform state and lifecycle | `UI/Canvas.cs`, `CanvasSystem.cs`, `Core/RectTransform.cs` | S | done for the measured Unity 6 runtime matrix (8671 observations, 293 snapshots and 401 callbacks; authored/driven poses, nested setters, activation, reparenting, resize, scene/prefab/clone behavior) |
| Built-in uGUI sprites (UISprite, Background, Knob, Checkmark, ...) drawn in code, not Unity's | `Graphics/AssetImporters.cs:328-450` | S | M-H (against the no-hand-ports rule) |
| Press and release within one frame lost (mouse buttons, legacy keys) | `crates/unbound-app/src/lib.rs:270-285`, `Input/NativeInput.cs:440` | S | M |
| RectMask2D is a screen scissor: no `_ClipRect`/`UNITY_UI_CLIP_RECT`, no softness, rotated canvases clip wrong | `UI/CanvasSystem.cs`, `UI/CanvasRenderer.Clipping.cs`, `Graphics/FrameBuilder.cs` | S | done for measured paths (root-space batching, clipping uniforms/local keyword precedence, negative/fractional softness, live masks/pop draws, direct renderer and real uGUI across WorldSpace/Camera/Overlay; Linear, Gamma and quality-MSAA each 1211 observations and 266 images pass after the camera sample-selection follow-up) |
| `Canvas.vertexColorAlwaysGammaSpace` and byte color batching | `UI/CanvasRenderer.Colors.cs`, `CanvasSystem.cs` | S | done (98402 Editor observations in Linear/Gamma: source/tint conversion, byte quantization, nearest Canvas flags, original UI/Default and custom shaders, alpha/group inheritance, toggles, mesh snapshots, pop draws and cloning; World Space and Screen Space Camera) |
| `systemCopyBuffer` is in-process only | `IMGUI/GUIUtility.cs:49` | S | M |
| IME: `compositionString` = "", `imeIsSelected` false (warn), Bevy IME events not read | `Core/Input.cs` | W | M (CJK input) |
| `TextMesh` | stub; skipped | W | M |
| UXML: asset references, typed attributes, `[UxmlAttribute]` aliases, `UxmlTraits` dropped | `UIElements/Uxml/VisualTreeAsset.cs:232-490` | S | M |
| UI Toolkit runtime stubs: Painter2D, TreeView, MultiColumn*, TabView, RadioButton, EnumField, MinMaxSlider, DataBinding, ContextualMenuManipulator, DropdownMenu | stubs | W | M |
| IMGUI GameSkin built procedurally | `IMGUI/GUIStyle.cs:790-960` | S | L-M |
| Colour glyphs/emoji, hinted render modes, GPOS marks/GSUB | `Text/FontEngine.cs:288-550`, `crates/unbound-assets/src/font.rs` | S/W | L-M |
| `Mouse.WarpCursorPosition`; mouse position frozen outside the window | `Input/NativeInput.cs:209`, `crates/unbound-app/src/lib.rs:316` | S | L-M |
| Legacy pen input (`Input.penEventCount/GetPenEvent/GetLastPenContactEvent`) never read | `Input/LegacyPen.cs` | W | L |
| `GUI.DrawTexture(alphaBlend: false)`, border radius | `IMGUI/GUI.cs:243-268` | S | L-M |
| `CanvasRenderer.SetAlphaTexture` | `UI/Canvas.cs` | S | L |
| `Screen.currentResolution`/`dpi`/refresh rate constants | `Core/Input.cs:31-33` | S | L |

### Assets, import, networking

Done: texture import (sprites, NPOT, max size, alpha, cubemaps, sRGB/normal maps), audio import
settings, model scale/blend shapes/generic rigs, AssetBundles, Addressables (AssetDatabase mode),
ScriptedImporters, UnityWebRequest core (multipart, certificates, timeouts, file, texture).

| Gap | Where | | Impact |
|---|---|---|---|
| FBX embedded materials import as white Standard (`materialImportMode`, search, URP default) | `crates/unbound-assets/src/model/materials.rs`, `crates/unbound-runtime/src/models.rs`, `Editor/FBXMaterialDescriptionPreprocessor.cs`, `Editor/ModelMaterialImport.cs` | S | done for the built-in pipeline (6033 Editor values in Gamma/Linear and 35 material descriptions: MaterialDescription preprocessing with Standard, Autodesk Interactive, Arnold and 3ds Max materials, the legacy importer, external materials by name and search, texture search; tests/projects/modelimport). Render pipelines' and projects' `OnPreprocessMaterialDescription` run too, URP's not yet compared with the Editor; material animation curves are not imported |
| `UnityWebRequestAssetBundle`, `DownloadHandlerAssetBundle`, `Caching` | stubs | W | **H** for remote content / Addressables "existing build" |
| LODGroup from `_LOD0/_LOD1` node names | `crates/unbound-assets/src/model.rs` | S | done (levels, heights, bounds, gaps, case, mesh-less level nodes: tests/projects/modelimport) |
| `generateSecondaryUV` (lightmap UVs) | `crates/unbound-assets/src/model.rs:33` | S | M-H in baked scenes |
| `AssetPostprocessor` callbacks other than `OnPreprocessMaterialDescription` never run | stub | W | M |
| `SpriteMeshType.Tight` outline; default physics shape | `crates/unbound-assets/src/sprite.rs:15-95` | S | M |
| Normal map from grayscale, `normalSmoothAngle`, mipmap filters/preserve coverage | `crates/unbound-assets/src/texture.rs`, `model.rs:121` | S | L-M |
| DDS/KTX/ASTC sources; `.dae/.3ds/.blend/.max` | `crates/unbound-runtime/src/state.rs:144-150` | S | L-M |
| `DownloadHandlerScript.ReceiveData` / `DownloadHandlerFile` progressive delivery | `Networking/Handlers.cs`, `UnityWebRequest.cs` | S | done (458 Editor observations: main-thread ordered callbacks, buffer ownership, bounded streaming, progress, abort/rejection/disposal/errors, append and remove-on-abort; tests/projects/networkstreaming) |
| `SystemInfo.SupportsTextureFormat` always true; `supportsAsyncGPUReadback` false | `Platform/SystemInfo.cs` | S | done (the Editor's measured format table: tests/projects/urp caps) |
| `TextureImporter`/`ModelImporter` scripting API; `AssetDatabase.Refresh/CreateAsset/...` | `Editor/EditorMisc.cs:313`, `Editor/AssetDatabase.cs:288` | S/W | L |
| `AssetDatabase.ImportAsset` does not re-import; `ShaderUtil` compiler messages unknown (always none) | `Editor/AssetDatabase.cs`, `Editor/ShaderUtil.cs` | W | L |
| Unity Transport TLS (`Unity.TLS.LowLevel.Binding`) | stub | W | L-M (secure multiplayer) |

## Order of attack

1. **Make gaps loud** (the tooling list above). After it, this file is regenerated from
   `api-check`/`doctor` output instead of audits.
2. ~~**Messages and the player loop**~~: done (tests/projects/messages): `OnMouse*`,
   focus/pause, `OnBeforeTransformParentChanged`, frustum culling with
   `isVisible`/`OnBecameInvisible`, `ShadowsOnly`/TwoSided, the FixedUpdate cap, cursor
   lock/visibility, DontSave roots, coroutines on disabled behaviours, `Debug.Break`,
   `Application.Quit`.
3. **Built-in RP correctness**: ~~depth/depth-normals/motion-vector textures with their camera
   events, main material properties from shader attributes, disabled passes, transparency sort
   axis, `.renderTexture` assets, baked reflection probes~~ (done: tests/projects/builtinrp,
   baked, display, materials), ~~Projector, replacement shaders, oblique matrices with inverted
   culling, cubemap renders, realtime probes, deferred and vertex-lit paths~~ (done:
   tests/projects/builtinfx); SpriteMask and tiled sprites, texture mip chains and formats (one
   protocol change).
4. **Animation and audio**: one root-motion pass with a probe; mixer effects and filter
   components (one DSP chain); constraints; polar 2D blend trees; then humanoid avatars (large).
5. **Physics**: ~~2D joints~~ (done: tests/projects/joints2d), effectors, particle vertex streams and
   collision events, NavMeshObstacle, project defaults and DynamicsManager settings, local
   physics scenes, raycast UVs.
6. **UI and text**: ~~USS transitions~~ (done: tests/projects/uitransitions), per-draw `_ClipRect`, Unity's built-in UI sprites and GameSkin
   from its resource files, input events within a frame, clipboard, IME, TextMesh.
7. **Assets and networking**: FBX materials, LOD names, lightmap UVs, AssetBundle downloads and
   caching, audio downloads, `InstantiateAsync`.

Deferred by decision (see ROADMAP): VFX Graph, XR, LOD cross-fades, Terrain leftovers, console
codecs.
