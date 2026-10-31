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

## Measured scope and remaining differences

Unity 6000.0.71f1 on Linux produced independently identical recordings at 48000 and 44100 Hz,
1,179,674 values per profile, including the complete main and master buffers and parameter records.
The capture matrix uses mono/stereo source clips and stereo output. Other Unity releases and
surround output are unmeasured.

Lowpass has a fourth-order Butterworth steady response. Highpass has a second-order response.
The measured defaults are 5000 Hz and resonance 1, with bounds 10–22000 Hz and 1–10.
Exposed overrides apply immediately to GetFloat; clearing them and immediate snapshot changes
become visible on the next audio update. The capture intentionally renders before submitting that
update, matching its Unity coroutine phase.

The engine sends group routing and effect parameters through the existing audio RPC path.
A group is summed before its effects, with persistent independent channel histories. The optional
group output observer records its actual processed buffer before routing it into its parent.
Callback kind 4 is opt-in; the ABI 5 table/signature and existing kinds 0–3 are unchanged.
Active unsupported effects and wet mixing throw clear errors. Other mixer effects and spatializer
processing are outside this implementation.

The complete Native highpass tone startup and dry step are checked by Rust tests. Lowpass's
initial time-varying state still has retained differences in the first buffer; the complete
comparison reports are authoritative. No samples are trimmed or aligned to remove them.

The main outputs use different observation points: Unity AudioRenderer delivers device-converted
PCM, quantized in steps of 1/32768, while the engine listener callback provides floats before
device conversion. The engine does not quantize its recorded listener buffer to make those
comparisons pass. Master group taps compare matching points. Main-buffer failures remain in the
reports, including introduced fields whose prior zero output happened to match Unity's quantized
zero. This fixture does not implement or certify Unbound's public AudioRenderer API.

`union.py --release 6000.0.71f1 --rate 48000 --before <capture> --after <capture> --report <cache.json>`
keeps every Native/before/after failing value and classifies it as retained, resolved or introduced.
`regress.py` runs private source-matched audio-import, physics and graphics gates and retains all
fields, including the legacy audio probe's lossy-asset sample differences. It preserves the existing
numeric tolerances. `--reuse-before` reads an existing immutable baseline gate directory.
