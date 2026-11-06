# Agent workflow

There are four roles: one orchestrator, several workers, a reviewer per worker and a periodic sheriff.
Workers build, test, compare with the Unity Editor, get reviewed and land on `main` themselves.
Nobody waits for permission, a budget or a second approval layer.

Machine-local state lives in `~/.cache/unbound/agents/` (not in Git):

| Path | Contents |
|---|---|
| `QUEUE.md` | the orchestrator's task queue |
| `landed.jsonl` | one line per landing, written by `land.py` |
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
- Starts each worker in its own new worktree from current `main`, with
  [WORKER_BRIEF.md](WORKER_BRIEF.md) filled in. When a worker reports, it updates the queue and
  starts the next task at once. It acts only on blockers, failures and decisions that are the
  user's.
- Starts a sheriff after about every 8 landings or 3 hours, whichever comes first.
- Restarting: `QUEUE.md`, `landed.jsonl` and `git log main` are the whole state. A fresh
  orchestrator reads those, not old conversations.

## Worker

1. Read `AGENTS.md`, this section and the code involved. Check that the gap is still real; if not,
   report that and stop.
2. Check the Unity contract: read the Manual and Scripting API pages for the release, including
   their limitations. Keep three things apart: what is documented, what the reference capture
   shows, and your own hypotheses.
3. Probe: extend an existing `tests/projects/<probe>` or add one.
   - Record the Unity Editor reference in `Reference/unity-<version>`.
   - Record it twice; publish one copy if both captures are identical, and keep the repeat outside
     Git.
   - Run the Editor on the GPU through `~/.cache/compat/with-heavy-job` (or `tests/editor_runner.py`).
4. Implement:
   - Build with `python3 tools/build.py -- build --release --locked -p unbound`.
   - Run the managed tests and the probe against the reference. Iterate until it matches.
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
   - Never force-push and never bypass the gate.
9. Report to the orchestrator in at most 10 lines: the landed commit (or the blocker), what
   changed, the probe result, residuals, follow-ups.

Rules:
- Stay in your own worktree. Never touch the main checkout, another worktree or shared refs.
  `git stash`, `git reset --hard` and `git push --force` are off-limits outside your own branch.
- Do not push your branch; `land.py` pushes `main`. If your branch tracks a remote, the
  post-commit hook would push every commit, so run `git branch --unset-upstream` first.
- Never edit a Unity reference, a comparator tolerance or an existing assertion to make a probe
  pass. The oracle guard refuses it unless a commit carries `Oracle-Change: <why>` (for example,
  a re-recorded reference) or `Test-Removal: <why>`.
- No run output, evidence or environment dumps in Git or in files (they leak tokens). Run output goes
  to `Library/Unbound/` or `~/.cache/unbound/`. No test-game names anywhere.
- Unsupported behaviour fails loudly. Behaviour that differs between Unity releases goes through
  the compatibility profile.
- When you are blocked on something only the user or the orchestrator can decide, report it at once
  instead of guessing.
- Your final message is your report: do not end your turn before landing or a real blocker. Wait
  for background builds, tests and `land.py` with `t3_background_wait` (or run them in the
  foreground); an ended turn wakes the orchestrator with nothing to act on.

## Reviewer

See [REVIEWER.md](REVIEWER.md). The reviewer is read-only: it may run tests, but it does not edit,
commit or land.

## Sheriff

A worker with this task, in its own worktree from current `main`:

1. Run `python3 tools/agents/land.py --check --full` (build, all managed and Rust tests), then the
   full probe suite on that build, in the background:
   `python3 tools/agents/suite.py > ~/.cache/unbound/agents/logs/suite-$(date +%Y%m%d-%H%M).log 2>&1`.
   It runs every `tests/projects/<probe>` with a `Reference/` (this checkout's release build, or
   `--cli`/`$UNBOUND`), prints one line per probe (`pass`, `fail`, `report` for comparators
   without a verdict, `no recipe` with the reason) and writes
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
