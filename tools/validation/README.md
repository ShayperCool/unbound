# Validation job limits

Parity integration and new child briefs also follow [PARITY_WORKFLOW.md](PARITY_WORKFLOW.md).

Run validation with the existing build, test and probe commands through this
installed pool. The [plan runner](PLAN_RUNNER.md) is optional. Do not delay a
package to adopt it or create another per-package launch framework.

Use tests/editor_runner.py for supervised Native captures; see
[../../tests/EDITOR_RUNNER.md](../../tests/EDITOR_RUNNER.md).

`install_job_runner.py` installs the common `~/.cache/compat/with-heavy-job`
entry point and the user systemd slices. The source policy is `job-limits.json`;
the previous serial launcher is backed up before replacement.

Admission is by count: at most eight Unity Editors (`-nographics` or graphics),
four direct `unbound run` engine jobs (`--headless` selects CPU mode), two builds
(`cargo build/test/check` and other known build verbs, `rustc`,
`dotnet build/test/publish/restore/msbuild/pack/format`, also
behind time, timeout and env; pass `--kind build` for a script that only builds)
`tool_slots` CPU tools (other `unbound` subcommands such as `compile`, `.NET` tools
run as `dotnet Tool.dll` such as ApiCheck, or anything passed with `--kind tool`)
and `gpu_slots` GPU jobs, shared by graphics Editors and non-headless engine runs.
Wrap build scripts with `--kind build` and analysis scripts that start no Editor or
engine run with `--kind tool`: an unrecognized command is an exclusive `heavy` job
and stops every pool while it runs.
`cargo run` and unknown Cargo aliases stay exclusive because they can execute an engine.
A job reserves everything it needs at once, without blocking while it holds any
of it: a job that cannot get all its slots releases them and retries. A waiting
job therefore never pins slots and never delays jobs whose own slots are free.
Only an exclusive `heavy` job keeps the entry lock while it waits, so newer jobs
queue behind it and a writer cannot starve; a waiting pooled job (for example a
second build behind a running one) never delays jobs that need other slots. Other (`heavy`) commands keep the
exclusive lock, because they may start Editors without going through the pool.
Direct engine runs on the same resolved project directory are serialized before
admission; different spellings and symlinks share the same project lock.
Known `time`, `timeout` and `env` wrappers are recognized. An executable passed as
an argument to a Python driver does not pool that whole driver. Never put a whole
driver under the launcher if its child commands already use it.

Memory is not divided into tokens (`memory_tokens` stays 0; the runner still
accepts weighted one-GiB tokens if a policy sets them). The systemd slices bound
RAM: `unbound.slice` MemoryHigh 14 GiB / MemoryMax 16 GiB for all pools, the
engine slice 8 GiB with 4 GiB per engine job, the build slice 12/14 GiB with
7/9 GiB per build job. Swap is
allowed in all of them, so a peak above the RAM bound swaps instead of being
killed. All Editor children, import workers and compiler servers count toward the
Editor slice. Lock descriptors are closed for child commands. At pool idle, only
owned Unity Roslyn servers in the pool's scopes are terminated. Shared licensing
and other users' compiler processes are excluded.
Every job appends its kind, queue wait, run time, exit code and the scope's memory
peak to `~/.cache/compat/job-timings.jsonl`; use it to tune the limits.
`test_job_runner.py OUT` checks the checkout's runner with private lock files and
policy (`UNBOUND_JOB_RUNNER_STATE`), so it neither waits for nor blocks live jobs.
The launcher disables .NET shared compilation, node reuse and the MSBuild server
for its children, so build servers do not retain memory after a lease ends.

```bash
python3 tools/validation/install_job_runner.py
~/.cache/compat/with-heavy-job --status
~/.cache/compat/with-heavy-job timeout 600 "$UNITY_EDITOR" -batchmode -nographics ...
~/.cache/compat/with-heavy-job timeout 600 /path/to/unbound run -p /path/to/project --headless 30000 --no-watch
# Keep jobs with shared outputs, project state or PlayerPrefs exclusive.
~/.cache/compat/with-heavy-job --kind heavy COMMAND...
# Opt in a known independent script or a differently named engine binary.
~/.cache/compat/with-heavy-job --kind engine-cpu COMMAND...
```

