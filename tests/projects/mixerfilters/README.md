# Mixer filter PCM probe

The fixture generates its own impulses, steps and two-tone signal in C#. It creates private
Lowpass and Highpass mixer assets through the Editor's mixer controller API. No recorded audio,
source WAV imports, microphone, default audio device changes or external test project is used.

`native.py` creates a new cache-only project for each capture. AudioRenderer holds audio in
recording mode and captures twelve complete 1024-frame stereo buffers per case. A master mixer-group sink is registered for each render and records the floats before
device conversion as well. The Editor gets
a frame boundary to submit newly created sources before each render. Captures include startup
silence, every channel and the entire tail, with no alignment or trimming. Mono and stereo inputs,
cutoff/resonance, effect and group bypass, effect order, parent routing, exposed SetFloat/ClearFloat
and an immediate snapshot change are covered. Other mixer effects are outside this fixture.

Run from the repository root, choosing a new cache directory for every run:

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/mixer-filters"
python3 tests/projects/mixerfilters/native.py --release 6000.0.71f1 --work "$work/native-a"
python3 tests/projects/mixerfilters/native.py --release 6000.0.71f1 --work "$work/native-b"
python3 tests/projects/mixerfilters/compare.py --release 6000.0.71f1 \
  --reference "$work/native-a/capture" --candidate "$work/native-b/capture" \
  --exact --report "$work/native-agreement.json"
```

Independent Native captures must agree and contain audible dry and filtered signals before
publishing a canonical reference under `Reference/unity-6000.0.71f1/rate-<rate>/`. A silent capture does not
establish filter behavior. Both raw repeats remain outside Git.

Use `--rate 44100` on captures and comparison for the separate sample-rate matrix. `engine.py` copies a Native project's private
mixer assets and runs the source-owned probe against a supplied matching CLI/managed/native
runtime. It flushes the normal managed audio update with zero elapsed time, then advances the
existing virtual audio frame by one complete DSP buffer. The ordinary listener OnAudioFilterRead
callback records the production mix. This avoids a new capture API or a native ABI change.

```bash
python3 tests/projects/mixerfilters/engine.py --release 6000.0.71f1 \
  --native-project "$work/native-a/project" --runtime "$work/runtime" --work "$work/engine"
python3 tests/projects/mixerfilters/compare.py --release 6000.0.71f1 \
  --candidate "$work/engine/capture" --report "$work/comparison.json"
```

The comparator fixes absolute tolerance at 0.00002 and relative tolerance at 0.0002 before any
engine candidate. It writes every missing, extra and failing sample to the cache-only report.
The Native repeat check uses zero tolerance. No full-engine audio parity claim follows from this
probe. Actual measurements and remaining differences must be reported with the delivery.
