# Roadmap: Unity 6000.0 packages running unmodified

Goal: any package a Unity 6000.0 project installs (Unity's own and third-party) runs under Unbound
as it is, because the engine API it calls behaves as Unity's. Packages are never patched; what they
need is implemented in the engine, and every behaviour is checked against the Unity Editor
(`tests/projects/*` probes record the Editor's reference, Unbound is compared with it).

The measure is `tests/projects/packages`: a Unity 6000.0.71f1 project with 86 packages (every
runtime-relevant package the Editor ships offline, URP, Shader Graph, VFX Graph and all built-in
modules). `unbound graph --compare-bee` checks the assembly graph against the Editor's record;
`unbound api-gaps -p tests/projects/packages` lists the API the packages' code references that Unbound
lacks.

## Where it stands (2026-10-24)

* Compiling: the 188 script assemblies of the packages project match the Editor's graph exactly
  (references, defines, .NET Standard 2.1 and .NET Framework levels) and all compile.
* API the packages reference: 2698 of 5970 members implemented (80% of uses), 722 of 1406 types.
* Works in a real game: core objects and lifecycle, scenes and prefabs, Unity
  serialization, uGUI, TextMeshPro, IMGUI, 2D physics (Box2D), 3D physics (Box3D), legacy and
  Animator animation, particles, audio and mixers, UnityWebRequest, Addressables in Play mode
  (AssetDatabase mode), Localization, shaders compiled from Unity's sources (built-in RP),
  ScriptedImporters, source generators, hot reload.

Missing API by package (members / uses):

| Package | Missing | Mostly |
|---|---|---|
| Render Pipelines Core, URP, URP 2D, GPU Driven, Light Transport | 1700 / 14800 | SRP: CommandBuffer, ScriptableRenderContext, culling, RenderTexture descriptors, compute, GraphicsBuffer |
| Input System (+ ForUI) | 486 / 3560 | editor UI (inspectors, settings windows), UI Toolkit; the engine side is done |
| Timeline, Sequences, Cinemachine, Animation Rigging | 53 / 231 | Editor API (Undo, AnimationUtility), BoundingSphere, CompositeCollider2D, humanoid avatars |
| VFX Graph | 160 / 1700 | VisualEffect, compute |
| Post Processing v2 | 100 / 1570 | CommandBuffer, RenderTexture, compute |
| Collections, Burst, Logging, Transport, Serialization | 100 / 350 | UnityTLS (secure Transport), Editor hooks |
| 2D Animation, SpriteShape, Tilemap Extras | 130 / 1760 | SpriteShape module, sprite data access, jobs |
| AI Navigation | 60 / 140 | NavMesh |
| Notifications, Ads, IAP, LevelPlay | 50 / 900 | AndroidJNI as the Editor answers it |
| Test Framework, UnityEditor.UI | 450 / 3300 | Editor UI API (inspectors), build pipeline |

## Work list

In dependency order; each item ends with an Editor-reference probe.

### 0. Pipeline
- [x] Package compile parity: .NET Standard 2.1 level, CodeGen assemblies, repeated asmdef keys,
      runtime assemblies referencing Editor-only ones.
- [x] Run IL post-processors (`*.CodeGen`: Burst, Collections, Netcode, Entities) on compiled
      assemblies, as the Editor does after each compile. (Edit/Play mode; not yet in Player
      compiles, whose graph has no CodeGen assemblies.)
- [ ] Resolve registry packages without an Editor-made `Library/PackageCache`: unpack the tarballs
      the Editor ships into the Unbound package store.

### 1. Core runtime
- [x] Jobs: JobHandle, IJob/IJobFor/IJobParallelFor(Transform), JobsUtility (reflection data,
      scheduling on worker threads), dependencies and Complete. Probe: `tests/projects/jobs`.
- [x] Collections low level: AtomicSafetyHandle (Editor safety checks), UnsafeUtility complete,
      container attributes, Baselib bindings.
- [x] Burst in the Editor without native compilation: BurstCompiler runs code managed, as with
      Burst compilation off.
- [x] Profiling: ProfilerRecorder, Recorder, ProfilerMarkerData, ProfilerUnsafeUtility.
- [x] Playables: PlayableGraph, Playable/ScriptPlayable, outputs, PlayableDirector; animation
      playables (clip, mixer, layer mixer, controller), audio playables, animation jobs
      (AnimationScriptPlayable, stream handles). `tests/projects/playables` matches the Editor in
      every scenario (graph callbacks and timing, director, animation and audio playables, jobs,
      a Timeline with animation/activation/audio/signal/control tracks, Animation Rigging, a
      Cinemachine track) but one: objects destroyed from Update are destroyed later in the frame
      than in the Editor (OnDisable/OnDestroy right after the Update scripts). Left: humanoid
      avatars (generic animation only), root motion applied to the Animator's transform,
      Experimental Camera/Texture/Material playables, DataPlayable/PlayableSystems.
- [x] AndroidJNI, iOS and platform APIs answering exactly as the Linux Editor does:
      AndroidJNI (defaults), the Java wrappers over it (AndroidJavaObject/Class/Proxy, field
      lookups failing with Unity's message), AndroidJNIHelper signatures and argument arrays,
      Android permissions and application, Handheld, iOS.Device, webcam and microphone without
      devices, Screen.orientation (Portrait, setting ignored). `tests/projects/platform` matches the
      Editor line for line (156 calls). A player's working platform APIs are out of scope: Unbound
      runs projects as the Editor does. Left: webcam and microphone capture on machines that have
      them (warns), Microphone.devices, Android asset packs and game mode APIs.
- [x] Unity.Properties (PropertiesModule): property bags (reflected and registered), visitors,
      PropertyContainer, PropertyPath; `tests/projects/properties` matches the Editor (117/117).

### 2. Input
- [x] Input System native backend (`NativeInputSystem`): the window's keyboard and mouse as the
      Linux Editor reports and feeds them (devices, state and text events, device commands,
      updates in the player loop), active input handling (defines, legacy `Input` refusing),
      InputForUI module, project-wide actions and settings assets, PlayerInput, uGUI through
      `InputSystemUIInputModule`; `tests/projects/input` matches the Editor record for record.
- [ ] Gamepads and joysticks (the Linux Editor's SDL-described devices) and touchscreens: needs
      the window's gamepads in `FrameInput` and an Editor reference recorded with a device.

### 3. Rendering
- [x] Built-in RP extension points: CommandBuffer, camera command buffers, Graphics.Blit,
      DrawMesh/DrawMeshNow/DrawMeshInstanced/RenderMesh/RenderMeshInstanced/DrawProcedural/
      DrawTexture, GL, RenderTexture (descriptors, formats, MSAA, depth, temporaries), Mesh
      vertex/index buffers and MeshData, Texture2DArray/3D/Cubemap, AsyncGPUReadback, camera MSAA
      from the quality level; `tests/projects/graphics` matches the Editor (40/40).
- [x] Built-in RP forward lighting beyond the main directional light: additional pixel lights
      (ForwardAdd passes, light importance and pixelLightCount, attenuation textures, cookies),
      vertex and SH lights, and shadows (the camera depth texture, directional cascades through
      Unity's screen-space collector, spot maps, point cubes, other shadowed suns' own masks);
      `tests/projects/lighting` matches the Editor (shadows within 1/255 on average). Left: the
      default spot cookie is a fitted curve (the cone's edge, 2/255).
- [ ] Left of the built-in RP: CubemapArray, RenderBuffer and GraphicsTexture overloads,
      indirect draws. (Light command buffers run around the built-in shadows: the shadow map, its
      passes and the screen-space mask.)
- [x] Compute: ComputeShader compiled from Unity's sources, ComputeBuffer, GraphicsBuffer
      (`tests/projects/graphics`, 25/25 cases match the Editor).
- [x] Scriptable Render Pipeline core: RenderPipelineAsset/RenderPipeline/RenderPipelineManager,
      GraphicsSettings and QualitySettings pipelines, global settings with their graphics
      settings, ScriptableRenderContext (culling, renderer lists, render passes, camera setup),
      SubShader selection by the pipeline tag, passes by LightMode, keywords, SystemInfo as the
      Linux Editor's OpenGLCore device (measured), SphericalHarmonicsL2 and the ambient probe.
- [x] URP 17 forward rendering through its render graph (native render pass compiler), unmodified:
      Lit and Unlit, the main light, the scene lighting data's ambient probe and default
      reflection cubemap; `tests/projects/urp` matches the Editor's image (mean difference
      0.01/255).
- [x] URP: shadows (main light cascades, spot and point lights), additional lights (per-object
      lists, Forward+ clusters), depth/opaque textures, post-processing (Volume stack), MSAA,
      render scale, camera stacking, RenderStateBlock overrides, structured buffers; every
      `tests/projects/urp` scenario within 0.21/255 of the Editor (deferred falls back to forward
      on the Editor's OpenGL device).
- [x] URP 2D renderer: sprites lit by 2D lights (global, point, spot, freeform, sprite), 2D
      shadows (ShadowCaster2D, on Clipper2D), the light textures and blend styles;
      `tests/projects/urp2d` matches the Editor within 0.01/255.
- [ ] Multiple render targets (deferred off OpenGL); the exact directional shadow slice depth
      (edge pixels of `shadows` differ).
- [x] Shader Graph assets: their ScriptedImporter runs unmodified and the ShaderLab it generates
      (`Library/Unbound/GeneratedShaders`) is compiled like any shader; `tests/projects/shadergraph`
      matches the Editor's image (mean difference 0.02/255).
- [ ] VFX Graph.
- [x] Baked lighting: a scene's LightingData (read through its type tree): lightmaps and each
      renderer's lightmap placement, light probes (tetrahedral interpolation), baked lights out of
      the realtime ones, the skybox reflection; cubemap textures from images (strips, crosses,
      panoramas, glossy convolution). `tests/projects/baked` matches the Editor (mean difference
      0.64/255). Directional lightmaps and shadowmasks are read but untested; additive scenes'
      probes are not merged.
- [x] QualitySettings levels from the project (shadows, AA, LOD, anisotropic filtering, ...).

### 4. Modules
- [x] UI Toolkit runtime: UIDocument, PanelSettings, UXML/USS, layout, TextCore text engine,
      events, controls, rendering with Unity's shader; `tests/projects/uitk` matches the Editor's
      trees and event steps, images within 0.06/255 (glyph edges: Unity's SDFAA atlas).
- [x] Tilemap (rule and animated tiles), TilemapCollider2D and CompositeCollider2D, SpriteShape,
      2D sprite data access and SpriteSkin deformation; `tests/projects/twod` matches the Editor
      but for 23 of 13297 values (composite outline edge cases, one concave tile's decomposition).
- [ ] AI: NavMesh data, queries, runtime builds, NavMeshAgent and links on Recast/Detour are in
      (`tests/projects/navmesh`); left: Unity's own agent avoidance and its voxelization of runtime
      builds (most of the probe's 1261 differing values), the water path costs.
- [x] Particle System against an Editor probe (`tests/projects/particles`: 93 scenarios of
      particles per step, real frames and 18 images; 1491 of 1559 records and every image but one
      match): stepping (Unity's equal sub-steps, emission timing and phase across loops, prewarm,
      lifetimes), every main and shape module setting tried, velocity/limit/drag/force/orbital,
      color/size/rotation modules, custom data, collisions with planes and the world, triggers,
      force fields, trails, the lights module, the render modes. Left: Unity's noise function,
      force field rotation, ribbon trails, sub-emitter timing in Simulate, Particle System jobs.
- [ ] Terrain, Video, AssetBundles, Cloth, Wind, Vehicles, XR (no device).

### 5. Editor API in Play mode
- [x] Editor services packages call in Play mode, answering as the Editor: Undo (groups,
      object snapshots), Selection, EditorJsonUtility, serialized files and ScriptableSingleton,
      Progress, EditorAnalytics, GlobalObjectId, scene management (EditorSceneManager), Edit mode
      and Play mode transitions, script compilation queries, ShaderUtil (compute/ray tracing
      shaders created from generated sources). `tests/projects/editor` matches the Editor record
      (170/174, the 4 differences are batch mode answers).
- [ ] Editor UI API as no-UI implementations: EditorGUI/Layout, TreeView, SceneView, Handles,
      DragAndDrop handlers, inspectors.
- [ ] Settings assets (`ProjectSettings/*.asset`) as objects with YAML-backed SerializedObjects
      (VFX Graph reads VFXManager.asset this way).
- [x] Test Framework: `unbound test` runs EditMode and PlayMode tests with com.unity.test-framework
      unmodified and writes its NUnit XML; `tests/projects/testrunner` matches the Editor's
      `-runTests` results (logged exception stack traces differ in format).
