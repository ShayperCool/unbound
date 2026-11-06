# Clipless source lifecycle

This 48 kHz control uses public `AudioSource` operations and offline `AudioRenderer`
capture on Unity 6000.0.71f1. It creates an attenuation-only mixer, a clipless source
filter, and an independent listener filter. No source starts before the explicit
`Play`. The timeline includes render bursts before audio updates, amplitude changes,
Pause, UnPause, Stop, and replay.

Every step saves the complete device and mixer sink buffers. The sink is initialized
to -3 before registration so an unwritten graph remains observable. `trace.f32`
contains nine values per render: source callback count, listener callback count,
isPlaying, isVirtual, timeSamples, last source callback sample length, amplitude
written by that callback, and the listener callback's first and last input sample.
The last five fields retain their previous observation when no callback occurs.
`capture.json` records every step in order. The fixture does not adjust alignment,
quantize samples, or normalize callback timing.

Two independent Native projects agreed bit for bit on all 94,422 metadata/sample
observations before publishing the single canonical reference. Native does not run
the source callback or write the mixer sink before Play. Play activates the graph
immediately, while the source waits for an audio update. Its first pull requests two
configured DSP buffers; thereafter a callback supplies the following buffer. Pause
retains this read-ahead, Stop clears it, and neither runs the source callback. The
listener still runs once per rendered DSP buffer.

```bash
work="$HOME/.cache/unbound/generator-lifecycle"
python3 tests/projects/audiogeneratorlifecycle/run.py native --release 6000.0.71f1 --work "$work/native-a"
python3 tests/projects/audiogeneratorlifecycle/run.py native --release 6000.0.71f1 --work "$work/native-b"
python3 tests/projects/audiogeneratorlifecycle/compare.py --release 6000.0.71f1 --exact \
  --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
python3 tests/projects/audiogeneratorlifecycle/run.py engine --release 6000.0.71f1 \
  --native-project "$work/native-a/project" --runtime "$work/runtime" --work "$work/engine"
python3 tests/projects/audiogeneratorlifecycle/compare.py --release 6000.0.71f1 \
  --before "$work/before/capture" --candidate "$work/engine/capture" --report "$work/union.jsonl.gz"
```

The runner owns its heavy-job wrapping. The comparator reuses the original full
missing/extra/failing audio union, with ATOL 2e-5 and RTOL 2e-4. All repeated captures,
engine captures, logs, rejections and unions belong outside Git. This control makes
no claim about reset or snapshot parity, scheduled generators, or other DSP layouts.

## Engine runs without the Editor

`Authored/unity-6000.0.71f1/<set>/` holds every file a Native run authored in the Editor:
mixer assets, scenes, `.meta` GUIDs, the Editor's ProjectSettings and `packages-lock.json`.
`authored.json` records the authoring command and the hash of every fixture file it started
from. Each set was recorded with `tests/authored_project.py record` from a Native run whose
capture matched the canonical reference exactly. Sets: `default`.

```bash
python3 tests/authored_project.py assemble audiogeneratorlifecycle default --release 6000.0.71f1 --out "$work/native"
```

The assembled project is the `--native-project` of `run.py engine`. A set is refused once any fixture
file under Assets/, Packages/ or ProjectSettings/ has changed: re-run its recorded command in a
fresh cache directory and record that project again. `tools/agents/suite.py` runs every profile
this way.

