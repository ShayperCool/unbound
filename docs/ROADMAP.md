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
| Timeline, Sequences, Cinemachine, Animation Rigging | 320 / 1570 | Playables, PlayableDirector, animation jobs |
| VFX Graph | 160 / 1700 | VisualEffect, compute |
| Post Processing v2 | 100 / 1570 | CommandBuffer, RenderTexture, compute |
| Collections, Burst, Logging, Transport, Serialization | 330 / 3900 | Jobs, AtomicSafetyHandle, UnsafeUtility, Baselib, Properties |
| 2D Animation, SpriteShape, Tilemap Extras | 130 / 1760 | SpriteShape module, sprite data access, jobs |
| AI Navigation | 60 / 140 | NavMesh |
| Notifications, Ads, IAP, LevelPlay | 50 / 900 | AndroidJNI as the Editor answers it |
| Test Framework, UnityEditor.UI | 450 / 3300 | Editor UI API (inspectors), build pipeline |

## Work list

In dependency order; each item ends with an Editor-reference probe.

### 0. Pipeline
- [x] Package compile parity: .NET Standard 2.1 level, CodeGen assemblies, repeated asmdef keys,
      runtime assemblies referencing Editor-only ones.
- [ ] Run IL post-processors (`*.CodeGen`: Burst, Collections, Netcode, Entities) on compiled
      assemblies, as the Editor does after each compile.
- [ ] Resolve registry packages without an Editor-made `Library/PackageCache`: unpack the tarballs
      the Editor ships into the Unbound package store.

### 1. Core runtime
- [ ] Jobs: JobHandle, IJob/IJobFor/IJobParallelFor(Transform), JobsUtility (reflection data,
      scheduling on worker threads), dependencies and Complete.
- [ ] Collections low level: AtomicSafetyHandle (Editor safety checks), UnsafeUtility complete,
      container attributes, Baselib bindings.
- [ ] Burst in the Editor without native compilation: BurstCompiler runs code managed, as with
      Burst compilation off.
- [ ] Profiling: ProfilerRecorder, Recorder, ProfilerMarkerData, ProfilerUnsafeUtility.
- [ ] Playables: PlayableGraph, Playable/ScriptPlayable, outputs, PlayableDirector; animation
      playables (clip, mixer, layer mixer, controller), audio playables, animation jobs
      (AnimationScriptPlayable, stream handles).
- [ ] AndroidJNI, iOS and platform APIs answering as the Editor does on Linux.
- [ ] Unity.Properties (PropertiesModule).

### 2. Input
- [x] Input System native backend (`NativeInputSystem`): the window's keyboard and mouse as the
      Linux Editor reports and feeds them (devices, state and text events, device commands,
      updates in the player loop), active input handling (defines, legacy `Input` refusing),
      InputForUI module, project-wide actions and settings assets, PlayerInput, uGUI through
      `InputSystemUIInputModule`; `tests/projects/input` matches the Editor record for record.
- [ ] Gamepads and joysticks (the Linux Editor's SDL-described devices) and touchscreens: needs
      the window's gamepads in `FrameInput` and an Editor reference recorded with a device.

### 3. Rendering
- [ ] Built-in RP extension points: CommandBuffer (complete), camera and light command buffers,
      Graphics.Blit/DrawMesh*/RenderMesh*, GL, RenderTexture (descriptors, formats, MSAA, depth,
      temporaries), Texture2DArray/3D/Cubemap, AsyncGPUReadback.
- [ ] Compute: ComputeShader compiled from Unity's sources, ComputeBuffer, GraphicsBuffer.
- [ ] Scriptable Render Pipeline core: RenderPipelineAsset/RenderPipeline, ScriptableRenderContext,
      culling results, RendererLists (drawing/filtering settings, ShaderTagId, RenderStateBlock),
      shadows, render passes, keywords, GraphicsSettings for pipelines.
- [ ] URP and URP 2D drawing: their shaders and passes, compared with the Editor's frames.
- [ ] Shader Graph assets (their ScriptedImporter generates the shader).
- [ ] VFX Graph.
- [ ] Baked lighting: LightingData (binary serialized file), lightmaps, light probes, reflection
      probes.

### 4. Modules
- [ ] UI Toolkit runtime: UIDocument, PanelSettings, UXML/USS, layout, TextCore text engine,
      events, rendering.
- [ ] Tilemap, SpriteShape, 2D sprite data access.
- [ ] AI: NavMesh data, NavMeshAgent, queries.
- [ ] Terrain, Video, AssetBundles, Cloth, Wind, Vehicles, XR (no device).

### 5. Editor API in Play mode
- [ ] Editor UI API packages call at load time (EditorGUI/Layout, TreeView, Undo, SceneView,
      Handles) as no-UI implementations.
- [ ] Test Framework: run a project's tests.
