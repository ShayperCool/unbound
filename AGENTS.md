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
- Commit messages do not name test games.

## Probe references and run artifacts

- Commit probe sources, runners/comparators/verifiers, and one canonical Unity Editor
  reference per release under `tests/projects/<probe>/Reference/unity-<version>`
  (a version-named file for single captures, a directory for multi-file/profile sets).
- Independent Native repeats are checked before publishing one reference. Keep both
  repeat captures outside Git. Do not keep Before/Final/Followup copies of the same oracle.
- All engine outputs, candidates, comparisons, gates, manifests and logs go to the
  project's `Library/Unbound/` or `$XDG_CACHE_HOME/unbound` (`~/.cache/unbound` by default).
  Never add `Evidence*` or `*Evidence` run directories to Git.
- Probe scripts select the release explicitly, read Native oracles from `Reference/`,
  and work in clean checkouts without machine-specific archived artifacts. Reports
  must keep every missing/extra/failing value and preserve comparator tolerances.
- Work reaches `main` only through `tools/agents/land.py` (rebase, gate, fast-forward).
  Never rewrite `main`'s history or force-push.

## Rules of the codebase

- When adding or changing a Unity API type or member, obtain its declaration from the selected
  release's assemblies through `Unbound.ApiTools.StubGenerator`: signature, attributes and
  `AttributeUsage`, optional parameter defaults, constants, enum values and modifiers. Take default
  state from that release, with an Editor probe where runtime initialization determines the value.
  Keep `unbound api-check` and the default-state probe clean. Each exception needs an exact
  release-specific allow-list entry with a nonempty reason; queued defaults retain their raw
  discrepancies and counts until their module is fixed.
- Never write to a Unity project's authored files (`Assets/`, `Packages/`, `ProjectSettings/`) except
  through explicit, Unity-compatible edits. Anything Unbound produces goes to
  `<project>/Library/Unbound/` (per-project cache) or the global cache (`$XDG_CACHE_HOME/unbound`).
- Every behaviour that differs between Unity releases goes through the compatibility profile
  (`compat` crate / `Unbound.Compat` on the managed side). Never hard-code a single release's behaviour
  where releases differ. Releases are grouped by family, `MAJOR.MINOR`: every release of a family
  (6000.0.0f1 ... 6000.0.71f1) is one version and gets the answers measured on the family's measured
  release (6000.0.71f1 for 6000.0); another family (6000.1, 6000.3) is a different version, with its
  own measured answers or else the unmeasured behaviour. Gates ask the family
  (`unbound_compat::MeasuredFamily`, `Compat.Family`), never a patch. What an installation ships
  (its engine module assemblies) is a fact of that installation, read from it.
  Managed tests run under the measured release (6000.0.71f1, `UnityEngine.Tests/TestRelease.cs`) like
  the engine; a test of another family's path pins it (`TestRelease.Pin`, `TestRelease.Unmeasured`),
  and `UNBOUND_TEST_UNITY_RELEASE=6000.3.0f1` runs the whole suite under a release without measured
  answers.
- The cache is ours: it does not need to be Unity-compatible, but every entry is keyed by a content
  hash of its inputs plus a format version, so a stale entry can never be used.
- Unsupported content fails loudly with a clear message (and is listed by `unbound doctor`) instead
  of being silently approximated where that would change game behaviour.
- Vendored PhysX changes are allowed. Record each patch immediately in
  `native/physx/UNBOUND_PATCHES.md`: file/function, change, Unity behavior that requires
  it, and the confirming runnable probe or canonical reference. Distinguish measured
  behavior from internal-mechanism inference.

## Unity contracts and parity decisions

- Apply these rules across all engine features, including rendering, assets, physics,
  particles, animation, UI and audio, not only lifecycle callbacks.
- Before changing Unity API behavior, lifecycle timing, callback order or other observable
  semantics, check the official Manual and Scripting API for the project's Unity release.
  Read the relevant limitations as well as the main description. Record the version, links
  and applicable guarantee in the cause table or review.
- Distinguish a documented guarantee, behavior observed in a specific reference capture,
  and an unresolved implementation hypothesis. A repeatable observation does not establish
  a universal Unity contract.
