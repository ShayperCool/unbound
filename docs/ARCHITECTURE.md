# Unbound architecture

Unbound runs a Unity project from its sources — scenes, prefabs, `.meta` files, assets and C# —
without the Unity Editor and without building a player. It is an iteration tool: start the game,
edit code or content, see the change in the running game. Everything it writes into a project is in
Unity's own formats; everything else is its private cache.

## Goals

1. **API compatibility.** Game code and precompiled plugins compiled for Unity run unchanged.
2. **Per-release behaviour.** Where Unity releases differ (APIs, defines, serialization, behaviour),
   the selected *compatibility profile* decides; a project's `ProjectVersion.txt` selects the profile.
3. **Fast iteration.** Warm incremental compilation, in-process hot reload, and a content-addressed
   cache so a second start does almost no work.
4. **No Editor.** A Unity installation is used only as a source of reference assemblies and built-in
   packages (never executed). Bevy supplies windowing, input, rendering and audio.

## Process model

One process, `unbound run`:

```
 Rust (main thread)                                 CoreCLR (hosted via hostfxr)
 ───────────────────                                ──────────────────────────────
 unbound CLI ── project scan, packages, graph        Unbound.Compiler (own ALC, Roslyn, warm)
          ── asset import + cache                   UnityEngine.CoreModule (our implementation)
          ── file watcher                           game assemblies (game ALC, reloadable)
 Bevy App ── window/input/render/audio   ⇄ interop ⇄ PlayerLoop, scene graph, serialization
```

* **Rust owns the platform**: the Bevy app (window, input, render, audio), asset decoding (textures,
  meshes, audio), Unity YAML parsing, the project model (GUIDs, packages, assembly graph), the cache
  and file watching.
* **C# owns the Unity object model**: `Object`, `GameObject`, `Transform`, components, the
  PlayerLoop and its lifecycle messages, Unity serialization of `MonoBehaviour`/`ScriptableObject`
  fields, `Resources`, `SceneManager`, coroutines. Keeping these in C# makes Unity's managed
  semantics (fake null, destroy timing, message order, serialization rules) straightforward to
  reproduce exactly, with no interop on hot paths such as `transform.position`.
* **Native-backed components** (renderers, camera, light, physics bodies, audio sources, canvas
  renderers, ...) are C# objects holding their serialized state; they register a handle with the
  Rust side and push changes once per frame through batched interop buffers. Rust mirrors them as
  Bevy entities.

## Compiling game code

Modelled on what the Unity Editor itself records in `Library/Bee/*-inputdata.json`:

* **Assembly graph** (`unbound-project::assemblies`): `.asmdef`/`.asmref`, the predefined
  `Assembly-CSharp[-firstpass][-Editor]` assemblies, `Editor` folders, `defineConstraints`,
  `versionDefines`, include/exclude platforms, `autoReferenced`, `overrideReferences`,
  `noEngineReferences`, precompiled plugin DLLs and their `.meta` platform settings.
* **Defines** come from the compatibility profile (per-release tables captured from real Editor
  compiles) plus the project's scripting define symbols for the active build target.
* **References**: game code is compiled against the *real* Unity reference assemblies of the
  selected release (`Editor/Data/Managed/UnityEngine/*.dll`, `UnityReferenceAssemblies/unity-4.8-api`),
  exactly as Unity compiles it. So the code compiles under the same API surface as in Unity, and
  every API gap of Unbound shows up at runtime as a precise, logged missing member instead of a
  compile error that would block the whole project.
* **Compiler**: Roslyn as a library, in-process (`Unbound.Compiler`), kept warm. Each assembly keeps
  its parsed syntax trees and its `Compilation`; an edit re-parses the changed file and re-emits that
  assembly. Dependents are re-emitted only when the public surface changed. Independent assemblies
  compile in parallel.
* **Cache**: each emitted assembly is keyed by a hash of its sources, defines, options and the
  hashes of its references; a fresh process reuses unchanged outputs without compiling.

