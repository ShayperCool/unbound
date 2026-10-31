# Mixer filter PCM probe

The fixture generates its own impulses, steps and two-tone signal in C#. It creates private
Lowpass and Highpass mixer assets using the Editor mixer controller API. No recorded audio,
microphone, default audio device changes or external test project is used.

Each run creates a fresh cache-only project. The matrix captures twelve complete 1024-frame
stereo DSP buffers per case, including startup silence and the entire tail. It records three
observation points: the master group sink, the listener callback, and AudioRenderer device PCM.
The master sink is registered for every render. No buffers are aligned, shifted or trimmed.
The synchronous engine capture submits queued source and mixer changes through AudioSystem at
zero elapsed time, after each render, matching the measured coroutine submission order. The
production AudioRenderer owns DSP stepping. The API profile runs before these submissions and
checks the capture-duration properties and sample demand separately.

Run from the repository root, choosing a new cache directory for each run:

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/mixer-filters"
python3 tests/projects/mixerfilters/native.py --release 6000.0.71f1 --listener --work "$work/native-a"
python3 tests/projects/mixerfilters/native.py --release 6000.0.71f1 --listener --work "$work/native-b"
python3 tests/projects/mixerfilters/compare.py --release 6000.0.71f1 \
  --reference "$work/native-a/capture" --candidate "$work/native-b/capture" \
  --exact --report "$work/native-agreement.json"
python3 tests/projects/mixerfilters/engine.py --release 6000.0.71f1 --renderer --listener \
  --native-project "$work/native-a/project" --runtime "$work/runtime" --work "$work/engine"
python3 tests/projects/mixerfilters/compare.py --release 6000.0.71f1 \
  --candidate "$work/engine/capture" --report "$work/comparison.json"
```

Supply a matching CLI, managed engine and native shim in `runtime/`. Both capture drivers wrap
only their child Editor/engine command in the currently installed heavy-job launcher. Use
`--rate 44100` on Native, engine and comparison for the second matrix rate. Independent Native
repeats must agree before publishing a canonical reference. Silent signals do not establish DSP
behavior; both raw repeats remain outside Git.

The production AudioRenderer renders the normal mixer graph. Its group sinks and listener
callback retain floats. Returned device PCM uses the measured signed-16-bit conversion: saturation
and truncation toward zero in units of 1/32768. This conversion lives in the production capture
path. The fixture writes the returned samples unchanged. Native API captures also retain the
untouched suffix when a request is shorter than a complete DSP buffer, lifecycle results, pending
sample counts, and capture-duration/FPS setter/getter round trips. Float properties are recorded
as IEEE-754 bits so text formatting does not affect the comparison.

The earlier `engine.py` mode without `--renderer` remains available for a baseline whose
AudioRenderer API is absent. It records the listener float proxy and flushes the audio update
with zero elapsed time before advancing the virtual device by one buffer. Historical proxy
captures and their full main-buffer failures remain in cache and in the before/after unions.
Use `--renderer --listener` to compare the actual matching device and float observation points.

## Measured profiles

The single canonical reference set is `Reference/unity-6000.0.71f1/`. Every profile keeps complete
buffers and metadata. Additional profiles are measured at 48000 Hz.

| Profile | Native / engine option | Comparator option | Scope |
| --- | --- | --- | --- |
| `rate-48000`, `rate-44100` | `--listener` and engine `--renderer` | default; select `--rate` | Mono/stereo clips, cutoff/resonance, bypass, effect order, parent routing, summed voices, exposed Set/Clear and immediate snapshot |
| `startup` | `--startup` and engine `--renderer` | `--profile startup` | Step and 134 independent impulses at known startup frames |
| `renderer-api` | `--api` and engine `--renderer` | `--profile renderer-api` | Lifecycle, complete/partial buffer writes, sample demand, duration/FPS round trips |
| `renderer-pcm` | `--pcm --listener` and engine `--renderer` | `--profile renderer-pcm` | Positive/negative fractional, full-scale and saturated PCM |
| `gain` | `--gain --listener` and engine `--renderer` | `--profile gain` | Mixer attenuation from -80 to +20 dB |
| `callbacks` | `--callbacks --listener` and engine `--renderer` | `--profile callbacks` | Source callback gain before Lowpass and listener callback gain after the master sink |

Use the matching private Native project for each engine profile. `--diagnostic-input` separately
captures the input subgroup for the step and tone; both Native repeats and all diagnostic PCM stay
in cache. It is intended to locate a regression, rather than to replace the complete matrix.

The comparator fixes absolute tolerance at 0.00002 and relative tolerance at 0.0002, chosen before
any engine candidate. Native repeat checks use zero tolerance. Every missing, extra and failing
sample is retained. `union.py --release 6000.0.71f1 --rate 48000 --before <capture> --after <capture>
--report <cache.json>` records each failing Native/before/after value as introduced, retained or
resolved. The same `--profile` selections apply to unions. Earlier and rejected reports remain
outside Git; no tolerance changes or sample masks are used.

## DSP behavior and limits

Lowpass has the measured fourth-order Butterworth response, including parameter-dependent delay
normalization during startup. Highpass has a second-order response. Both preserve channel history
and tails after a source ends. Defaults are 5000 Hz and resonance 1; parameter bounds are
10–22000 Hz and 1–10. The mono startup gain matrix accumulates its 64-sample ramp, and the group
input fade and parameter slew remain in the full recordings. The gain profile constrains the
mixer's quadratic exponent approximation independently of its exposed dB values.

Exposed overrides apply immediately to GetFloat. Clearing an override and immediate snapshot
changes become visible at the next audio update. The coroutine renders before that submission;
the engine capture preserves the same order. The narrow Time dependency retains the requested
float duration independently of the FPS view and uses the existing rational-time conversion for
its public view and capture stepping. Other Time and unscaled-clock behavior is outside this change.

Group routing and effects use the existing audio RPC route. Sources are summed before serialized
effects, then passed to their parent. Optional group observation uses callback kind 4;
NativeApi12/render5/shim9 and existing callback kinds 0–3 remain unchanged. Active unsupported
effects and wet mixing fail clearly. Other mixer effects and spatializer processing remain separate
work. Only Unity 6000.0.71f1 Linux, mono/stereo input and stereo output at the two matrix rates are
measured. Other releases, surround output and timed snapshot transitions are unmeasured.

`regress.py` runs private source-matched audio-import, physics and graphics gates, preserving their
existing tolerances and every failing field, including lossy-asset samples. `--reuse-before` reads
an immutable baseline gate directory. Complete current reports and artifact hashes are required
for a delivery; these profiles do not establish full-engine parity.