- When Unity leaves an order or behavior unspecified, do not promise a stronger guarantee,
  invent a global sort or phase rule, or add normative tests merely to reproduce one fixture.
  Preserve documented guarantees, including configurable script execution order. A normal
  deterministic implementation is not a defect just because Unity leaves its order unspecified.
- Probe an undocumented detail only to resolve a concrete compatibility question. Keep the
  measured release/context and unmeasured cases explicit. Existing raw failures and callback
  sequences stay visible; do not change an oracle, comparator, tolerance or ordering to make
  an unspecified difference disappear. Review observable game effects before changing code.
- Crash-class defects (an exception, a crash, an object or component Unbound fails to create, an
  API member that throws where Unity works) need no Editor probe when Unity's behaviour is
  unambiguous (documented for the release, or plainly no throw and no log in the Editor): the fix,
  a test that fails before it and passes after it, and a rerun of the project where it was seen
  (a debug check, not named in Git) are the evidence. Probe when
  the resulting values, objects, order, callbacks or log lines, or the object's creation, are in
  doubt; value, visual, timing and order differences always need an Editor reference.

## Multi-agent work

The full workflow is in [tools/agents/WORKFLOW.md](tools/agents/WORKFLOW.md). In short:

- An orchestrator keeps about 5 workers busy, one per subsystem, each in its own persistent slot
  worktree (`tools/agents/slot.py`) on a fresh branch from `main`, so builds stay incremental.
  It writes no engine code and does not re-check workers.
- A worker owns its task end to end. It probes against the Unity Editor, implements, builds,
  tests and runs the engine and the Editor as often as it needs (no budgets; the job pool bounds
  the machine). It gets one independent reviewer subagent and fixes P1/P2 findings itself, then
  lands with `tools/agents/land.py` and reports in at most 10 lines.
- `land.py` queues branches and serializes their integration with a lock. The holder rebases the
  queued branches in order and runs one combined release build, managed tests, Rust tests of the
  union of touched crates and all requested probes, with an oracle guard per branch. Passing
  branches fast-forward `main` in order. A conflict rejects only its branch; a failed train is
  bisected to land its passing prefix and return each failure to its caller.
- A sheriff periodically runs the full suite on `main`, bisects new failures and reverts or fixes
  them.

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

- For new Rust/native builds use `python3 tools/build.py -- build --release --locked -p unbound`.
  It takes one installed build lease, gives each checkout private targets/intermediates, and
  shares compiler results through kache when installed, otherwise sccache. Inside an existing
  build lease add `--direct`; never nest leases. An explicit `CARGO_TARGET_DIR` is preserved.
  See `tools/validation/README.md` for setup and the parallel cache benchmark. Do not enable
  kache build-script-run caching or target seeding: our host build has outputs outside `OUT_DIR`.
- Rust: `~/.cargo/bin` (add to `PATH`), version pinned in `rust-toolchain.toml`.
- .NET: `~/.dotnet/dotnet` (SDK 10; else `DOTNET_ROOT` or the system install, e.g.
  `/usr/local/share/dotnet` on macOS) builds the managed engine; the game code runs on the CoreCLR
  shipped with that SDK.
- Unity's files (reference assemblies, resources, built-in packages; never executed) come from the
  Hub installation of the project's release, else of the profile's release (`~/Unity/Hub/Editor/<version>/Editor` on Linux,
  `/Applications/Unity/Hub/Editor/<version>/Unity.app` on macOS), else from its downloads, unpacked
  once into `<cache>/editors/` (`UNBOUND_UNITY`/`--unity` overrides, `UNBOUND_OFFLINE` forbids
  downloading, `UNBOUND_EDITOR_MODULES` picks the platform modules, default `android,ios`).
- Platforms: Linux x86_64 and macOS arm64. What the OS decides follows the macOS Editor there:
  platform, paths, prefs, native plugins, OS fonts, and the graphics device's answers (Metal:
  device type, SystemInfo caps, format support and defaults, sample counts, depth formats, shader
  targets and variants). Input devices stay as measured on Linux unless measured on macOS.
  Everything else compares against the Linux references; Editor build-toolchain artefacts (C++
  library sort ties, compiler arithmetic) get no per-OS emulation, and such macOS differences are
  recorded, not chased. `--remote` streaming needs NVENC (an NVIDIA GPU on Linux).
- Test project: `~/unity-projects/game` (Unity 6000.0.71f1, built-in RP, mobile).
