# Clipless source mixing and scheduling controls

Independent offline `AudioRenderer` controls for Unity 6000.0.71f1 at 48 kHz,
1024 DSP frames and stereo. Each case has a separate mixer and source. Sources
have no assigned clip except the three clip-backed scheduling controls. A 4096-frame stereo one-shot contains 0.125 on the left
and -0.25 on the right. The callback applies `sample * 0.5 + 0.0625`.

The common cases are idle, an unfiltered one-shot, a filtered one-shot without
main Play, a played generator, a played generator plus one-shot, scheduled
start inside a block, scheduled end inside a block, and a scheduled window
entirely inside one 256-frame interval. Three corresponding clip-backed cases
check the same scheduling calls with an assigned clip. Scheduling uses absolute DSP time,
8192 frames of lead time, offsets 73 and 185, and fixed start/end
phases of one quarter and three quarters of a sample. Both absolute times and
durations stay away from exact integer sample conversion edges. Absolute DSP
clock bits are retained in each run log; scheduling uses the unrounded DSP clock. Scheduled cases retain 88
render blocks to observe natural completion and route quietness before Stop.

Callbacks only access preallocated managed arrays and counters. Setup, source
commands, scheduling, sink registration, capture and file IO run on the main
thread. Captures retain every device sample, raw master/child sink sample,
callback input/output sample, callback length and channel count, and source
state before/after submission and every render. DSP observations are sample
ticks relative to the case's submission clock. A sink's untouched -3 sentinel
is retained. Native rejection or ignored scheduling is an observed result.

Run in private work directories, selecting the release explicitly:

```sh
python3 tests/projects/audiogeneratorboundaries/run.py native \
  --release 6000.0.71f1 --work "$XDG_CACHE_HOME/unbound/boundaries-native-a"
python3 tests/projects/audiogeneratorboundaries/run.py engine \
  --release 6000.0.71f1 --work "$XDG_CACHE_HOME/unbound/boundaries-engine" \
  --runtime /path/to/private/matching/runtime \
  --native-project "$XDG_CACHE_HOME/unbound/boundaries-native-a/project"
python3 tests/projects/audiogeneratorboundaries/compare.py \
  --release 6000.0.71f1 --candidate /path/to/capture \
  --report "$XDG_CACHE_HOME/unbound/boundaries.jsonl.gz"
```

`run.py` invokes the installed heavy-job limiter once per heavy process. Do not
wrap the runner again. `--focused` selects only the filtered one-shot and short
scheduled window. `--exact` on the comparator checks independent Native repeats
bit for bit. The comparator reuses the complete mixer comparison union with
unchanged absolute 2e-5 and relative 2e-4 sample tolerances. No frames or metadata
are discarded or aligned. `--before` retains the full three-way union.

The structural verifier checks completeness without requiring a particular
scheduling or mixing result. Publish one canonical release reference only after
two complete, independent Native captures agree exactly. Keep repeats, engine
outputs, full unions, failures and runtime manifests outside Git.