On the measured Linux machine, fresh imports plus the complete 3.36-million-value
motion probe used 1.86GiB for one Editor, 7.33GiB for four and 14.11GiB for eight.
All eight captures matched the pre-existing Native oracle byte for byte, without
swap. The installed launcher was then tested with eight warm independent Editor
projects: all succeeded with the same oracle hash and a 13.42GiB pool peak.
An independent graphics-clipping Editor also completed. This is capacity evidence
for the measured probes, not a promise about arbitrary project sizes or releases.

Cold import timing varies: one six-Editor batch had a 140-second AssetDatabase
refresh outlier, although every capture passed. The count limit is based on
memory and successful completion, not a linear speedup assumption.

```bash
python3 tools/validation/test_job_runner.py "${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/job-test"
python3 tools/validation/editor_benchmark.py --work "${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/editor-benchmark" --label fresh-eight --count 8
```

The default pool mode acquires a launcher lease for each Editor; do not wrap
that driver in another heavy-job lock. For explicit `--launcher direct`, configure
the temporary benchmark slice's 16GiB limit first and hold the exclusive heavy-job
lock around the driver. Direct mode refuses an unbounded slice. Each label is
write-once. Its projects, raw Native captures, memory samples, manifests and
process logs are cache-only. The integration check verifies the eight-token
budget, the two-engine cap, actual mixed overlap, OS memory/swap limits,
exclusive writers, shared GPU serialization and cancellation in both pools.
Run pooled probes in independent projects with distinct output directories.
Keep stateful probes exclusive.

`engine_benchmark.py` warms two independent copies of the motion fixture, then
runs the same captures serially and concurrently through the installed launcher.
It records full commands, input and artifact hashes, timings, sampled combined
memory/swap use, and the SHA-256 of every complete capture. All six captures
must be byte-identical. Supply the release explicitly; it must match the fixture.
The existing motion comparator also checks serial and parallel captures against
that release's canonical `Reference/` oracle, retains all failures and uses its
unchanged tolerance. Existing parity gaps are allowed; new gaps are not.

On the measured Linux machine after the SSD migration, two warmed motion
captures took 12.660 seconds serially and 8.692 seconds concurrently (1.457x
throughput). The sampled combined pool peak was 3.053 GiB, with zero swap.
All six warm/serial/parallel captures were byte-identical. The canonical
comparator introduced zero failures and retained its existing parity gaps.
This is one workload measurement, not a general engine speedup guarantee.

```bash
python3 tools/validation/engine_benchmark.py --release 6000.0.71f1 \
  --cli /path/to/unbound --managed /path/to/managed \
  --work "${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/engine-pool-benchmark"
```

## Measuring build throughput

`build_benchmark.py --plan PLAN.json --out NEW_CACHE_DIRECTORY` runs explicit
independent build batches serially and with two workers. Each build takes one
installed lease; the driver is not wrapped. Every job needs an argv array, cwd,
unique resource_key for its output/target and a name. Optional env, timeout_s
and restore_checks make the inputs explicit. Source/package preparation belongs
outside the measured batch. Use fresh outputs and equivalent source/flags in
both cases, physical paths for private managed projects, and pre-restored packages.

The tool saves complete logs, source plan, worker CPU/wall time and available
lease wait/peak-memory records. It stops a serial batch at its first failure;
canceling it terminates only the build scopes it created. Compare total batch
completion time, not just individual job latency. A managed-only comparison
does not establish the best Cargo parallelism or performance under Editor load.

## Build isolation

For new builds, run `python3 tools/build.py -- build --release --locked -p unbound`
from the checkout being built. The wrapper uses the installed build lease once.
Use `--direct` when a surrounding script already owns that lease. Do not wrap
the default command in `with-heavy-job` again. Cargo arguments follow `--`.

