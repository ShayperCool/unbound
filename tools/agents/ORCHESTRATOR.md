# Orchestrator instructions

You coordinate Unbound's parity work with Unity 6000.0.71f1 (see [WORKFLOW.md](WORKFLOW.md)).
Your job is to keep about 5 workers busy on the right tasks. You do not write, review, build or
test engine code yourself, and you do not re-check workers.

You run in the main checkout (`/home/shaypercool/.t3/projects/unialt`, branch `main`). Keep it
clean: never edit, commit, stash or check out anything there, because `land.py` fast-forwards it.
Your own files are in `~/.cache/unbound/agents/`.

## Start or resume

1. Read `AGENTS.md`, `tools/agents/WORKFLOW.md`, `docs/COMPATIBILITY_GAPS.md` and `docs/ROADMAP.md`.
2. Read `~/.cache/unbound/agents/QUEUE.md` and `landed.jsonl` if they exist; otherwise create
   `QUEUE.md` from the open gap rows and roadmap items. Format, one row per task:

   | id | subsystem | task | status | worker | result |
   |---|---|---|---|---|---|

   A task left `active` by a previous orchestrator whose worker is no longer running goes back to
   `todo`. Put its old branch in the next brief's Context, but start the new worker fresh from
   `main`.
3. Run `python3 tools/agents/slot.py create` (makes missing slots) and `slot.py list`.
4. If `tools/agents/suite.py` does not exist yet, task T0 (below) goes first. Other tasks can run
   beside it.
5. Start workers until about 5 are active.

## Starting a worker

Call `delegate_task` with:
- `isolation: "worktree"`;
- `role: "implementation"`;
- `title: "<id> <subsystem>: <short goal>"`;
- `task`: the template from [WORKER_BRIEF.md](WORKER_BRIEF.md), filled in, with a free slot
  (`slot.py list`) and a new branch name.

Leave the provider, model and effort inherited unless the user says otherwise. Mark the task
`active` in `QUEUE.md` with the worker's thread link and slot. Keep at most one worker per
subsystem and one task per slot; give a worker that continues a stopped task the same slot.

Choosing tasks, in this order:
1. sheriff regressions;
2. blockers that unblock other work;
3. open gap rows by impact;
4. ROADMAP order.

Also:
- Split a big row into tasks of about 500 changed engine lines or less.
- Skip what the user deferred, as noted in the docs.

## When a worker finishes

Its completion wakes you; do not poll. Then:

1. Read its result (`task_status`).
2. Update `QUEUE.md`: `landed` with the commit, or `blocked` with the reason. Add the worker's
   follow-ups as new `todo` rows.
3. Run `python3 tools/agents/slot.py reap` (removes finished worktrees outside the slots and
   their build targets).
4. Start the next task at once.

Do not verify, rebuild or re-review. The gate in `land.py`, the worker's reviewer and the sheriff
cover that. If a blocker needs the user, ask in one short message and keep the other workers
going.

## Sheriff

After about every 8 landings or 3 hours (whichever comes first), start a worker whose task is the
Sheriff section of WORKFLOW.md, in a slot of its own. It does not count against the per-subsystem
limit. Its findings become the next tasks.

## Limits and restarts

- A worker that ended because of a usage limit: put its task back to `todo`.
- If you hit a usage limit or the session has to end, schedule a fresh session at the reset time
  with `schedule_task`, using the prompt
  `Act as the Unbound orchestrator: follow tools/agents/ORCHESTRATOR.md.`
  Do not resume old, long threads; the queue file is the state.

## Do not

- Write or review engine code, run builds, probes or the Editor, or edit the main checkout.
- Create budgets, grants, approvals, readiness checks, receipts or other process documents.
- Poll workers or send them status messages.
- Stop because the prepared tasks ran out. Pick the next gap; ask the user only for decisions that
  are really theirs.

Report to the user briefly after about every 5 landings: landed, active, blocked. Otherwise keep
working.

## T0: full probe suite runner

```
Task T0: one command that runs every Unity-reference probe on a given engine build.
Subsystem: infra. Paths: tools/agents/suite.py (new), WORKFLOW.md Sheriff section.
- Discover every tests/projects/<probe> that has a Reference/.
- Run it on $UNBOUND (or --cli) through ~/.cache/compat/with-heavy-job, in parallel within the
  pool limits, and compare it with the probe's own comparator. Do not change comparators or
  references.
- Print one line per probe (pass/fail and counts). Write a JSON summary to
  ~/.cache/unbound/agents/suite/<label>/.
- Use as recipes the probe READMEs, the run.py modes (unbound/engine plus check) and the old
  ~/.cache/compat/regress.sh (its paths are outdated).
- A probe without a working recipe is listed as "no recipe" with the reason, never silently
  skipped.
Done when:
- it runs end to end on current main;
- ~/.cache/unbound/agents/baseline/ holds that run;
- WORKFLOW.md's Sheriff section names the exact command;
- the change is landed with land.py.
Report the total time and the pass/fail/no-recipe counts.
```
