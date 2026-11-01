# Supervised Native captures

Use editor_runner.EditorJob and run/run_parallel in probe runners; each job takes
its own installed launcher lease. Do not wrap their driver in with-heavy-job.
The helper starts the private display only inside the admitted GPU job, checks
the renderer, supervises the Editor and cleans up that private project's helpers.
Shared licensing clients and other projects are excluded from signal selection.
Process start ticks protect against a recycled PID with an identical command.

Set timeout for the full admitted run. Optionally set progress_files to the
probe's flushed output files and progress_timeout to a measured maximum silence
budget, including cold import before the first write. File writes reset this
deadline; unrelated Editor log messages do not. Progress monitoring is opt-in:
some probes legitimately write only once after a long capture. Keep their hard
timeout even when progress monitoring is disabled.

The supervisor also stops if the initiating runner PID exits or is recycled.
This covers a dead driver, not every provider failure: a background driver may
outlive its agent. Keep the hard/progress deadlines and cancel unneeded jobs.
Cancellation, wrong-renderer, no-progress and watchdog results cannot publish.

Retain editor-spec.json, editor-run.json and all logs even on failure. Publishing
requires complete output and identical independent repeats. The helper's generic
publish gate permits only the documented post-shutdown guard exception to a
normal exit; a runner may require strict exit0, as textureraw does. Never turn an
arbitrary forced exit into success because two partial captures happen to match.

Migrated controls: textureraw and quaternioneuler. Their probe sources, oracles
and comparisons stay unchanged. CPU captures verify this path; GPU supervision
has fake-process renderer and private-Xvfb controls, not a new GPU parity claim.

Also migrated: lines and appfacts (windowed GPU Editors). Their references were
published by the ported publish gate from complete identical pairs recorded with
the pre-migration runner and the same probe sources; no live capture has used
the ported Editor path yet.
