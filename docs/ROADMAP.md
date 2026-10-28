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
  serialization, uGUI, TextMeshPro, IMGUI, 2D physics (Box2D), 3D physics (PhysX 4.1), legacy and
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
- [x] Resolve registry packages without an Editor-made `Library/PackageCache`: the tarballs the
      installation ships unpack into the Unbound package store (with or without a lock file);
      packages it does not ship stay missing (no download).

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
- [x] Gamepads and joysticks as the Linux Editor reads them: through SDL (the system's SDL2 with
      the Editor's own controller database), pads as Input System `Gamepad`s of the "SDL"
      interface (descriptions, `GPAD` state, hotplug), the legacy Input Manager's joystick names,
      buttons and axes; touches through the Input System's touch simulation (the Editor reads no
      touchscreen). `tests/projects/gamepad` plugs a virtual pad in through uinput and matches the
      Editor record for record (349/349) but for the Editor's SDL holding back a trigger's first
      move. Left: joysticks SDL does not map (legacy Input reads them; the Input System's SDL
      joystick layouts are not fed, a warning says so), hats as legacy axes, rumble.

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
- [x] LODGroup: the level each camera draws by the group's screen-relative height (LOD bias,
      maximumLODLevel, ForceLOD, orthographic cameras), shadows cast by the drawn level, read from
      scenes and prefabs; terrain trees of LOD prefabs (`tests/projects/lod`: transitions within a
      sample of the Editor's). Not planned: cross-fades (levels switch at once).
- [x] Left of the built-in RP: several colors at once (render buffers, RenderTargetSetup, command
      buffer arrays), RenderBuffer targets, CubemapArray, GPU instances and indirect draws (procedural
      and mesh, Now and command buffer forms); `tests/projects/graphics` matches the Editor (49/49).
      Left: GraphicsTexture overloads, indirect draws with an index buffer of their own. (Light
      command buffers run around the built-in shadows: the shadow map, its passes and the
      screen-space mask.)
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
- [x] Directional shadow cascades with conservative enclosing spheres (URP's asset asks for them):
      each cascade's own slice out to the shadow distance, culled from the previous split;
      `shadows` and `lights_shadows` now match the Editor (1 pixel over 8/255). Left: the legacy
      spheres' slice depth is a fit (the built-in pipeline's, within 1%), and the Editor's
      iterative sphere is within 0.3% of the exact one Unbound takes. (Multiple render targets are
      in; URP's deferred path stays off as on the Editor's OpenGL device.)
- [x] Shader Graph assets: their ScriptedImporter runs unmodified and the ShaderLab it generates
      (`Library/Unbound/GeneratedShaders`) is compiled like any shader; `tests/projects/shadergraph`
      matches the Editor's image (mean difference 0.02/255).
- [ ] ~~VFX Graph~~: not planned for now (the user's call, 2026-10-27): its runtime is the engine's
      native VFXModule, a large piece; projects that use it need only the Particle System to work.
      VisualEffect objects are skipped with a warning.
- [x] Baked lighting: a scene's LightingData (read through its type tree): lightmaps and each
      renderer's lightmap placement, light probes (tetrahedral interpolation), baked lights out of
      the realtime ones, the skybox reflection; cubemap textures from images (strips, crosses,
      panoramas, glossy convolution). `tests/projects/baked` matches the Editor (mean difference
      0.64/255). Directional lightmaps and shadowmasks are read but untested; additive scenes'
      probes are not merged.
- [x] Skybox ambient without baked lighting data: a scene starts with the built-in lighting
      data's probe and reflection (the default sky's, as the Editor); DynamicGI.UpdateEnvironment
      projects the current sky (its shader, the scene's sun) onto the probe as Unity stores it
      (`tests/projects/ambient`: a six-colour cubemap sky within 3e-5, procedural skies within
      0.6 %); the reflection stays, as in the Editor.
- [x] QualitySettings levels from the project (shadows, AA, LOD, anisotropic filtering, ...).

### 4. Modules
- [x] UI Toolkit runtime: UIDocument, PanelSettings, UXML/USS, layout, TextCore text engine,
      events, controls, rendering with Unity's shader; `tests/projects/uitk` matches the Editor's
      trees and event steps, images within 0.06/255 (glyph edges: Unity's SDFAA atlas).
- [x] Tilemap (rule and animated tiles), TilemapCollider2D and CompositeCollider2D, SpriteShape,
      2D sprite data access and SpriteSkin deformation; `tests/projects/twod` matches the Editor
      but for 23 of 13297 values (composite outline edge cases, one concave tile's decomposition).
- [x] AI: NavMesh data, queries, runtime builds, NavMeshAgent and links on Recast/Detour
      (`tests/projects/navmesh`). Queries match the Editor but for edge ties (2 samples, a raycast
      normal at a vertex, one path round a cost-20 area); paths weigh area costs as Unity's do (A*
      nodes at the portal point nearest their parent, ranked by cost). Runtime builds give the
      Editor's polygons (unmerged monotone regions, Recast's 1.3-voxel contour error, ledges past
      the climb) but at one obstacle corner (13 of 211 values). Agents: Unity's avoidance, measured
      in `tests/projects/avoidance` (12 sets, 95% of 12618 records; braking and walls 100%):
      Detour's adaptive sampling with Unity's bias, offset and weights, the prediction time, the
      kept desired velocity, priorities, separation, overlap resolution, NavMesh edges up to the
      next corner, braking (v²/2d once the end is within two radii at speed), the update order.
      Left: near-ties between sides once agents move, grazing paths at exactly the radii,
      separation from a neighbour behind in a path's first frames, the height Unity keeps or drops
      in a desired velocity on slopes; the probe's six agents interact, so these carry on.
- [x] Particle System against an Editor probe (`tests/projects/particles`: 93 scenarios of
      particles per step, real frames and 18 images; 1491 of 1559 records and every image but one
      match): stepping (Unity's equal sub-steps, emission timing and phase across loops, prewarm,
      lifetimes), every main and shape module setting tried, velocity/limit/drag/force/orbital,
      color/size/rotation modules, custom data, collisions with planes and the world, triggers,
      force fields, trails, the lights module, the render modes. Left: Unity's noise function,
      force field rotation, ribbon trails, sub-emitter timing in Simulate, Particle System jobs.
- [ ] Terrain (leftovers deferred, the user's call 2026-10-27): TerrainData from the Editor's asset with its height, normal and steepness queries,
      the TerrainCollider (PhysX height field, holes, tree capsules), drawing with Unity's terrain
      shaders and trees (`tests/projects/terrain`: 202/216 records, images within 2.6/255);
      details: the detail database, where instances go as ComputeDetailInstanceTransforms (instance
      counts exact; coverage exact for samples three quarters of a footprint across and more),
      drawn with Unity's detail shaders as the Editor builds the meshes (`tests/projects/grass`);
      layerless terrains' checker. Left: coverage mode's rule for narrower samples and low
      coverage, ground-aligned meshes' turns, the base map past basemapDistance, heightmap LOD by
      pixel error, tree billboards, detailObjectDensity.
- [x] AssetBundles the Editor builds (UnityFS: uncompressed, LZ4, LZMA): LoadFromFile, Memory,
      Stream and their async forms, LoadAsset (by path or name, sub-assets), LoadAllAssets, Unload,
      the manifest, scenes through SceneManager; player data read by its type trees (references
      across bundles resolved when an asset loads, scripts by their MonoScripts, built shaders by
      name, block-compressed textures decoded, `.resS` streams, built scenes' ambient probe and
      reflection), meshes in Unity's serialization (bundles and `.asset` meshes);
      `tests/projects/assetbundles` matches the Editor. Clips in player form: muscle clips and
      compiled AnimatorControllers (`tests/projects/bundleanim`), AudioClips' FSB5 banks — PCM16,
      FMOD's IMA ADPCM, Vorbis with its setup header made again by vendored libvorbis 1.3.7
      (`tests/projects/bundleaudio`). Not supported (details a player build needs, not the Editor;
      maybe later): Vorbis below 32 kHz (FMOD's own setup headers), other FSB5 codecs (MP3 for iOS;
      the consoles' are not planned), Mesh Compression, CRC checks.
- [x] Blend shapes: the Mesh API, SkinnedMeshRenderer weights (blended before skinning, in
      BakeMesh too), serialized meshes' and models' shapes (normals calculated as the Editor does,
      MikkTSpace tangent deltas; imported models' "Calculate" tangents are MikkTSpace now), initial
      weights from DeformPercent, `blendShape.*` clip curves (`tests/projects/blendshapes`: all
      records match). Left: GetBlendShapeBuffer, `legacyClampBlendShapeWeights`, GPU skinning.
- [x] Video: VideoClip import (properties from ffprobe) and the VideoPlayer decoding through the
      machine's ffmpeg, colours from Unity's VideoDecode shader, into a RenderTexture, a material
      property or a camera's far or near plane (built-in RP): prepare, play, pause, seek, step,
      loop, events and states as the Editor (`tests/projects/video`: every record and frame
      matches, camera planes within 0.6/255). Left: camera plane modes under a render pipeline
      asset, audio output, URL sources over the network, transparency (VP8 alpha).
- [x] XR without a device: XRSettings, XRDevice, XRStats, input devices and tracking, usages and
      subsystems answer as the Editor with no XR provider (`tests/projects/xr`: all records match).
- [x] Wind zones: WindZone as the Editor has it and its wind on particles (External Forces):
      directional and spherical zones, the pulse, the influence filter (`tests/projects/wind`: all
      records match but two pulsing scenarios that depend on the frame times). Left: wind on trees
      (the Nature shaders' `_Wind`, SpeedTree wind).
- [x] Vehicles: WheelColliders on PhysX's own vehicle SDK (PxVehicleNoDrive) with Unity's tire
      model and set-up, the input-clearing quirk (`tests/projects/vehicles`: 36/53 records match;
      9 chaotic ones end within millimeters, 8 small known differences).
- [x] 3D physics on PhysX 4.1.2 (the release Unity runs; vendored, two small patches) instead of
      Box3D: the scene set up as Unity's, bodies and shapes in Unity's order, materials, filtering,
      events with PhysX's impulses, queries, ComputePenetration, the terrain height field; joints
      (hinge, fixed, spring, character, configurable) as Unity builds them (`tests/projects/joints`:
      26/27 records match); CharacterController on PhysX's character controller
      (`tests/projects/controller`: all records match). Physics probe: every 3D scenario but the
      resting stack matches. Left: joint projection, configuredInWorldSpace/swapBodies, the
      stack's resting jitter.
- [x] Cloth on NvCloth 1.1.6 (the solver Unity runs; vendored, unmodified), set up as Unity sets
      it up: welded particles, geodesic tethers, stiffness phases, inertia, max distance and
      collision sphere distance constraints, virtual particles, sphere, cone and capsule colliders,
      self collision, fading, teleports, bone-skinned cloths in their root bone's frame
      (`tests/projects/cloth`: 37/40 records match, most exactly; 3 that swing far end within
      7 cm). Left: inter-collision between cloths (wired, unmeasured), random acceleration's
      distribution, the editor's constraint painting.

### 5. Editor API in Play mode
- [x] Editor services packages call in Play mode, answering as the Editor: Undo (groups,
      object snapshots), Selection, EditorJsonUtility, serialized files and ScriptableSingleton,
      Progress, EditorAnalytics, GlobalObjectId, scene management (EditorSceneManager), Edit mode
      and Play mode transitions, script compilation queries, ShaderUtil (compute/ray tracing
      shaders created from generated sources). `tests/projects/editor` matches the Editor record
      (170/174, the 4 differences are batch mode answers).
- [x] Editor UI API without editor windows, as the batch-mode Editor runs it: IMGUI editor state,
      metrics, content and the Editor's icons, property heights (drawers, decorators, attributes,
      reorderable lists), editors and inspectors, generic menus and dialogs, TreeView and
      MultiColumnHeader models, drag and drop, handles and scene views, curve bindings and
      AnimationUtility; drawing outside OnGUI throws as the Editor's does
      (`tests/projects/editorui`: 242/243 calls match, 1 expected). Left: the editor controls'
      exact look inside a game's OnGUI (they draw as plain IMGUI controls), UI Toolkit inspectors.
- [x] Settings assets (`ProjectSettings/*.asset`) as the Editor's objects (AssetDatabase,
      `Unsupported.GetSerializedAssetInterfaceSingleton`) whose SerializedObjects iterate the
      classes' native type trees (dumped from the Editor) over the files' YAML; applied edits take
      effect at once (input axes, tags, layers, product name, physics, time), SaveAssets writes them
      as the Editor does, in Edit mode only (`tests/projects/settings`: all 4548 properties match,
      saved files byte for byte).
- [x] Test Framework: `unbound test` runs EditMode and PlayMode tests with com.unity.test-framework
      unmodified and writes its NUnit XML; `tests/projects/testrunner` matches the Editor's
      `-runTests` results (logged exception stack traces differ in format).
