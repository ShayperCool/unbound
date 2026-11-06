# AudioMixer Compressor, ParamEQ, Lowpass Simple and Highpass Simple probe

The fixture measures four AudioMixer effects of Unity 6000.0.71f1 on Linux through the production
AudioRenderer API. It generates its signals in C# and creates private mixer assets with the Editor
mixer controller API; no recorded audio, external project or default audio device is used. Every
case has its own mixer with auto-suspend disabled: Master with one child, Music, whose source
plays the case's signal through `Attenuation, Compressor` by default (other chains are named per
case). Each case records 32 complete 1024-frame stereo DSP blocks of the master group sink floats
(`<case>.master.f32`) and the returned device PCM (`<case>.f32`). No samples are shifted, trimmed
or discarded.

The 65 cases (`Cases` in `Assets/Scripts/EffectProbe.cs`) cover:

- Compressor: 0..2 ramps for the static curve at Threshold 0 (default), -20 and -40 dB and with
  Make up gain; steps from 0.05 to 0.5/1 for Attack 10/50/200 ms and Release 20/50/1000 ms;
  unset parameters; 1 kHz and 100 Hz tones; one loud channel; noise; Threshold -60 dB; Make up gain
  30 dB; pre-fader placement; effect and group bypass; startup with and without bypass.
- ParamEQ: impulses at the default, boost/cut at 1 kHz, narrow (0.2 octave), wide (5 octaves),
  20 Hz and 22000 Hz centers, gain only; a 20 Hz..20 kHz sweep; bypass; startup with and without
  group bypass.
- Lowpass Simple and Highpass Simple: impulses at the default cutoffs and a ladder from 10 Hz to
  22000 Hz, noise, bypass, startup.

## Reproduce

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/mixer-compressor"
for rate in 48000 44100; do
  python3 tests/projects/mixercompressor/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-a"
  python3 tests/projects/mixercompressor/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-b"
  python3 tests/projects/mixercompressor/compare.py --release 6000.0.71f1 --rate $rate \
    --reference "$work/native-$rate-a/capture" --candidate "$work/native-$rate-b/capture" \
    --report "$work/agreement-$rate.jsonl.gz"
  # Only for a new canonical reference: publish.py refuses to overwrite one.
  # python3 tests/projects/mixercompressor/publish.py --release 6000.0.71f1 --rate $rate --capture "$work/native-$rate-a/capture"
done
python3 tests/projects/mixercompressor/run.py --release 6000.0.71f1 --unbound "$UNBOUND"
```

`run.py` is the gate (declared in `gate.json`). It assembles the committed Editor-authored set
`Authored/unity-6000.0.71f1/default` (tests/authored_project.py, recorded from the 48000 Hz
reference capture's project), so no Editor starts; `--native` authors a fresh private Native
project per rate instead. When the fixture changes, record such a project again. It copies
`$UNBOUND` and its `managed/` outputs into a runtime directory, captures the engine at both rates,
writes a strict report and exits with the result of the gate report. The gate admits one 1/32768
step of device PCM and, for the keys in `common.RESIDUALS` only, the recorded ParamEQ rounding
residual: absolute 4e-4 for `eq_startup` (master and PCM) and the PCM of `eq_startup_group_bypass`,
3e-5 for the master floats of `eq_sweep`. `engine.py` and `compare.py` run single steps;
`--before` produces a before/after union. Reports stream every missing, extra and failing value into gzip JSON lines,
followed by a summary that counts failures per case.

The tolerances are ATOL `2e-5` and RTOL `2e-4` on floats, as in the other mixer probes, and exact
on metadata. Independent Native repeats agree within these tolerances at both rates
(8388738/8388738 values each). Bit for bit they differ only in ParamEQ's anti-denormal offset
(about 1e-20, whose sign alternates in a phase that varies between runs) and values of 1e-16 or
less after it; every Compressor and simple filter value is identical. The canonical reference is
one capture per rate in `Reference/unity-6000.0.71f1/rate-<rate>/`, with gzip sample files. Both
raw repeats stay in the cache.

## Result

Gate: 8388738/8388738 values at 48000 Hz and at 44100 Hz. The strict comparison keeps one-step
device PCM flips and the ParamEQ residual (`eq_startup`, four master floats of `eq_sweep`, and the
device PCM of `eq_startup_group_bypass`, which still carries the previous case's ringing tail); they are listed
in docs/COMPATIBILITY_GAPS.md. The measured model and its limits are in docs/AUDIO.md.
