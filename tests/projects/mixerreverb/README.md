# AudioMixer SFX Reverb probe

The fixture measures the SFX Reverb mixer effect of Unity 6000.0.71f1 on Linux through the
production AudioRenderer API. It generates its own impulses, tone and silence in C#, and it creates
private mixer assets with the Editor mixer controller API. No recorded audio, external project or
default audio device is used. Every case has its own mixer with auto-suspend disabled: a Child group
with one SFX Reverb, unless the case name says otherwise. Each case records 24 complete 1024-frame
stereo DSP blocks of two observation points: the master group sink floats (`<case>.master.f32`) and
the returned device PCM (`<case>.f32`). Device PCM also carries the still-ringing mixers of earlier
cases, as in Native. No samples are shifted, trimmed or discarded.

The matrix covers 48 cases:

- Steady-state signals begin after the first DSP block. They include separate left and right
  impulses and a simultaneous pair that tests the mono sum.
- Cases cover the Editor defaults, every parameter including both range ends, and an
  all-parameter preset.
- Wet levels 0, -6, -68 and -70 dB cover the drop threshold.
- Effect and group bypass, effect order with Lowpass, parent routing, summed sources and group
  volume are captured.
- Exposed `Room`/`Decay Time` are set before the first render.
- Startup cases put impulses inside the first block. Two of them run with the effect or the group
  bypassed.

## Reproduce

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/mixer-reverb"
for rate in 48000 44100; do
  python3 tests/projects/mixerreverb/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-a"
  python3 tests/projects/mixerreverb/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-b"
  python3 tests/projects/mixerreverb/compare.py --release 6000.0.71f1 --rate $rate --exact \
    --reference "$work/native-$rate-a/capture" --candidate "$work/native-$rate-b/capture" \
    --report "$work/agreement-$rate.jsonl.gz"
  # Only for a new canonical reference: publish.py refuses to overwrite one.
  # python3 tests/projects/mixerreverb/publish.py --release 6000.0.71f1 --rate $rate --capture "$work/native-$rate-a/capture"
done
python3 tests/projects/mixerreverb/run.py --release 6000.0.71f1 --unbound "$UNBOUND"
```

`run.py` is the gate. It creates fresh private Native projects, copies `$UNBOUND` and its `managed/`
outputs into a runtime directory, and captures the engine at both rates. It writes the strict
all-case reports and exits with the `gate` profile result. That profile covers every steady case,
plus the `startup*` cases before the first reverb arrival of their in-block impulses: frame
`floor(0.02 * rate)`, 960 or 882. `engine.py` and `compare.py` run single
steps. `compare.py --profile {all,steady,startup,gate}` selects cases. `--before` produces a
before/after union. Reports stream every missing, extra and failing value into gzip JSON lines,
followed by a summary that counts failures per case.

The tolerances are ATOL `2e-5` and RTOL `2e-4` on floats, as in the other mixer probes, and exact
on metadata. Independent Native repeats compare float bits exactly, and they agree at both rates
(4718692/4718692 values each). `--pcm-steps 1` admits one 1/32768 step on device PCM only. That
PCM is the truncation of master floats, which are compared within the tolerances above. The
canonical reference is one capture per rate in `Reference/unity-6000.0.71f1/rate-<rate>/`, with
gzip sample files. Both raw repeats stay in the cache.

## Result

Gate profile: 4529764/4529764 values at 48 kHz and 4529140/4529140 at 44.1 kHz. The strict all-case comparison keeps two
residuals, both listed in docs/COMPATIBILITY_GAPS.md:

- One-step device PCM flips: 165 values at 48 kHz and 234 at 44.1 kHz. The master floats of these
  samples pass; the largest float error is `4.54e-7`.
- `startup`/`startup_wet`: reverb arrivals of impulses inside the first block, up to `3.66e-4` at
  48 kHz and, at 44.1 kHz, `6.77e-4` in the master floats and `7.02e-4` in device PCM. Native spreads each
  over two frames by a fraction that shrinks towards frame 1024. The engine has no such smoothing.

The model and its measured rules are in docs/AUDIO.md. Unmeasured: other releases, surround
output, parameter changes while a tail runs, timed snapshot transitions, wet levels between -68 and
-70 dB, and sample rates other than 48000 and 44100 Hz.