The default target is `$XDG_CACHE_HOME/unbound/builds/targets/<checkout-path-hash>`
or `~/.cache/unbound/builds/targets/<checkout-path-hash>`. Intermediates go to
that target's `build/`, regardless of an inherited `CARGO_BUILD_BUILD_DIR`.
An explicit `CARGO_TARGET_DIR` wins; an ownership marker rejects sharing it
between checkouts. Use that environment variable rather than `--target-dir`,
because `unbound-host` reads the variable for managed/native output placement.
`--print-env` inspects the selection without starting a build. `UNBOUND_BUILD_ROOT`
can select a different cache volume. Put `builds/` on an SSD with enough space;
cache and targets on the same CoW filesystem allow kache to reflink outputs.
Existing targets and global Cargo/shell configuration are not migrated.

`--cache auto` selects kache when installed, otherwise sccache. `--cache none`
disables the Rust wrapper for an uncached control. Explicit CC/CXX selections
are preserved; otherwise C and C++ use the same cache as Rust. Worktree roots
are normalized through `KACHE_BASE_DIR` or `SCCACHE_BASEDIRS`. The kache store
defaults to `builds/kache`, is local-only, and has a 30 GiB budget. The wrapper
disables build-script-run caching, shared hermetic `OUT_DIR`s, target/incremental
seeding and automatic target deletion. `unbound-host` writes required files
outside `OUT_DIR`, which cached script execution would not replay. Real compiler
children stay in the build scope; trusted daemon execution is disabled.

The measured version is kache **2.0.0**, upstream tag at
`e465eff90d87669d7b0a9ee1010f1ee1e44090ed`. Install the official release's paired
`kache` and `kache-engine` binaries together in
`~/.cache/unbound/tools/kache-2.0.0/`, or install kache on PATH. For Linux x86_64,
the release asset `kache-x86_64-unknown-linux-musl.tar.gz` has SHA-256
`6c59665faf1b14d0944391e6f2a1b510195e6be92c63060a9d1770ed00676824`.
Release assets: <https://github.com/kunobi-ninja/kache/releases/tag/v2.0.0>.
There is no need to run `kache init`, change global wrappers or stop an existing
sccache server. Keep both binaries when upgrading a standalone installation.

### Reproducing compiler-cache measurements

Run the driver directly, since its workers take their own build leases:
Populate the ordinary Cargo registry first with `cargo fetch --locked` if needed.
Select the repository's Rust toolchain for the external fixture directories.

```sh
RUSTUP_TOOLCHAIN=1.99.0 python3 tools/validation/compiler_cache_benchmark.py \
  --out "$HOME/.cache/unbound/validation/runs/compiler-cache-NEW" \
  --sessions 4 --workers 2 --units 3 --repeats 3
```

This compiles actual Rust libraries/executables and C++ template units using
cc-rs, with a cold batch followed by three warm batches per backend. Each case
uses new independent source, target and build directories. Only the compiler
cache survives. Backend order reverses on alternate warm repeats. It then
checks a Rust edit, a header edit with unchanged mtime, divergent simultaneous
session inputs, behavior-changing Rust/C++ flags and a deliberate compiler error.
Every successful binary must return its expected value. Every target must have
private dep-info/OUT_DIRs and the required build-script side effect. An embedded
`env!("CARGO_MANIFEST_DIR")` must name the current checkout, as in our asset build.

The report retains per-job logs, commands, wall/CPU time, admission wait, output
hashes and cache statistics. CPU accounting from the process tree excludes a
separate sccache server; compare batch wall time. The sccache server uses a
private port/store and is stopped afterwards, as is a benchmark kache daemon
if one was started. A busy port is rejected; select another with `--sccache-port`.
Do not overlap benchmark drivers or other heavy jobs when comparing throughput.
Synthetic cache gains do not establish full-engine, managed-build or Editor gains.
The recorded measurements and workload limits are in
[COMPILER_CACHE_RESULTS.md](COMPILER_CACHE_RESULTS.md).

### Native host link reuse

The host build script now keeps a private `OUT_DIR/native-link-v1.receipt`.
It checks preprocessed shim inputs, compiler commands/toolchain files, actual
SDK archive bytes, the build recipe and the resulting library's content hash.
An unchanged native library is left in place, including its modification time.
Managed compilation still runs normally. A miss compiles/links as before; inputs
are rechecked before publishing the receipt. Failed builds never publish success.
The existing SDK archive cache remains separate.

