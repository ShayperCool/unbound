# Agent workflow

There are four roles: one orchestrator, several workers, a reviewer per worker and a periodic sheriff.
Workers build, test, compare with the Unity Editor, get reviewed and land on `main` themselves.
Nobody waits for permission, a budget or a second approval layer.

Machine-local state lives in `~/.cache/unbound/agents/` (not in Git):

| Path | Contents |
|---|---|
| `QUEUE.md` | the orchestrator's task queue |
| `landed.jsonl` | one line per landed branch, including its train ID and gate log |
| `land-queue.json` | queued callers, requested probes, and recoverable results |
| `land-publications.json` | durable publication receipts, retained until the push is queued |
| `logs/` | gate logs |
| `suite/<label>/` | `summary.json` of each `tools/agents/suite.py` run |
| `baseline/` | the sheriff's last full-suite results (a promoted `summary.json`) |

## Orchestrator

Its full instructions are in [ORCHESTRATOR.md](ORCHESTRATOR.md).

- Writes no engine code and does not re-review or re-run workers' work.
- Keeps about 5 workers busy, at most one per subsystem at a time (physics, particles, rendering,
  UI Toolkit, animation, audio, assets, navigation, scripting/lifecycle, ...).
- Keeps `QUEUE.md`: one line per task with id, subsystem, goal (gap row or roadmap item), status
  (`todo`, `active`, `landed`, `blocked`), the worker thread link and the landed commit.
  Tasks come from:
  - open rows of `docs/COMPATIBILITY_GAPS.md` and `docs/ROADMAP.md`;
  - sheriff reports;
  - follow-ups that workers report.
- Task size: one gap row or one behaviour, landing at roughly 500 changed engine lines or less.
  Split bigger rows before assigning them.
- Gives each worker its own slot (see Slots below) and a branch name, with
  [WORKER_BRIEF.md](WORKER_BRIEF.md) filled in. When a worker reports, it updates the queue, runs
  `python3 tools/agents/slot.py reap` and starts the next task at once. It acts only on
  blockers, failures and decisions that are the user's.
- Starts a sheriff after about every 8 landings or 3 hours, whichever comes first.
- Restarting: `QUEUE.md`, `landed.jsonl` and `git log main` are the whole state. A fresh
  orchestrator reads those, not old conversations.

## Slots

A slot is a persistent worktree, `slot-01` to `slot-10`, under `$UNBOUND_SLOT_ROOT` (default:
`slots/` next to the build root). Its path never changes, so its private build target and its
ignored caches (probe `Library/`, managed `obj/`) stay warm from task to task: the first build of
a task is incremental instead of cold. No target is shared or copied between checkouts (see
tools/validation/README.md for why that is unsafe).

- `python3 tools/agents/slot.py create` makes the missing slots; `slot.py list` shows each slot's
  branch, whether it still holds work, idle time and whether its target is warm.
- A worker starts with `python3 tools/agents/slot.py prepare <slot> <branch>`: it refuses a slot
  that still holds unlanded commits or changes (`--discard` drops them once they are landed or
  abandoned), then switches the slot to a new branch from `main` and keeps ignored files.
- One slot holds one task at a time. A slot is free once its branch is on `main` and it is clean.
- A worker started by a tool that creates its own worktree works in its slot and leaves that
  worktree untouched. `slot.py reap` removes such worktrees, and any other finished worktree
  outside the slots, once it is clean and idle (default 6 h), together with its build target.
  Branches and their commits are retained even when not on `main`; an unreferenced detached
  commit gets a `reaped-worktree/<commit>` branch before removal. It keeps dirty and recently
  active worktrees, slots, the main checkout, worktrees with tracked paths hidden by index
  flags, and worktrees that a symlink under the run caches points into. Clear hidden-path flags
  only after preserving any work. Reaping prints the count kept with unmerged commits. Link discovery uses an incremental index of
  directory listings, revalidating directory identity and change times on every walk. The walk
  has a 300 s discovery limit; an incomplete scan saves progress and refuses to delete worktrees. Retry
  reap to finish indexing a large historical cache, or run `slot.py index` for a read-only
  backfill with a printed 300 s limit. Completed task runtime links are retired
  when landing, reassigning a slot, or reaping its checkout.

