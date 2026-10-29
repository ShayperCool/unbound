# AGENTS.md

Unbound is a Unity-API-compatible engine built on Bevy (Rust) with game code running on CoreCLR.
It runs Unity projects straight from their sources (scenes, prefabs, `.meta`, C# code) without the
Unity Editor and without building a player. See `docs/ARCHITECTURE.md` for the design.

## Commits

- All work in progress is committed as it goes, in small steps, with
  [Conventional Commits](https://www.conventionalcommits.org/) messages:
  `type(scope): summary`, where type is one of `feat`, `fix`, `perf`, `refactor`, `docs`, `test`,
  `build`, `ci`, `chore`. Scope is the module (`core`, `scene`, `compiler`, `scripting`, `render`,
  `physics`, `audio`, `assets`, `cache`, `compat`, `cli`, `managed`, ...).
- One logical change per commit; don't leave work uncommitted at the end of a step.

## Rules of the codebase

- Never write to a Unity project's authored files (`Assets/`, `Packages/`, `ProjectSettings/`) except
  through explicit, Unity-compatible edits. Anything Unbound produces goes to
  `<project>/Library/Unbound/` (per-project cache) or the global cache (`$XDG_CACHE_HOME/unbound`).
- Every behaviour that differs between Unity releases goes through the compatibility profile
  (`compat` crate / `Unbound.Compat` on the managed side). Never hard-code a single release's behaviour
  where releases differ.
- The cache is ours: it does not need to be Unity-compatible, but every entry is keyed by a content
  hash of its inputs plus a format version, so a stale entry can never be used.
- Unsupported content fails loudly with a clear message (and is listed by `unbound doctor`) instead
  of being silently approximated where that would change game behaviour.

## Code search

- The repo has a semantic index (`ccc`, see the `ccc` skill). Use `ccc search <what it does>` to find
  where a concept, behaviour or mechanism lives ("where is the tire slip computed", "how canvas masks
  are drawn"); use `rg` for exact identifiers, strings and file names.
- The index does not update itself: run `ccc search --refresh ...` (or `ccc index`) on the first
  search of a session and after significant changes; consecutive searches with no code changed in
  between don't need it.
- Narrow results with `--path`: our code is `crates/**`, `managed/**`, `native/unbound_*`; vendored
  sources are `native/physx/**`, `native/nvcloth/**`, `native/vorbis/**`; probe projects are
  `tests/projects/**`. `managed/UnityEngine/Generated/` (generated API stubs) is not indexed.

## Toolchains

- Rust: `~/.cargo/bin` (add to `PATH`), version pinned in `rust-toolchain.toml`.
- .NET: `~/.dotnet/dotnet` (SDK 10; else `DOTNET_ROOT` or the system install, e.g.
  `/usr/local/share/dotnet` on macOS) builds the managed engine; the game code runs on the CoreCLR
  shipped with that SDK.
- Unity's files (reference assemblies, resources, built-in packages; never executed) come from the
  Hub installation of the profile's release (`~/Unity/Hub/Editor/<version>/Editor` on Linux,
  `/Applications/Unity/Hub/Editor/<version>/Unity.app` on macOS), else from its downloads, unpacked
  once into `<cache>/editors/` (`UNBOUND_UNITY`/`--unity` overrides, `UNBOUND_OFFLINE` forbids
  downloading, `UNBOUND_EDITOR_MODULES` picks the platform modules, default `android,ios`).
- Platforms: Linux x86_64 and macOS arm64. Behaviour measured against the Linux Editor (graphics
  caps, input devices) stays as measured on macOS; what the OS decides (platform, paths, prefs,
  native plugins) follows the macOS Editor there. `--remote` streaming needs NVENC (an NVIDIA GPU on
  Linux).
- Test project: `~/unity-projects/game` (Unity 6000.0.71f1, built-in RP, mobile).
