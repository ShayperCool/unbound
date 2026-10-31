# Validation job limits

`install_job_runner.py` installs the common `~/.cache/compat/with-heavy-job`
entry point and the user systemd slice. Every checkout uses the same eight
Editor slots and the same 16 GiB memory budget. The source policy is
`job-limits.json`; the previous serial launcher is backed up before replacement.

The launcher recognizes the actual Unity Editor executable in the command,
including commands wrapped by timeout or time. Editors with `-nographics` share
the heavy-job lock and occupy one global slot. Graphics Editors also acquire
one GPU mutex. Builds, engine runs and other jobs acquire the exclusive lock.
The entry lock prevents new readers from continuously bypassing a waiting writer.
Never put a whole driver under the launcher if its child commands already use it.

The Editor slice has MemoryMax16GiB, MemoryHigh14GiB and MemorySwapMax0. All
Editor children, import workers and compiler servers count toward that budget.
The slice limits are enforced by the OS; slots add an independent process-count
bound. Lock descriptors are closed for child commands. At pool idle, only owned
Unity Roslyn servers in the pool's scopes are terminated. Shared licensing and
other users' compiler processes are excluded.

```bash
python3 tools/validation/install_job_runner.py
~/.cache/compat/with-heavy-job --status
~/.cache/compat/with-heavy-job timeout 600 "$UNITY_EDITOR" -batchmode -nographics ...
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
python3 tools/validation/test_job_runner.py "$XDG_CACHE_HOME/unbound/validation/runs/job-test"
python3 tools/validation/editor_benchmark.py --work "$XDG_CACHE_HOME/unbound/validation/runs/editor-benchmark" --label fresh-eight --count 8
```

For an unconfigured direct benchmark, configure the temporary benchmark slice's
16GiB limit first and hold the old exclusive heavy-job lock around the driver.
For `--launcher pool`, each Editor acquires its own installed launcher lease;
do not wrap that driver in another heavy-job lock. Each benchmark label is
write-once. Its projects, raw Native captures, memory samples, manifests and
process logs are cache-only. The integration check verifies the eight-slot cap,
actual overlap, exclusive writers, GPU serialization and cancellation.