## Worker

1. Start in your slot: `python3 tools/agents/slot.py prepare <slot> <branch>`, then work only
   there. Read `AGENTS.md`, this section and the code involved. Check that the gap is still real;
   if not, report that and stop.
2. Check the Unity contract: read the Manual and Scripting API pages for the release, including
   their limitations. Keep three things apart: what is documented, what the reference capture
   shows, and your own hypotheses.
3. Probe: extend an existing `tests/projects/<probe>` or add one.
   - Record the Unity Editor reference in `Reference/unity-<version>`.
   - Record it twice; publish one copy if both captures are identical, and keep the repeat outside
     Git.
   - Run the Editor on the GPU through `~/.cache/compat/with-heavy-job` (or `tests/editor_runner.py`).
   - A new probe declares its land-gate command (the one you pass to `land.py --probe`) in
     `tests/projects/<probe>/gate.json`, with the release as `$UNBOUND_PROBE_UNITY_VERSION`, so
     the sheriff's suite runs it (format: `tools/agents/suite.py`'s docstring); `suite.py --list` shows it.
     `land.py` sets that variable for each `--probe` command (the exported value, else the release
     of the probe named in the command, else `tests/probe_paths.py`'s default, 6000.0.71f1);
     `suite.py` uses the same project lookup per probe; `--release` overrides it for the run.
   - The gate command runs the engine only and compares it with `Reference/` as committed. The
     Editor is only for recording a reference. When the engine run needs files the Editor authors
     (mixer assets, scenes, GUIDs), commit them once with `tests/authored_project.py record` and
     assemble them in the gate. `suite.py` and `land.py --probe` share a process guard that stops
     a step starting the Editor or queueing an Editor job. Both export `UNBOUND_SUITE_NO_EDITOR`,
     which also makes `tests/editor_runner.py` refuse a launch. Guarded runs require Linux with
     `/proc` and pidfd support; other platforms fail explicitly before starting a probe.
4. Implement:
   - Build with `python3 tools/build.py -- build --release --locked -p unbound`.
   - Run the managed tests and the probe against the reference. Iterate until it matches.
   - Engine-only probes use immutable matching runtimes. `suite.py` and `land.py` capture them
     automatically. For a manual replay, capture the completed build with
     `tools/agents/runtime_snapshot.py create --cli <build-cli> --destination <run>/runtime`,
     then use that reference's `unbound` and `managed/`. A rebuilt artifact gets a new run
     reference. Keep project `Library/Unbound`, captures and logs private to the run.
     See [Shared probe runtimes](../validation/README.md#shared-probe-runtimes).
   - Build, run the engine, run the Editor and run tests as often as you need. There are no budgets;
     the job pool bounds the machine.
5. Commit in small Conventional Commits. Update your gap row in `docs/COMPATIBILITY_GAPS.md`
   (`done` plus the probe name) in the same branch.
6. Review: start exactly one reviewer subagent (the Agent tool, foreground). Give it
   [REVIEWER.md](REVIEWER.md), the task statement, the diff range (`main...HEAD`) and the commands you
   ran with their results. Do not give it your reasoning.
7. Fix every P1/P2 finding in your own context and re-run the affected tests. If a fix changed
   behaviour, ask one more reviewer about that delta only. Stop after two rounds. P3 findings are
   optional.
   If you disagree with a finding, say why in your report.
8. Land: `python3 tools/agents/land.py --probe '<probe command>' ...`.
   - Run it in the background and wait for completion (`t3_background_wait`); it may queue
     behind another landing.
   - Exit 3 is a conflict: run `git rebase main`, resolve (see Conflicts), re-run the affected tests,
     land again.
   - Exit 5 is a failed gate: fix it, land again.
   - When `managed/` or `crates/unbound-compat/` changed, the gate also runs the build's
     `gen-stubs -p tests/projects/packages` (about 30 s plus a wait for a tool slot). It fails when the hand-written code
     does not build without stubs or when `managed/UnityEngine/Generated/` differs from what the
     generator writes, and restores `Generated/` afterwards. Regenerate, commit, land again.
   - Automatic probe checks select a probe when its files or its `gate.json` `engine_paths`
     change. Those paths are repository-relative files, directory prefixes or globs. A suite-only
     probe may declare just this metadata. `fast_commands` declares static/short checks to run on
     every code landing, without an Editor capture or long engine replay. Selected full recipes
     run through the suite against the current sheriff baseline when present, preserving its
     existing failures and regression rules. Without a baseline every check must pass.
     Recipe lookup uses the candidate checkout in a guarded process with a printed 10 s budget.
     Automatic checks stay sequential and share a printed 600 s wall-clock budget, at most 300 s each. A timeout or
     exhausted budget fails the gate. Supply a complete declared recipe as explicit `--probe`
     commands, or a suite `--only` command, for longer checks. Exact explicit commands are not
     repeated. A train selects checks for each branch, then unites them by command and release
     under the same total budget, retaining prefix checks during failure isolation. Suite output
     labels are fresh for every execution. Docs-only changes keep their existing exemption.
   - Never force-push and never bypass the gate.
9. Report to the orchestrator in at most 10 lines: the landed commit (or the blocker), what
   changed, the probe result, residuals, follow-ups.

Rules:
- Stay in your own slot. Never touch the main checkout, another worktree or shared refs.
  `git stash`, `git reset --hard` and `git push --force` are off-limits outside your own branch.
- Do not push your branch; `land.py` pushes `main`. If your branch tracks a remote, the
  post-commit hook would push every commit, so run `git branch --unset-upstream` first.
- Never edit a Unity reference, a comparator tolerance or an existing assertion to make a probe
  pass. The oracle guard refuses it unless a commit carries `Oracle-Change: <why>` (for example,
  a re-recorded reference) or `Test-Removal: <why>`.
- A probe that already fails may not count more failures than the sheriff baseline. When a rise is
  decided (by the user, for an insignificant residual that comes with a fix), declare it in
  `tests/projects/<probe>/gate-accepted.json` as `[{"against": "<the baseline's label>",
  "baseline": <its count>, "accepted": <the new count>, "reason": "..."}]`, in a commit with the
  `Oracle-Change:` trailer, and record the residual in the gap table. The entry covers only that
  count's rise against that baseline run; the suite lists it as `accepted`. After the sheriff
  promotes a new baseline it lists the entry as `stale`: remove the file then (no trailer needed).
- No run output, evidence or environment dumps in Git or in files (they leak tokens). Run output goes
  to `Library/Unbound/` or `~/.cache/unbound/`. No test-game names anywhere.
- Unsupported behaviour fails loudly. Behaviour that differs between Unity releases goes through
  the compatibility profile.
- When you are blocked on something only the user or the orchestrator can decide, report it at once
  instead of guessing.
- Your final message is your report: do not end your turn before landing or a real blocker. Wait
  for background builds, tests and `land.py` with `t3_background_wait` (or run them in the
  foreground); an ended turn wakes the orchestrator with nothing to act on.

## Land trains

The worker command stays `python3 tools/agents/land.py --probe '<command>' ...`.
Each caller registers its branch, committed head, worktree and probe releases in the queue,
then waits for its own result. Keep that worktree unchanged until the command finishes.
Use the current tooling: processes started before a tooling update retain their loaded code.
Rebase and restart a waiting caller to adopt a new queue protocol.

The lock holder takes all queued branches for one repository in registration order. A train
with several branches rebases them in a temporary integration checkout beside the slots and
build targets. A branch with a rebase conflict or a refused oracle change is rejected on its
own. Each branch gets its own oracle guard, so one branch's trailer cannot authorize another
branch's changes.

The combined tree gets one release build, one managed test run, Rust tests for the union of
touched crates, and the union of probe commands and their requested releases. Any `--full`
request makes the train run the full gate. Explicit probe commands run with at most 16 concurrent
drivers. Each driver still sends its own engine and build jobs through the heavy-job pool; do
not wrap a whole driver in another heavy-job lease. No worker command changes are needed.

Each command gets a separate `<gate-log>.probes/NNN.log`. Suite defaults include the process
ID and current nanosecond timestamp, so concurrent suites cannot share their default output
paths. Explicit labels keep their requested paths and must be unique. The gate log lists the full command,
release, requesting branches and log path, then its exit code and the tail of any failed command.
All explicit commands finish even after a command exits unsuccessfully. An interruption or
driver-launch exception stops the other running process trees. Failures are reported in request order and
still fail with exit 5, so a train isolates the requesting branch by gating prefixes as before.
A stacked branch's inherited checks stay attributed to the branches carrying them too.

A probe that needs the machine alone declares `"exclusive": true` in its `gate.json`, including
metadata-only declarations for built-in suite recipes. Such a command waits for preceding drivers
and finishes before later drivers start. A suite selecting an exclusive probe is also a barrier;
within a suite, exclusive probes run alone between concurrent groups. These barriers apply to
commands in that gate or suite, not unrelated jobs already admitted by the heavy-job pool.
Commands must name their `tests/projects/<probe>` path, match a declared command verbatim, or use
a direct `suite.py` invocation so the gate can identify this metadata. Shell-wrapped suite
commands conservatively form a barrier whenever an exclusive recipe could be selected.

The initial resource audit marked the following recipes:

| Probes | Shared resource in their runners |
|---|---|
| `destroyphases`, `inputplatform` | Private X display and focus, clipboard or work-area controls |
| `cameramsaa`, `materialcolortransfer`, `terrainlightmaps` | Fixed project staging directories under `Library/Unbound` |
| `canvaslifecycle`, `uirectclipping` | Legacy shell runners with fixed output and managed paths |
| `navpath`, `navmesh`, `trigger2dtiming` | Default capture paths reused by distinct explicit commands |
| `physics2dmotion`, `physics2ddynamics`, `inactive3dloading`, `colliderskin2d`, `trigger2dlifecycle`, `query2dties`, `query2dcontinuation`, `query2dparity`, `querycontactfeatures` | Explicit setup and capture commands share default project and output roots |

Suite recipes that give these helpers private work roots retain the conservative marker, so
direct explicit setup and capture commands keep their order too. A shared parent directory is
not enough to require exclusivity when labels distinguish every output and existing paths are
refused, as in `contactevents3d`.

Other private X display helpers and the audio reference recorder's named PulseAudio sink belong
to reference recording. Gate commands cannot run those paths because the Editor guard refuses
them. Audio gate captures use private run directories and virtual engine device layouts.
Automatic checks remain sequential, with the same total 600 s wall-clock budget and 300 s limit
per check. A touched recipe exceeding 300 s still fails with exit 5 unless every declared command
is supplied explicitly, or an explicit suite invocation covers it. These jobs retain landing priority in the shared
job pool. A passing train fast-forwards main through each branch, records one `landed.jsonl`
line per branch and pushes once. Each caller then refreshes its own branch to its rebased tip.
A single queued branch uses its original worktree and warm target, as before. `--check` remains
an independent gate and can still supply a reusable pass when main and the checks are unchanged.

A failed train is bisected by gating prefixes. Passing prefixes land; the first failing branch
receives its failed gate log. Remaining branches are rebased without that branch and tested
again. Checks from a landed prefix stay in subsequent gates. A stacked branch also retains
the declared probes of earlier queued commits it carries, even if their own request fails.

Caller locks identify live requests without depending on reusable process IDs. A caller that
changes its head or authored files while queued is rejected without discarding its work.
Prepared results and publication receipts are saved before main advances. Publication survives
completed caller results, so a later lock holder can recover the log and launch a missed push.
An interrupted push can be replayed safely. Temporary integration checkouts are locked while active.
An interrupted or changed checkout is retained, with its location reported. Inspect it before
unlocking or removing it; normal cleanup never forces removal. Old callers cannot join a new
train until restarted with the updated tooling.

## Reviewer

See [REVIEWER.md](REVIEWER.md). The reviewer is read-only: it may run tests, but it does not edit,
commit or land.

## Sheriff

A worker with this task, in its own slot on a branch from current `main`:

1. Run `python3 tools/agents/land.py --check --full` (build, all managed and Rust tests), then the
   full probe suite on that build, in the background:
   `python3 tools/agents/suite.py > ~/.cache/unbound/agents/logs/suite-$(date +%Y%m%d-%H%M).log 2>&1`.
   It runs every `tests/projects/<probe>` with a `Reference/` (this checkout's release build, or
   `--cli`/`$UNBOUND`) without starting the Unity Editor (a probe whose step starts one fails with
   `launched the Unity Editor`), prints one line per probe (`pass`, `fail`, `report` for comparators
   without a verdict, `no recipe` with the reason: neither a recipe in `suite.py` nor a valid
   `gate.json` in the probe) and writes
   `~/.cache/unbound/agents/suite/<label>/summary.json`; step logs are in the run's work directory.
2. The suite compares itself with `~/.cache/unbound/agents/baseline/` and lists every probe whose
   status or summary changed; it exits 1 on a new failure, when a failing probe got worse (an
   earlier failing step, fewer passing or more failing checks, more counted failures), or when an
   image measurement of any probe got worse (`images` lines). Image comparators (`report` probes,
   display's screenshot) have no verdict, so the rule is the baseline's own numbers with no
   tolerance: a higher mean as printed (two decimals) or more pixels over the comparator's bound
   of 8. Each is a regression; rerun it once with `--only <probe>` to rule out flakiness. A
   deliberate image change (an accepted residual, a fix that moves a mean up) is promoted with the
   next baseline and named in the report.
3. Find the landing that caused it: bisect within `landed.jsonl`. Then either land a `git revert`
   with the reason, or, if a small fix is obvious, land the fix. Add a queue item for the owner
   subsystem either way.
4. Update the baseline (`python3 tools/agents/suite.py --promote <label>`) and report in at most
   10 lines: suite counts, regressions, what was reverted or fixed.

## Conflicts

- `managed/UnityEngine/Generated/**`: never merge by hand. Take main's version, then regenerate with
  `$UNBOUND gen-stubs -p tests/projects/packages` and commit.
- ABI version numbers (NativeApi, PhysX shim) that both sides bumped: main's value + 1.
- `docs/COMPATIBILITY_GAPS.md`: keep both sides' row edits.
- `docs/PARITY_PROGRESS.md` is a historical log; do not add entries. Git history and the gap rows
  are the record.

## Insignificant residuals

User decision, 2026-11-01: a difference with no visible or other observable effect, which does not
change game logic, does not block landing. Examples: a few float ulps, or isolated pixels off by
about 1-3/255.

Still fixed:
- systematic biases;
- wrong API answers;
- changed counts, callbacks, order or lifetime;
- any real bug behind a small delta.

Record each accepted residual in
[Known insignificant residuals](../../docs/COMPATIBILITY_GAPS.md#known-insignificant-residuals)
with its key or region, size and suspected cause. Keep comparator tolerances unchanged. Do not
spend Editor captures or research just to close such a residual.

## Known pitfalls

- Tests that assert `Unbound:` diagnostics must capture through a `Debug.unityLogger.logHandler`
  wrapper; `Application.logMessageReceived` never sees them.
- After a hand-written managed API change, run `$UNBOUND gen-stubs -p tests/projects/packages`.
  Otherwise the build ships an engine WITHOUT compatibility stubs, which the gate refuses.
- Hand-written code must build without stubs (`-p:UnboundNoStubs=true`, what gen-stubs builds first):
  declare by hand every type or member it names or overrides, then regenerate; never hand-edit
  `Generated/`, or gen-stubs stops running for everyone.
- Run every build, Editor and engine through `tools/build.py` or `~/.cache/compat/with-heavy-job`;
  never nest the launcher around a driver that already uses it.
- Keep long outputs in log files and print only summaries; context fills up quickly.

## Main push delivery

A passing train advances local `main` before enqueueing one normal push to `origin/main`.
Landing returns without waiting for the network. The common Git directory holds `land-push.json`
and `auto-push.log`; `landed.jsonl` records each branch's push state as `pending`, `pushed`,
`failed` or `disabled`. A successful local landing does not imply that its push completed.
The background worker makes at most three attempts per batch, with a 30 s network timeout.
Newer trains coalesce into the latest target. Rejected pushes leave both main refs unchanged;
there is no force push, reset or implicit pull. Failed intent survives process exits and is
retried by the next landing, or manually with:

```bash
python3 tools/agents/land_push.py --repo <main-checkout>
```

`UNBOUND_LAND_NO_PUSH` keeps its existing opt-out. Network attempts never hold the land lock.
Receipt updates briefly share that lock so older coordinators' journal appends remain safe.
