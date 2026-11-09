<img src="assets/icon.svg" width="64" height="64" alt="">

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

Unbound never writes to a project's `Assets/`, `Packages/` or `ProjectSettings/`. Its caches go
to `<project>/Library/Unbound/` and `~/.cache/unbound/` (`$XDG_CACHE_HOME/unbound`). PlayerPrefs
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

Created in [T3 Code](https://t3.codes).
