# Parity integration and child briefs

Read this file in every new parity implementation or review brief, together with
AGENTS.md and the current validation README. Assign one owner to each shared path.
Keep a bounded control and a clear stopping condition in each task.

Use [CHILD_BRIEF.md](CHILD_BRIEF.md) for a new task. Keep a queue of delivered
packages with their source, evidence and next action. Once three packages await
review/integration, assign free workers to review, conflict resolution or matching
validation instead of opening more feature branches. Drain an existing larger
backlog first. A worker finishing a task does not by itself justify a new feature.

## Reuse and acceptance

Reuse a child's completed gate when the integrated runtime's complete recursive
artifact manifest matches, including CLI, Core, host, managed dependencies and
native libraries. Also require matching probe sources, fixtures, canonical
reference, comparator, compatibility profile and release. Matching only three
headline hashes is insufficient. Record the equivalence and link the saved report.

When bytes or relevant inputs differ, run the affected controls on a matching
runtime. Do not repeat unrelated physics, audio or rendering suites. Merge reviewed
deliveries in batches and run one combined managed, API and affected-probe batch
on the final candidate. Regenerate shared API stubs once for that candidate.

Resolve private managed project and working-directory paths before restore when
the cache is reached through a symlink. Verify the restored project-reference
graph before a costly build: an exit0 restore can still omit those references
with mixed logical/physical paths. Keep failed preparation separate from timing
or runtime acceptance results.

An introduced significant difference blocks the package that causes it. To
attribute a difference elsewhere, use a controlled Root A/B with and without the
package and hold all other source, runtime dependencies and probe inputs fixed.
File ownership or an already-failing record is not proof. Preserve the complete
original union and record unresolved values with their owner. Never remove fields
or loosen tolerances to make a package pass.

### Known insignificant residuals

User decision, 2026-11-01: a difference that has no visible or other observable
effect and does not change game logic does not block a package. Examples include
a few float steps or isolated pixel differences of roughly 1-3/255. Those are
examples, not a new global tolerance or an automatic exemption for every small
number. A systematic frame/object/material bias, a wrong functional API answer,
changed counts, callbacks, order or lifetime, and any actual bug behind a small
delta still need correction. Significant inherited gaps remain open.

Keep every raw value, missing/extra/failing field and original comparator
tolerance. The strict verifier may remain red while package admission succeeds.
Report strict introduced/retained counts separately from the nonblocking
classification; do not count a residual as an exact pass or remove it from a
union. Record its key or pixel region, size, suspected cause and evidence in the
package receipt and docs row, with a durable entry in
[Known insignificant residuals](../../docs/COMPATIBILITY_GAPS.md#known-insignificant-residuals).

Do not spend Native captures or deep offline research solely to close an
insignificant residual. Revisit its classification if existing or later evidence
shows a real bug, visible effect or logic change. Source review, functional tests,
API checks and matching-runtime provenance remain required for the actual change.

Keep new PARITY_PROGRESS entries to about eight lines: change, probe passed/total,
introduced/retained, managed, API, open issues and the full report path. Leave old
entries intact. Distinguish source review, offline projection, runtime validation
and final combined acceptance.

## Running and waiting

Use the currently installed with-heavy-job. Mark a pure build script `--kind build`
and a CPU analysis script that starts no Editor or engine `--kind tool`. Do not
wrap a driver whose child jobs already acquire leases. Keep shared-output or
stateful work exclusive. Use the current README for machine limits, not copied
limits from an old brief.

Start long commands in the provider's background/session mechanism and continue
independent work. Use its completion notification or supported wait primitive.
Claude uses run_in_background followed by t3_background_wait; Codex uses exec
sessions and completion waits. Follow an asynchronous delegate's returned next
instruction and end the turn when it says completion will wake the thread.
Do not use sleep/until, process polling or repeated status calls to wait for jobs.
Timed worker fixtures and the runner's lock retry algorithm are separate from
agent waiting and may use bounded delays.

Finish or cancel background searches/index refreshes before the final response.
Refresh the index once for the completed source batch, not after every edit.
Send one final delivery and only material blocker updates. A finished helper with
no source/result change does not need another agent turn or coordinator message.
Keep long investigations in bounded source/evidence checkpoints; a new worker
reads the compact handoff, not the entire retired conversation.

Native probe runners should use tests/editor_runner.py. It keeps the display
inside the admitted lease, records the initiating driver's process identity,
and supports a probe-output progress deadline. The progress budget includes
startup/import before the first write: enable it only with a justified probe
budget. Ordinary log noise or expected exceptions are not output progress.
An agent failure can leave its background driver alive, so driver ownership
alone is insufficient: retain both the hard and progress deadlines.

Never resume a retired coordinator or its children. A fresh coordinator reads a
compact handoff, saved decisions, branch snapshots and report links before
creating new children. It must not replay an entire retired conversation.
