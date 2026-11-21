# Worker brief template

The orchestrator fills this in and sends it as the worker's whole task. Keep it short; the worker
reads the code and docs itself.

```
You are an Unbound worker. Read AGENTS.md and tools/agents/WORKFLOW.md (Worker section) first,
then follow that loop: probe vs Unity Editor, implement, build and test, one reviewer subagent,
land on main with tools/agents/land.py, report.

Slot: <slot-NN>, branch <t3/...>. Start with `python3 tools/agents/slot.py prepare <slot> <branch>`
and work only in that slot (not in any worktree a tool created for you).
Task <id>: <one-sentence outcome>
Subsystem: <area>. Likely paths: <paths>.
Source: docs/COMPATIBILITY_GAPS.md row "<row>" | ROADMAP item "<item>" | sheriff report <link>
Unity behaviour to match: <short description; doc links if known>
Probe: <existing tests/projects/<probe> to extend | new probe>
Context: <related landed commits, earlier attempts, known pitfalls; or "none">
Done when: the probe matches the Unity reference (or residuals are recorded), managed tests pass,
the reviewer has no P1/P2 left, and the change is landed on main.
You may start exactly one reviewer subagent per review round, as WORKFLOW.md describes.
Before each review round, check the diff for what reviews most often find:
- a changed value, order, log line or file write that no Editor reference measures (revert it,
  fail loudly, or mark it unmeasured in the code and the gaps row);
- a rule the probe cannot tell from an alternative (edit another axis, mutate before the first
  read, two writes in one frame, a reused pooled buffer): add a control;
- null, destroyed objects, array elements, static or lambda targets, empty and multiple targets;
- logic that already exists (use the helper or Unity's own C#), and every other path computing
  the same rule (cache, legacy, V1/V2, deferred, typed overloads);
- silent fallbacks or broad catches where Unbound cannot match;
- tests that pass without the change (revert the line once and see the test fail);
- the same checks again on every fix: most later-round blocks are bugs the fixes introduced.
Follow-up rounds review the fix delta only; a clean round ends the review.
Do not end your turn until the change is landed or you are blocked. Wait for long commands
(builds, tests, probes, land.py) with t3_background_wait or in the foreground; ending the turn
while they run wakes the orchestrator for nothing.
Report in at most 10 lines: landed commit or blocker; what changed; probe result; residuals;
follow-ups for the queue.
```
