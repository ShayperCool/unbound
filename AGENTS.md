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

## Toolchains

- Rust: `~/.cargo/bin` (add to `PATH`), version pinned in `rust-toolchain.toml`.
- .NET: `~/.dotnet/dotnet` (SDK 10) builds the managed engine; the game code runs on the CoreCLR
  shipped with that SDK.
- Unity installs (reference assemblies only, never executed): `~/Unity/Hub/Editor/<version>/`.
- Test project: `~/unity-projects/game` (Unity 6000.0.71f1, built-in RP, mobile).
