# AudioMixer suspension probe

This independent fixture measures Unity 6000.0.71f1 Linux at 48 kHz and 44.1 kHz,
with stereo output and both mono and independent stereo sources. Each profile has
79 cases and complete 1024-frame blocks, including long silent gaps. Two fresh
private projects agree on all 497,027,311 compared values per profile, with exact
float bits, integers and metadata, before publication of the single canonical set.

Each case captures device PCM returned by AudioRenderer, listener callbacks and
the raw master-group sink. They are distinct observations. A suspended Native
mixer can leave the sink untouched, retaining its previous block, while the
device and listener receive silence. The fixture never clears that retained data.
Files ending in `.f32.gz` are lossless gzip containers for the complete little-endian
float stream named in `capture.json`. Packing checks every byte after decompression.
No block is removed, shifted, aligned or quantized by the fixture.

## Measured behavior

The serialized default enables suspension at a threshold of -80 dB. Disabled
suspension preserves a five-second Echo during a silent gap. Enabled suspension
can stop it before the delayed output reaches the mixer. A playing voice, including
a quiet voice or a voice in a sibling group, keeps the mixer awake. Echo and filter
history survive suspension and resume when a new voice wakes the mixer.

The `wake_hold` and `silent_voice_end` cases distinguish a frozen delay from lost
history and distinguish voice lifetime from the last nonzero source sample. At
48 kHz their delayed copies resume at frames 584064/585088 and 579968/580992.
The latter source plays four additional blocks of silence before ending. At
44.1 kHz the corresponding frames are 567640/568664 and 563544/564568.

Suspension is observed on audio updates and its waiting interval advances with
rendered DSP time. Burst rendering, updates without rendering, different
captureDeltaTime values and a 2.5-second wall-time gap distinguish those clocks.
At 48 kHz the short-source wake case freezes after 50 complete DSP blocks.
The 1023/1024/1025 ms delay controls bracket that observation boundary. These
measurements constrain a one-second wait after activity, observed on an update,
rather than a timer advanced independently by wall time or update count.

Constant Echo tails constrain the level test separately from gaps between pulses.
The -40 dB boundary distinguishes exact exponentiation from the existing mixer
gain approximation, after signed-16-bit Echo storage and stereo RMS. Names such
as `level_high` refer to the authored input: its decoded Echo value is still below
the observed boundary. Mono and single-channel controls cross that boundary.
The full amplitude, threshold, polarity and pulse matrices remain in the oracle.

The matrix also captures parent and sibling routing, mute, effect/group bypass,
Lowpass/Highpass tails, new voices, exposed gain/wet changes, zero-duration
snapshots, source disable/enable and audio reset. Parameter changes alone do not
produce delayed output from a suspended mixer in the measured cases.

Native reset is recorded without assuming one listener callback per Render.
Fresh-process captures deliver an additional complete listener block during the
reset sequence, and same-frame replay does not produce later output. These
observations are retained as part of the canonical data. Matching all reset
delivery behavior is separate from establishing mixer suspension.

Other releases, surround output, linked-mixer timing and timed parameter/snapshot
transitions are unmeasured. This fixture does not establish full audio parity.

## Reproduce

Both runners wrap only their child Editor or engine in the current installed
`~/.cache/compat/with-heavy-job`. Do not wrap either runner again. Native authoring
and capture use separate Editor processes so the capture reloads the saved assets.
Use `--editor` for a nondefault Editor installation. Runtime artifacts must be built
from matching sources, including the actual native libraries.

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/suspend-fresh"
python3 tests/projects/mixersuspend/native.py --release 6000.0.71f1 --rate 48000 --work "$work/native-a"
python3 tests/projects/mixersuspend/native.py --release 6000.0.71f1 --rate 48000 --work "$work/native-b"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 --exact \
  --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
python3 tests/projects/mixersuspend/engine.py --release 6000.0.71f1 --rate 48000 \
  --native-project "$work/native-a/project" --runtime "$work/runtime" --work "$work/engine"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 \
  --before "$work/before/capture" --candidate "$work/engine/capture" --report "$work/union.jsonl.gz"
python3 tests/projects/mixersuspend/deltas.py --release 6000.0.71f1 --rate 48000 \
  --candidate "$work/engine/capture" --report "$work/deltas.jsonl.gz"
python3 tests/projects/mixersuspend/test_compare.py
```

Repeat at 44100 Hz. The verifier checks finite values and complete streams against
the captured metadata. The comparator preserves every missing, extra and failing
value across Native, before and after. Float tolerances remain ATOL `2e-5` and
RTOL `2e-4`; metadata and Native repeat bits compare exactly. Numerical delta
reports retain every changed float, including differences inside those tolerances.
All repeats, candidates, unions, logs and rejected attempts belong in cache.
