# AudioMixer Echo probe

This fixture measures Unity 6000.0.71f1 Linux through the production AudioRenderer API.
Each rate has two independent Native captures before one canonical set is published in
`Reference/unity-6000.0.71f1/`. The matrix has 35 cases, each with complete stereo DSP blocks,
unchanged master sink floats, listener callback floats, and returned device PCM. No samples
are shifted, trimmed, quantized by the fixture, or discarded. Native and engine use fresh
private projects, never an authored game.

The probe mixers explicitly disable auto-suspend. Native's default suspension can suppress
long echoes during a silent gap, so it cannot establish the five-second delay response.
The non-decaying case runs last. Other cases retain enough complete buffers for their finite
tails to finish. Capture lengths range from 32 to 768 blocks of 1024 frames. Clips end after
8192 frames, allowing the effect to continue after a source ends.

## Measured behavior

| Parameter | Default | Bounds |
| --- | --- | --- |
| Delay | 500 ms | 1–5000 ms |
| Decay | 0 | 0–1 |
| Max channels | 0 | 0–16 |
| Drymix | 1 | 0–1 |
| Wetmix | 1 | 0–1 |

The impulse matrix constrains delay conversion to an integer frame count rounded up to a
multiple of eight. For example, 10.125 ms gives 488 frames at 48 kHz and 448 at 44.1 kHz.
The first delayed copy has unit gain before Wetmix; Decay scales subsequent feedback.
Drymix scales the current input and Wetmix scales the delayed output independently.
The delay history stores signed 16-bit samples. It decodes with multiplication by `1/32767`,
and truncates the combined input and feedback on storage. The tone tails distinguish this
from division and from separately truncating the input and feedback.

Mono and independent stereo inputs, silence, defaults, parameter endpoints, fractional delays,
effect/group bypass, serialized effect order with Lowpass, parent routing, summed sources and
attenuation are captured at both rates. Max channels values 0, 1, 2 and 16 have the same stereo
response. Echo groups retain their initial input fade even with the effect or group bypassed.
The exposed and zero-duration snapshot cases set parameters before playback, then check exact
GetFloat bits at the first render and compare independent serialized controls. They do not
measure changes during a running tail or timed snapshot transitions.

Global effect-slot wet mixing is distinct from Echo's Drymix and Wetmix and remains unsupported.
Other releases, surround output, mixer auto-suspend and timed parameter changes are unmeasured.
This fixture establishes only the listed Echo behavior, not full audio parity.

## Reproduce

Choose a fresh cache directory for every label. Both capture scripts wrap only their child
Editor or engine job in the installed `~/.cache/compat/with-heavy-job` launcher.

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/echo-fresh"
python3 tests/projects/mixerecho/native.py --release 6000.0.71f1 --rate 48000 --work "$work/native-a"
python3 tests/projects/mixerecho/native.py --release 6000.0.71f1 --rate 48000 --work "$work/native-b"
python3 tests/projects/mixerecho/compare.py --release 6000.0.71f1 --rate 48000 --exact \
  --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
python3 tests/projects/mixerecho/engine.py --release 6000.0.71f1 --rate 48000 \
  --native-project "$work/native-a/project" --runtime "$work/runtime" --work "$work/engine"
python3 tests/projects/mixerecho/compare.py --release 6000.0.71f1 --rate 48000 \
  --candidate "$work/engine/capture" --report "$work/comparison.jsonl.gz"
python3 tests/projects/mixerecho/compare.py --release 6000.0.71f1 --rate 48000 \
  --before "$work/before/capture" --candidate "$work/engine/capture" --report "$work/union.jsonl.gz"
```

Repeat with `--rate 44100`. Supply matching CLI/Core/native outputs in `runtime/`; build and
validate the actual native library. The verifier rejects incomplete, nonfinite and silent
purported effect captures, including missing long-delay repeats. Reports stream every missing,
extra and failing sample into gzip JSON lines, followed by a summary. Integer/metadata/bit
fields compare exactly. Float tolerances remain ATOL `2e-5` and RTOL `2e-4`; independent Native
repeat checks compare float bits exactly. Summary files also count bit differences inside the
float tolerances. All run projects, repeats, candidates, failures and reports stay in cache.