`UNBOUND_BUILD_TIMINGS=1 python3 tools/build.py -- build --release --locked -vv`
prints `unbound-build:` records for PhysX, NvCloth, shim compilation, native key
verification, linking, total native work and managed builds. To measure the old
compile/link path with warm SDK archives, set `UNBOUND_NATIVE_CACHE=0`. This also
invalidates the receipt, so a subsequent enabled build must fill it again.

The build script registers Cargo watches for the output library, toolchain files,
compiler environment and transitive headers, including headers outside `native/`.
Watching generated outputs may cause one extra script run after a rebuild;
that run verifies/reuses the library without rewriting it. As with ordinary
Cargo freshness checks, preserving an input's mtime can suppress a script run;
once invoked, the receipt uses content hashes rather than mtimes.

Reproduce the synthetic parallel runs and Cargo invalidation controls:

```sh
python3 tools/validation/native_link_benchmark.py \
  --out "$HOME/.cache/unbound/validation/runs/native-link-NEW" \
  --sessions 4 --workers 2 --repeats 3
```

Add `--real --compiler-cache --repeats 5` to measure the real PhysX/NvCloth/shims
with kache already in the pipeline. Without `--real`, the synthetic SDKs verify
invalidation and failure recovery cheaply; tiny libraries can cost more to
fingerprint than to rebuild. Both modes compile the actual host build script,
use private outputs and the installed build pool, and check every library in a
fresh process. Preparation and cold cache fill are excluded from warm medians.
`--seed-native-out PATH` optionally copies only SDK archives from a read-only
preparation directory into private outputs; no Cargo freshness state is copied.
It is not required for a clean run. Do not overlap benchmark drivers.
See [NATIVE_LINK_RESULTS.md](NATIVE_LINK_RESULTS.md) for measured gains and limits.

Every child build keeps both its intermediates and its final outputs private. Give each
build its own `CARGO_TARGET_DIR` on the SSD cache and do not set a shared
`CARGO_BUILD_BUILD_DIR`. A private target directory is not enough if the build
directory is shared.

A pilot of two worktrees sharing one build directory showed the failure. Their workspace
crates resolved to the same unit directories and build-script `OUT_DIR`s. Because Cargo
judges freshness by modification time, the second build reused the first worktree's stale
compiled crate, overwrote its build-script outputs and compiled its `native/` sources.
Shared `OUT_DIR`s and mtime freshness are therefore not isolation.

Copying or reusing a warm target from another worktree has the same risk: dep-info and
build-script inputs record absolute paths. Before trusting such a target, show that the
current workspace was compiled and that its `OUT_DIR`s are its own. Invalidate only the
copied or owned workspace units, not the whole registry.

The `unbound-host` build script writes `libunbound_physx.so` and the managed build to
`$CARGO_TARGET_DIR/managed`, or to `<checkout>/target/managed` when that variable is not set in the
environment (for example when the target directory comes only from a config file). These
files are outside `OUT_DIR` and Cargo does not track them, so check that they exist in the
private target. `UNBOUND_SKIP_MANAGED_BUILD=1` skips the managed build.

Native builds are not byte-reproducible across target directories. PhysX rebuilt from
unchanged source and ABI has produced different `.so` hashes. Reuse is still decided by
the actual bytes in the complete artifact manifest. A differing new library is a
different artifact: it never rewrites a frozen runtime or its manifest.

## Moving active caches

Stop new jobs and wait for existing commands and their project-specific helpers
to exit before changing cache paths. Hold `parity-heavy-entry.lock` followed by
`parity-heavy-job.lock` exclusively during the migration. Copy with metadata and
hard links preserved, verify the copy, retain the original as a rollback copy,
then replace the old directory with a link to the SSD destination. Preserve both
the logical global-cache path and any physical paths already recorded in scripts.
Check open files and working directories before switching. Never kill the shared
Unity licensing daemon to clear a log descriptor.

On VDO-backed SSDs, check physical free space with `vdostats`; `df` reports the
larger logical capacity. Do not move authored project files or canonical
references independently of their project. A retained backup is frozen after
the switch; future writes belong to the SSD copy.
