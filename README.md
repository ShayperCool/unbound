<p align="center"><img src="assets/icon.svg" width="160" height="160" alt="Unbound"></p>

# Unbound

Unbound runs Unity projects straight from their sources without the Unity Editor. It reads the
scenes, prefabs, `.meta` files, assets and C# scripts, then compiles and plays the game in its own
engine. You don't need to build a player first. It is an iteration tool: start the game, edit code
or content, and see the change in the running game.

```sh
unbound run -p ~/projects/my-game
```

Under the hood, game code runs on .NET (CoreCLR) against Unbound's own implementation of the
Unity API. Windowing, input, rendering and audio come from [Bevy](https://bevyengine.org), 3D
physics from PhysX 4.1 (the version Unity uses) and 2D physics from a C# port of Box2D. Shaders are compiled
from Unity's own shader sources, so they are not hand-ported. Behaviour is checked against the
Unity Editor with probe projects (`tests/projects/`) that record the Editor's results as
references. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

> **Status: early.** Unbound targets **Unity 6 (6000.0.x)** projects. Core objects and the player
> loop, scenes and prefabs, serialization, uGUI, TextMeshPro, IMGUI, 2D and 3D physics, animation,
> particles, audio, the built-in render pipeline and much of URP already run real games. Many APIs
> are still missing or stubbed, and some content isn't supported yet. `unbound doctor` and
> `unbound api-gaps` show what your project uses that Unbound lacks.
> [docs/COMPATIBILITY_GAPS.md](docs/COMPATIBILITY_GAPS.md) lists the known gaps.

## Feature parity

Each feature is checked against the Unity 6000.0.71f1 Editor by probe projects in
`tests/projects/`. **[x]** means parity: the probe matches the Editor's reference, apart from small
residuals the gap map lists. **[ ] 🚧** means it works in part, and some measured or listed cases
are still open. **[ ]** means it has not started or is only a stub. The open details are in
[docs/COMPATIBILITY_GAPS.md](docs/COMPATIBILITY_GAPS.md) and the work order in
[docs/ROADMAP.md](docs/ROADMAP.md).

- [ ] 🚧 **Pipeline**
  - [x] Script assembly graph and compilation (188 package assemblies match the Editor's)
  - [x] IL post-processors (`*.CodeGen`) in Edit/Play mode
  - [x] Registry packages without `Library/PackageCache`
  - [x] No Unity installation needed (release files from Unity's downloads)
  - [x] Hot reload, source generators, ScriptedImporters
  - [ ] 🚧 Unity API coverage: in 11 Unity 6 test projects, 96-99% of API uses reach implemented
    members; 2647 distinct referenced members are still stubs, mostly Editor tooling (scan of
    2026-11-26). `unbound api-check`: 0 errors in 3567 hand-written types; default state: 17312
    values match the Editor, 320 still open
- [ ] 🚧 **Core runtime**
  - [x] Objects, components, player loop and messages
  - [x] Scenes, prefabs and Unity serialization
  - [x] Jobs, Collections, Burst (runs managed)
  - [x] Profiling API
  - [x] Unity.Properties
  - [x] Android, iOS and platform APIs, answered as the Linux Editor answers them
  - [ ] 🚧 Playables and Timeline (generic animation done; experimental playables left)
  - [ ] 🚧 Destroy timing, batch activation order, `LazyLoadReference`
- [ ] 🚧 **Input**
  - [x] Legacy Input Manager
  - [x] Input System: keyboard, mouse, PlayerInput, uGUI module
  - [x] Gamepads through SDL
  - [ ] 🚧 Joysticks SDL does not map, rumble, IME, pen
- [ ] 🚧 **Rendering: built-in pipeline**
  - [x] Forward lighting: pixel, vertex and SH lights, cookies, shadows
  - [x] CommandBuffer, Graphics.Draw*, GL, RenderTexture, compute
  - [x] Camera depth, normal and motion-vector textures
  - [x] LODGroup (no cross-fades)
  - [x] Projectors, replacement shaders, reflection probes, Deferred and Vertex Lit, halos, flares
  - [x] Baked lighting: lightmaps, light probes, skybox ambient
  - [ ] 🚧 Baking in Unbound (Progressive Lightmapper): filters, denoising, ambient occlusion, soft and
    area lights, cookies, directional maps; some settings are still refused with a notice
  - [ ] 🚧 Shadowmask mixing, mipmap limit, skinned motion vectors, a few shadow edges
  - [ ] 🚧 Precomputed realtime GI (Enlighten), see [docs/REALTIME_GI.md](docs/REALTIME_GI.md)
  - [ ] Mipmap streaming
- [ ] 🚧 **Rendering: scriptable pipelines**
  - [x] SRP core: contexts, culling, renderer lists, render passes
  - [x] URP 17 forward and Forward+, shadows, post-processing, camera stacking
  - [x] URP 2D renderer: 2D lights and shadows
  - [x] Shader Graph
  - [ ] 🚧 BatchRendererGroup / GPU Resident Drawer
  - [x] Adaptive Probe Volumes: bake and lighting compared with the Editor
  - [ ] 🚧 VFX Graph: simulation, all output kinds (quad, mesh, strip, point, line, cube, decal), motion
    vectors, graph files saved as the Editor saves them; a few unmeasured cases left
  - [ ] HDRP
- [ ] 🚧 **Textures and shaders**
  - [x] Shaders compiled from Unity's own sources
  - [x] Texture2D, RenderTexture and bundle mip chains
  - [ ] 🚧 Block-compressed imports (BC, ETC, ASTC, Crunch) with residuals
  - [ ] DDS, KTX and ASTC source files
- [ ] 🚧 **Physics**
  - [x] 3D physics on PhysX 4.1.2: bodies, shapes, queries, events, joints
  - [x] CharacterController
  - [x] WheelCollider
  - [x] Cloth on NvCloth
  - [x] 2D physics (Box2D)
  - [ ] 🚧 Joint projection, 2D contact features, 2D force layers
  - [ ] ArticulationBody, `Physics.BakeMesh`
- [ ] 🚧 **Animation**
  - [x] Legacy Animation and Animator (generic)
  - [x] Blend shapes
  - [x] Animation Rigging, Cinemachine track
  - [ ] 🚧 Humanoid and root motion (measured, residuals open)
- [ ] 🚧 **Particles and effects**
  - [x] Particle System modules, collisions, triggers, trails, lights
  - [x] Wind zones on particles
  - [ ] 🚧 Noise module, ribbon trails, sub-emitter timing, Particle System jobs
- [ ] 🚧 **Navigation**
  - [x] NavMesh data, queries and runtime builds
  - [x] NavMeshAgent and local avoidance
  - [ ] 🚧 Off-mesh link edge cases, obstacle shapes, crowded scenes
- [ ] 🚧 **Terrain and trees**
  - [x] TerrainData, TerrainCollider, terrain shaders, heightmap LOD
  - [x] Details and grass, tree billboards, URP terrains
  - [x] SpeedTree 8 and 9 import and wind
  - [ ] SpeedTree 7 (`.spm`), TerrainCallbacks
- [ ] 🚧 **UI and text**
  - [x] uGUI
  - [x] TextMeshPro
  - [x] IMGUI
  - [x] UI Toolkit runtime: UXML, USS, layout, events, controls
  - [ ] 🚧 UI Toolkit controls still stubbed (EnumField, MinMaxSlider, context menus)
- [ ] 🚧 **Audio and video**
  - [x] AudioSource, mixers, effects, 3D sound, surround output
  - [x] VideoPlayer (through the system's ffmpeg)
  - [ ] 🚧 Video audio output, URL sources, transparency
  - [ ] Tracker modules, microphone recording, spatializer plugins
- [ ] 🚧 **Assets**
  - [x] Model, texture, audio and font import
  - [x] AssetBundles (UnityFS), Addressables in Play mode
  - [x] Sprite atlases, Tilemap, SpriteShape, 2D Animation
  - [x] First-run import pass, session re-imports, importer overrides, forced re-imports, GUID-only
    `.meta` files
  - [ ] 🚧 `Refresh` of some asset kinds, prefab saves after their source changed in the same session
  - [ ] 🚧 `.dae`, `.3ds`, `.max` models (`.blend` imported through Blender, as the Editor does)
- [ ] 🚧 **Networking and services**
  - [x] UnityWebRequest, Localization, XR without a device
  - [ ] 🚧 Netcode for GameObjects: host and client on one machine; Multiplayer Play Mode virtual
    players in progress
  - [ ] Unity Transport TLS
- [ ] 🚧 **Editor API in Play mode**
  - [x] Undo, Selection, EditorSceneManager, compilation queries, ShaderUtil
  - [x] Editor UI API: IMGUI editors and fields, property drawers, TreeView, ReorderableList
  - [x] ProjectSettings assets as the Editor's objects, saved as it saves them
  - [x] Test Framework (`unbound test`)
  - [x] Project files (`.meta`, package lock, scenes, build settings, prefabs, VFX graphs) written as
    the Editor writes them
  - [x] Enter Play Mode options (no domain or scene reload)
  - [ ] 🚧 `ExecuteMenuItem` for built-in menus, `RequestScriptReload`, UI Toolkit inspectors

## Install

Linux x86_64 and macOS (Apple Silicon):

```sh
curl -fsSL https://raw.githubusercontent.com/ShayperCool/unbound/main/install.sh | sh
```

This installs into `~/.unbound` and links `~/.local/bin/unbound`. To pick a release, set
`UNBOUND_VERSION=v0.1.0`. You can also download an archive from
[Releases](https://github.com/ShayperCool/unbound/releases) and unpack it anywhere. The `unbound`
binary in its `bin/` works from any location.

### Requirements

- **.NET 10** runtime or SDK. Unbound looks in `DOTNET_ROOT`, `~/.dotnet` and the system
  locations. If you don't have it:
  `curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --runtime dotnet`
- **A GPU with Vulkan** (Linux) or **Metal** (macOS).
- Linux: Wayland or X11 for a window (`--headless` needs neither). SDL2 is optional (needed for
  gamepads), and libopus is optional (needed for `--remote` sound).
- **Unity's own files** (reference assemblies, built-in resources, built-in packages and shader
  sources) for the project's release. Unbound only reads these files and never runs the Editor. It
  takes them from a Unity Hub installation of that release, or `--unity <path>` /
  `UNBOUND_UNITY`. If neither is available, it downloads the release's Editor archive from
  Unity once (several GB) into `~/.cache/unbound/editors/`. Your use of these files is subject to
  Unity's terms.

On macOS, a release archive downloaded with a browser is quarantined by Gatekeeper. Run
`xattr -dr com.apple.quarantine <unpacked dir>` before the first start, or install with the script
above, which isn't affected.

## Use

```sh
unbound info    -p <project>              # release, profile, packages, assets
unbound run     -p <project>              # compile and play the first build scene, hot-reloading edits
unbound run     -p <project> --scene Assets/Scenes/Level1.unity --size 960x540
unbound run     -p <project> --headless 300 # run 300 frames without a window
unbound compile -p <project>              # compile the script assemblies only
unbound test    -p <project>              # run Unity Test Framework tests, NUnit XML results
unbound doctor  -p <project>              # content Unbound cannot read yet
unbound api-gaps -p <project>             # Unity API the project uses that Unbound lacks
```

`unbound run` behaves as the Editor's Play mode by default (`UNITY_EDITOR` defined). Add
`--player` to compile as a player. `unbound <command> --help` lists every option, including the
scripted input (`--tap`, `--type`, `--press`, ...) and the screenshots used for automated runs.

Unbound writes a project's files where the Unity Editor would: opening a project gives each new
file and folder its `.meta` and writes `Packages/packages-lock.json` when there is none, and what
editor scripts change through the AssetDatabase, importers and settings APIs is saved as the
Editor saves it. What Unbound makes for itself (caches, the view of the project the game runs
in, logs, test results) goes to `<project>/LibraryUnbound/` and `~/.cache/unbound/`
(`$XDG_CACHE_HOME/unbound`); `Library/` is Unity's and Unbound keeps nothing there (a
`Library/Unbound` left by an earlier version is removed when the project is opened, its logs,
test results and editor user settings moved over). `--project-data <dir>` or
`UNBOUND_PROJECT_DATA` puts that directory elsewhere: relative to the project, or absolute and
possibly outside it; not in `Assets/`, `Packages/`, `ProjectSettings/` or `Library/`. In the
project it holds a `.gitignore` of its own, so it stays out of the project's version control
without a change to the project's files. PlayerPrefs
and `Application.persistentDataPath` are the Editor's own unless you pass `--player-prefs` or
`--persistent-data-path`.

## Build from source

You need Rust (the toolchain in `rust-toolchain.toml`, installed by rustup), the .NET 10 SDK, a
C/C++ compiler, CMake and Python 3. On Linux you also need the Wayland/xkbcommon development
headers.

```sh
cargo build --release --locked -p unbound   # also builds the managed engine into target/managed
target/release/unbound run -p <project>
```

Contributor tooling (shared build cache, probe runners, the landing gate) is described in
[AGENTS.md](AGENTS.md) and [tools/validation/README.md](tools/validation/README.md). Release
archives are built by [.github/workflows/release.yml](.github/workflows/release.yml) from a `v*`
tag.

## License

Unbound is licensed under the [MIT License](LICENSE). Third-party code in the repository keeps its
own license:

- vendored native libraries in `native/`: PhysX (BSD-3-Clause), NvCloth (NVIDIA Source Code
  License), libvorbis/libogg (BSD-3-Clause), libtess2 and the GLU tessellator (SGI Free Software
  License B);
- the patched wgpu in `vendor/` (MIT/Apache-2.0);
- ports in `managed/`: Box2D and Box2DSharp (MIT), DotRecast (zlib), Yoga (MIT), Clipper
  (Boost Software License).

The license files sit next to that code. Release archives include `THIRD_PARTY_NOTICES.md` with
the licenses of everything they ship.

Unbound is not affiliated with or endorsed by Unity Technologies. Unity is a trademark of Unity
Technologies.
