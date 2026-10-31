# Validation job limits

`install_job_runner.py` installs the common `~/.cache/compat/with-heavy-job`
entry point and the user systemd slices. Every checkout uses the same eight
memory tokens and the same combined 16 GiB memory budget. The source policy is
`job-limits.json`; the previous serial launcher is backed up before replacement.

The launcher recognizes the actual Unity Editor executable in the command,
including commands wrapped by timeout or time. Editors with `-nographics` share
the heavy-job lock and occupy one global slot. Graphics Editors also acquire
one GPU mutex. Direct `unbound run` commands use the engine pool: at most two
jobs, with `--headless` selecting CPU mode and other runs sharing the Editor GPU
mutex. Known `time`, `timeout` and `env` wrappers are recognized. An executable
passed as an argument to a Python driver does not pool that whole driver.
Builds and other jobs acquire the exclusive lock.
Direct engine runs on the same resolved project directory are serialized before
taking pool tokens; different spellings and symlinks share the same project lock.
The entry lock prevents new readers from continuously bypassing a waiting writer.
Never put a whole driver under the launcher if its child commands already use it.

An Editor consumes one of eight memory tokens; an engine job consumes two.
Engines have a separate two-job cap, a 4 GiB limit per job and an 8 GiB pool
limit. Both
pools are children of `unbound.slice`, whose combined MemoryMax is 16 GiB,
MemoryHigh is 14 GiB and MemorySwapMax is zero. This prevents running eight
Editors plus two engines with independent memory allowances. These are
capacity limits; a job that exceeds them can be killed by the OS.

The Editor slice retains MemoryMax16GiB, MemoryHigh14GiB and MemorySwapMax0. All
Editor children, import workers and compiler servers count toward that budget.
The slice limits are enforced by the OS; slots add an independent process-count
bound. Lock descriptors are closed for child commands. At pool idle, only owned
Unity Roslyn servers in the pool's scopes are terminated. Shared licensing and
other users' compiler processes are excluded.
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