## Running game code

* Game code runs on CoreCLR. Assemblies built against Unity's .NET Framework 4.8 reference profile
  bind through the runtime's own `mscorlib`/`System`/`netstandard` facades.
* `UnityEngine.CoreModule` is Unbound's implementation of the engine API (one assembly). For every
  other Unity module (`UnityEngine.PhysicsModule`, ..., and the monolithic `UnityEngine.dll` that old
  plugins reference) a **facade** is generated from that release's reference assembly: a forwarder of
  each type to our implementation (`Unbound.ApiTools.FacadeGenerator`, `unbound facades`). Facades
  depend on the release alone and are cached per release in `<cache>/facades/<unity-version>/`.
* Members our implementation lacks: compatibility stubs are generated per release from the reference
  assemblies (the full API surface), logging their first use; real implementations replace them over
  time. `unbound gen-stubs -p <project>` builds `managed/UnityEngine` without stubs
  (`-p:UnboundNoStubs=true`), compares it with the release's modules (`Unbound.ApiTools.StubGenerator`)
  and writes `managed/UnityEngine/Generated/<family>/<Module>.g.cs`: new types in full, `partial`
  parts adding the missing members to hand-written types (which must therefore be `partial`).
  Generated code is marked `[Unbound.Stub]`; bodies call `Unbound.Runtime.Stubs.NotImplemented`
  and return defaults. Implementing a member by hand and regenerating drops its stub.
  `unbound api-check -p <project>` (CI: exits non-zero) checks the hand-written code against the
  release: a member with Unity's name but another signature suppresses the stub and fails game code
  with MissingMethodException, so signatures, type placement (namespace, nesting), enum values,
  accessibility and public struct field layout must match Unity's exactly.
  `unbound api-gaps` lists the types and members a project's assemblies (and the plugins they
  reference) use that the implementation lacks or only stubs, by call sites, with a coverage line.
