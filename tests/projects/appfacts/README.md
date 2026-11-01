# Application, Debug, Profiler and SystemInfo facts

A windowed GPU Editor probe for facts a game reads from `Application`, `Debug`,
`Profiler` and `SystemInfo` in Play mode (`Assets/Scripts/AppFactsProbe.cs`).
It writes two records.

- `values.jsonl` holds what must be the same on every run and machine:
  - `Application.isLoadingLevel` around synchronous, asynchronous, held
    (`allowSceneActivation`) and unloading scene operations;
  - stack traces written by `Debug.ExtractStackTraceNoAlloc` at several buffer
    sizes, with project paths written as `<project>`;
  - `Profiler.GetRuntimeMemorySizeLong` of the textures, meshes, audio clips,
    render textures, materials and components the probe creates;
  - changes in the graphics driver memory figure, and the temp allocator size;
  - the argv that `Application.OpenURL` runs.

  Only this record is published.
- `machine.jsonl` holds what the machine decides: device name, model and
  unique identifier, OS string, CPU, GPU and memory, battery, console log path,
  licence, startup logs and the graphics driver start figure. Each capture
  saves what the OS reports (`os.json`: hostname, `/etc/machine-id`, CPU, DMI
  product strings, GPU, power supplies) beside it.

`machine.jsonl` and `os.json` are never published. They identify the machine
and its user: hostname, machine id, home directory in the log path. They stay
with the capture under the validation cache.

`OpenURL` runs only recording stubs. `xdg-open`, `open`, `gio` and the usual
browsers are placed first on `PATH` and append their name and arguments to
`openurl.log`; no browser is started. Unbound's capture also sets
`UNBOUND_OPEN_URL` to the `xdg-open` stub.

## Running

Run from the repository root. The runner takes one installed launcher lease per
Editor through `tests/editor_runner.py`; do not wrap it in `with-heavy-job`.

```sh
python3 tests/projects/appfacts/run.py editor  --release 6000.0.71f1 --label L --dry-run
python3 tests/projects/appfacts/run.py editor  --release 6000.0.71f1 --label L
python3 tests/projects/appfacts/run.py check   --release 6000.0.71f1 --label L
python3 tests/projects/appfacts/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=/path/to/unbound python3 tests/projects/appfacts/run.py unbound --release 6000.0.71f1 --label U
python3 tests/projects/appfacts/compare.py tests/projects/appfacts/Reference/unity-6000.0.71f1 \
  ~/.cache/unbound/validation/runs/appfacts/U/unbound \
  --report ~/.cache/unbound/validation/runs/appfacts/U/report.json
python3 tests/projects/appfacts/compare.py --machine ~/.cache/unbound/validation/runs/appfacts/U/unbound
```

The Unbound capture's `run.json` records the absolute project path the engine was given and the
start time. `--machine` checks the reported console log (written as `<project>/...`) as that real
file: it must exist, be non-empty and be written during the run. `test_compare.py OUT` checks the
comparator itself on synthetic captures.

Projects, captures, logs, `editor-run.json` and reports go to
`$XDG_CACHE_HOME/unbound/validation/runs/appfacts/<label>/`. The Editor
deadlines are counted from admission into the lease. The hard limit is 1200 s.
The progress limit is 480 s without a write to `values.jsonl`, `machine.jsonl`
or `openurl.log`. The probe flushes after each step, so this budget covers cold
package resolution and import before the first write. See `run.py` for the
measurements behind both.

`publish` refuses unless both repeats exited with 0 on the NVIDIA renderer
without a forced stop, wrote `machine.jsonl`, end `values.jsonl` with exactly
one `complete: true`, have no `fatal` or duplicate key, and are byte-identical
in `values.jsonl`. It never overwrites an existing reference. `check` also runs
the machine comparison for each Editor capture.

## Comparison and tolerances

`compare.py REFERENCE ACTUAL` compares the whole key union of `values.jsonl`
exactly and reports every missing, extra and differing value. The only
normalization is the runtime's wording of an argument name in exception
messages: Mono's `\nParameter name: o` equals CoreCLR's ` (Parameter 'o')`.

`compare.py --machine CAPTURE [--editor]` checks `machine.jsonl` against the
capture's `os.json`, `/etc/os-release` and the Editor licence file read at
comparison time. The checks cover the GPU's PCI ids, OpenGL strings and memory,
the CPU vendor and model, the host name, the machine id as the device unique
identifier, memory and OS release, and the battery. They also cover the log
path (with `--editor` the Editor's `-logFile`, otherwise the project's
`Library/Unbound/Editor.log`, which must exist) and the licence. The CPU clock
must be a positive integer no greater than `cpuinfo_max_freq`, because it is a
current reading. The graphics driver start figure must be a non-negative byte
count. These facts are always compared with the machine that ran the capture,
never with another machine's capture.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl` was published with `run.py publish`
from two independent windowed GPU Editor captures (NVIDIA GeForce RTX 4060).
Both exited with 0 after `Cleanup mono`, their `values.jsonl` (555 records) was
byte-identical, and the machine facts of both agreed with the OS. They ran with
the committed `Assets`, `Packages` and `ProjectSettings` (34 files,
byte-identical) and the runner from before `editor_runner`. The ported runner
was validated with `py_compile`, `--dry-run`, `check` and `publish` on those
saved captures, plus a publish refusal on a saved pair whose values differ. No
live capture has used it yet.

## Recording stub completion

The OpenURL fixture takes its file offset before launch and waits up to five seconds of
`Stopwatch` elapsed time for a newline-terminated stub record. It blocks only this fixture
step; simulated headless frame time cannot exhaust the wait. It records the exact appended
lines, and a missing or incomplete record fails the capture at the deadline. It does not
change keys, argv, reference bytes or comparison tolerances. Existing Native records use
the same newline protocol, including the empty and null URL cases.

`AppFactsUrlRecordingTests` covers immediate and delayed writes, absent, late and incomplete
records. `test_recording.py NEW_CACHE_OUT` checks compatibility with the canonical protocol
and runner/comparator refusal controls. A stressed capture may delay only the private
recording stubs before their writes to reproduce the former process-start race.
