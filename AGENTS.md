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
- Do not rewrite parity-branch history or force-push. The user handles history cleanup
  before the final PR.

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

## Parallel parity delivery

- Keep several independent feature implementations moving when explicitly authorized, unowned
  work is ready. Audits and reviews are separate from implementation. A finished package waiting
  for Root must not stop unrelated ready work; never create filler tasks or duplicate a writer.
- The coordinator advances the authorized queue and starts a new independent review for each
  prepared source head without requiring a separate acknowledgement of every checkpoint.
  Validation still needs an applicable explicit release; integration and runtime admission
  remain Root's decisions.
- Consolidate routine receipts into review-ready packages with exact source/artifact links,
  findings, proposed commands and spending. Report material blockers promptly. Preserve the
  full raw evidence outside Git, and keep pins, ownership and spent budgets in compact state.
  Do not poll running agents or revive retired owners.
- Root dispatches independent completed packages to its own parallel reviewers for source,
  artifact and validation checks. They return short decision-ready findings and concrete drafts;
  Root handles corrections, finite releases, composition and admission instead of repeating each
  audit serially. Reviewers do not mutate shared source or spend a validator's budget.
- Routine launch notices go to the saved state/index, not a separate queued message per launch.
  Keep one consolidated pending receipt digest plus genuinely new material blockers. Deduplicate
  superseded notices only after archiving their complete text and routing unresolved work; never
  discard human instructions or failure witnesses. Do not repeat stale requests after Root has
  already supplied the applicable decision or grant.
- Before spending a validation build, suite or capture, complete the short readiness check in
  `tools/validation/PARITY_WORKFLOW.md`. Reuse exact-bound preparation evidence; extra compilation,
  smoke tests or captures require an explicit allowance and count toward the finite budget.
- Fix cosmetic review nits directly without another full review. Behavioral, assertion, fixture,
  dependency or composition changes get a new independent review focused on the delta and its
  affected consumers, carrying forward exact-bound prior findings and evidence. No retired owner
  is resumed and no spent gate is silently repeated.
- Root may release an exact candidate's finite validation conditionally on a clean independent
  review and readiness check, so the coordinator can advance it without another acknowledgement.
  Any unallowed source/input change or stop condition returns to Root; final admission stays Root's.
- Maintain one authoritative package index with current source/baseline pins, owner, next action,
  consumed budget and decision links. Close superseded requests when the applicable decision is
  recorded, keeping their full history and unresolved findings. Refill only authorized, unowned
  implementation work; a fixed count of waiting packages alone does not stop independent work.

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
- Platforms: Linux x86_64 and macOS arm64. Behaviour measured against the Linux Editor (graphics
  caps, input devices) stays as measured on macOS; what the OS decides (platform, paths, prefs,
  native plugins) follows the macOS Editor there. `--remote` streaming needs NVENC (an NVIDIA GPU on
  Linux).
- Test project: `~/unity-projects/game` (Unity 6000.0.71f1, built-in RP, mobile).