* **Hot reload**: game assemblies are loaded with modifiable-assembly support and edits are applied
  as metadata/IL deltas (`MetadataUpdater.ApplyUpdate`, the runtime's Edit-and-Continue), produced by
  Roslyn's `EmitDifference`. Edits the runtime cannot apply fall back to a *domain reload* in the
  style of Unity's "Recompile and continue playing": serializable state is captured, assemblies are
  loaded into a fresh load context, and the scene state is restored.
* **Editor Play mode**: the editor assemblies are compiled (with Unity's source generators and the
  Editor's exact class-library reference set) and loaded, as in the Editor. Assets whose `.meta`
  names a `ScriptedImporter` are imported by running it. Mono-specific behaviour game code relies on
  is emulated: assemblies that request `SkipVerification` get `IgnoresAccessChecksTo` for their
  references (Mono lets them use engine internals); .NET Framework assemblies .NET only has as thin
  facades (`System.Web`) come from the release's Mono; and the game runs in
  `Library/Unbound/ProjectView`, a directory of links where `Packages/<name>/...` resolves for plain
  file IO, as Mono's path remapping makes it in the Editor.
* **Tracing** (`UNBOUND_TRACE=<channels>` or `all`), to find where a game stalls or misbehaves:
  `lifecycle` (Awake/Start), `scenes` (loads and unloads), `render` / `render-detail` (cameras,
  canvases and draws per frame, and what the renderer skips), `exceptions` (every exception as it
  is thrown, also the ones plugins swallow), `perf` (time per script callback and native call,
  reported at quit), `input` (each press and what the EventSystem's raycasters hit), `hierarchy`
  (the live scene graph at quit, in `Library/Unbound/hierarchy.txt`).
* **Scripted input**: `unbound run --tap FRAME:X,Y` taps at a frame (window pixels from the top
  left), headless or in a window with `--screenshot`; playthroughs can be checked without a person.
  The window opens as the build target's screen in the project's orientation (a landscape phone
  game at 960x540), unless `--size` says otherwise.
* **Physics**: 2D on a managed port of Box2D v2.4 (`managed/UnityEngine/Physics2D/Box2D`), 3D on
  [Box3D](https://github.com/erincatto/box3d) (`native/box3d`, compiled by `unbound-host`'s build
  script with the flat C shim `native/unbound_box3d.c` into the `unbound_box3d` library next to the
  managed engine). `Physics2DWorld` and `PhysicsWorld` mirror the components into their worlds
  the way Unity mirrors them into Box2D and PhysX: a body per rigidbody, shapes per collider in the
  body's frame, layers as collision filters, transforms synced before steps and queries, poses
  written back after, and Unity's message rules.
* **Editor Play mode emulation** also covers: the Editor's enter-Play-mode sequence
  (`[InitializeOnLoad]`, `[InitializeOnLoadMethod]`, `playModeStateChanged`,
  `[InitializeOnEnterPlayMode]`), finalizer exceptions logged instead of ending the process,
  delegates passed to native code kept alive, .NET Framework-only reflection-emit calls redirected
  to shims, and game assemblies loaded from files so `Assembly.Location` names one.
* **Tests**: `UNBOUND_INTEGRATION=1 cargo test -p unbound --test fixture` runs the fixture project
  `tests/projects/hello` end to end (a run, and a code edit patched into the running game).

## Content

* **Unity YAML** (`unbound-yaml`): a fast reader of Unity's text serialization (documents keyed by
  `fileID`, `!u!<classID>` tags, `stripped` prefab instances).
* **Asset database** (`unbound-project::assetdb`): every `.meta` in `Assets/` and the resolved
  packages, `guid → path` and importer settings. Scenes and prefabs reference by `{fileID, guid}`.
* **Importers** (`unbound-assets`): textures (with sprite import settings), meshes (FBX via ufbx, OBJ),
  audio, fonts. Outputs are cached by hash of the source file, its `.meta` and the importer version.
* **Text**: fonts are rasterized natively (`unbound-assets::font`, FreeType's unhinted metrics) and
  laid out in C# (`Font`, `TextGenerator`: Unity's legacy generator for UI Text and TextMesh), with
  each dynamic font's glyphs packed into its own Alpha8 texture. The built-in `LegacyRuntime.ttf`
  lives in an installed player's `unity default resources` (the Editor's copy has no font data);
  it is extracted into the global cache on first use.
* **Prefabs** are instantiated with Unity's semantics: nested prefabs, variants, property
  modifications, added/removed components and game objects.

## Compatibility profiles

`unbound-compat` (Rust) and `Unbound.Compat` (C#) expose one `Profile` per supported release family:
version, define tables, serialization versions, and named *behaviour switches* (for example
`Rigidbody` velocity naming, `FindObjectsByType` availability, default `Physics` settings). Code
asks the profile (`profile.has(Feature::X)`) instead of comparing versions. A project is matched to
the newest profile not newer than its release; an unknown newer release uses the latest profile
with a warning.

## Layout

```
crates/
  unbound-compat     Unity versions, compatibility profiles, define tables
  unbound-yaml       Unity YAML reader
  unbound-project    project model: settings, asset database, packages, assembly graph
  unbound-cache      content-addressed cache
  unbound-assets     importers (textures, meshes, audio)
  unbound-host       CoreCLR hosting and the native interop table
  unbound-runtime    the running game: native API, frame loop, render protocol
  unbound-app        the windowed player: Bevy app, input, renderer of the render protocol
  unbound            the CLI
native/
  box3d             Box3D, vendored unmodified (see UNBOUND.md there)
  unbound_box3d.c    the flat C API the managed 3D physics calls
managed/
  UnityEngine       UnityEngine.CoreModule: the engine API implementation and player loop
  Unbound.Compiler   Roslyn compiler service
  Unbound.ApiTools   Unity API metadata tools: module facades, the API gap report
data/
  profiles/         per-release tables (defines, module lists)
```
