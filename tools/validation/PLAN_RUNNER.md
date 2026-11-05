# Reusable finite validation runner

`plan_runner.py` owns the repeated transport: input binding, read-only readiness,
one installed lease per job, timeout, once-only accounting, private logs and a
compact result. A package supplies data and its existing verifier commands.
Do not copy the runner or generate another launch framework for each package.

This runner consumes a **trusted Root-issued plan**, not untrusted commands. Its
approval file is an exact input binding, not a signature or security boundary.
It does not replace source review, package provenance, semantic comparators,
network/credential controls, or Root's final runtime admission. In particular,
checking a hash list does not establish that the list covers every dependency.

## Interface

Use physical absolute paths for the plan, approval, tools, inputs and run parent.
The run parent must already exist in `Library/Unbound` or the Unbound cache. The
run directory must be new. `{run}` is the sole substitution, allowed in argv,
environment values and cwd. Cwd is either `{run}` or an existing physical path.
No directory preparation, environment discovery, shell expansion or retry is
implicit. The runner creates only its private output namespace and its receipts;
payload output paths belong to the reviewed plan.

```json
{
  "version": 1,
  "run_dir": "/physical/cache/unbound/validation/runs/example-a",
  "launcher": "/physical/cache/compat/with-heavy-job",
  "timeout": "/usr/bin/timeout",
  "systemctl": "/usr/bin/systemctl",
  "env": {
    "HOME": "/home/launcher-owner",
    "PATH": "/usr/bin:/bin",
    "LANG": "C.UTF-8",
    "XDG_RUNTIME_DIR": "/run/user/1000",
    "DBUS_SESSION_BUS_ADDRESS": "unix:path=/run/user/1000/bus"
  },
  "inputs": {
    "/physical/cache/compat/with-heavy-job": "<sha256>",
    "/home/launcher-owner/.cache/compat/job_runner.py": "<sha256>",
    "/home/launcher-owner/.cache/compat/job-limits.json": "<sha256>",
    "/usr/bin/timeout": "<sha256>",
    "/usr/bin/systemctl": "<sha256>",
    "/usr/bin/python3.13": "<sha256>",
    "/physical/reviewed/check.py": "<sha256>"
  },
  "jobs": [{
    "id": "metadata-consumer",
    "kind": "tool",
    "argv": ["/usr/bin/python3.13", "-I", "-B", "/physical/reviewed/check.py", "--out", "{run}/metadata.json"],
    "cwd": "{run}",
    "timeout_s": 120
  }]
}
```

The explicit environment replaces the parent's environment. Supply the runtime
bus keys required by the installed pool and required toolchain flags in the
reviewed map. The installed `with-heavy-job` shell entry locates its Python entry
through HOME: use the launcher's real HOME and pin that `job_runner.py` and the
installed `job-limits.json` too. Readiness checks these stock-wrapper dependencies. A
private payload HOME must be an explicit reviewed `env -i` argv inside the lease,
with all required payload keys, not a replacement of the launcher's HOME. Keep
package/tmp/output paths private. Do not copy the agent's entire environment.
The installed pool retains its documented compiler-server flags.
The plan and payload logs can contain sensitive data; keep them private and
follow the package's existing publication rules. The runner's console and result
contain no argv or environment values.

The exact approval is a separate Root decision, issued after independent review:

```json
{
  "version": 1,
  "issued_by": "Root",
  "plan_sha256": "<hash of the exact plan bytes>",
  "runner_sha256": "<hash of the reviewed plan_runner.py>",
  "run_dir": "/physical/cache/unbound/validation/runs/example-a",
  "job_ids": ["metadata-consumer"]
}
```

```bash
python3 tools/validation/plan_runner.py check --plan /physical/plan.json --approval /physical/approval.json
python3 tools/validation/plan_runner.py run --plan /physical/plan.json --approval /physical/approval.json
```

Do not wrap this driver in a lease: its jobs acquire their own. Jobs that already
acquire leases must not be nested inside it. Each job gets a unique scope id
recorded before spawn; accounting does not depend on truncated command strings.
Jobs run sequentially. Independent plans can run concurrently under the installed
pool's unchanged limits.

For the stock Cargo wrapper, use a `kind: build` job with argv
`["/usr/bin/python3.14", "/reviewed/checkout/tools/build.py", "--direct", "--", "test", "--locked", "-p", "unbound-runtime", "--lib", "editor_resources::tests", "--", "--nocapture"]`.
The runner owns the single build lease; `--direct` disables the wrapper's inner
lease. Readiness refuses the known wrapper without that flag. The runner never
inserts or edits arguments: Root must explicitly approve this exact argv. An
existing proposal without `--direct` needs a plan amendment, not an unleased
adapter or a second lease mode that loses ownership of the payload scope.

All exits remain raw. A nonzero exit stops the sequence, including timeout 124.
If a strict comparator legitimately returns 1 for retained differences, its
existing reviewed semantic consumer must separately decide package eligibility;
do not add an unconditional `|| true` or call raw failures passes. The runner
never sets `admission=true`.

`result.json` records the attempted/completed job ids, raw exits, scope ids, log
hashes, readiness time and wall time, including admission waits. An attempt is
persisted before spawn; a failed spawn is still attempted. An existing incomplete
or finished output directory is refused. A continuation needs a new exact plan,
Root approval and namespace, with old spending and failure evidence retained.

The payload timeout runs inside its lease. A second outer deadline includes pool
admission with a 30-second allowance and stops only that job's UUID scope and
process group. A cancelled or failed run is not automatically resumed. Payloads
that escape their owned scope are outside this transport's contract.

## Adoption and observation

Do not migrate active validations or change their pinned plans. Choose an
unstarted, unspent validation and bind its existing semantic consumers to a new
plan. Review the common transport once at its exact hash; later reviews cover the
changed plan, inputs and semantic consumers. A runner code change requires its
own scoped review, while unchanged transport does not need a new per-package
conformance framework. Preserve any additional controls required by the grant.

Track time from package readiness to first payload, transport-related stops and
time to the final decision in the existing package index. Distinguish those from
lease waits, compilation, payload execution and actual product failures. Runner
unit controls establish transport behavior, not reduced engine-admission latency.
Conclude that throughput improved only after actual comparable package use;
report an insufficient observation sample honestly.

Isolated transport controls, with synthetic payloads and a private fake launcher:

```bash
python3 -m unittest discover -s tools/validation -p test_plan_runner.py -v
```

They preserve their output under the Unbound cache and spend no engine, Editor,
SDK, native or installed-pool budget. An end-to-end installed-pool pilot must
separately record its own tool leases; it is not reuse of a validator's allowance.
