# AudioReverbFilter and AudioReverbZone probe

The fixture measures `AudioReverbFilter` (on an AudioSource and on the AudioListener) and
`AudioReverbZone` in Unity 6000.0.71f1 on Linux through the production AudioRenderer API. It
generates its signals in C#: impulses on the left (frame 2048), the right (3072) and both channels
(5120) of a 12288-frame clip. The Editor authors one effect-free mixer, `Probe`, and a prefab of
serialized filters and zones. No recorded audio, external project or default audio device is used.
Each case records 24 complete 1024-frame stereo DSP blocks at two points: the Probe mixer's
master sink floats (`<case>.master.f32`) and the returned device PCM (`<case>.f32`). Filter cases
play into the mixer's Child group. Zone cases play to the default output, so their master sink is
silent. No samples are shifted, trimmed or discarded.

`capture.json` also holds the property tables. Enums and ints are recorded as integers, floats as
their IEEE bits:

- the defaults of a new filter and zone;
- every preset in enum order;
- a set while a preset is selected, followed by switching to User;
- custom values followed by a preset;
- out-of-range sets on fresh components;
- the serialized prefab as the engine reads it back;
- each case's component values.

Cases: no filter, a new filter, six presets and Off, User with every parameter, User extremes,
sets on a preset, a preset then User, a mono clip, a disabled filter, `bypassEffects`, volume 0.5,
a clip that ends mid-block (the tail cut), a listener filter with and without
`bypassListenerEffects`. Zone cases: the listener at 0, 11, 12.5, 14 and 20 from a Generic zone
(10/15), `reverbZoneMix` 0.5 and 0, User and Cave zones, and two overlapping zones.

## Reproduce

```bash
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/audio-reverb"
for rate in 48000 44100; do
  python3 tests/projects/audioreverb/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-a"
  python3 tests/projects/audioreverb/native.py --release 6000.0.71f1 --rate $rate --work "$work/native-$rate-b"
  python3 tests/projects/audioreverb/compare.py --release 6000.0.71f1 --rate $rate --exact \
    --reference "$work/native-$rate-a/capture" --candidate "$work/native-$rate-b/capture" \
    --report "$work/agreement-$rate.jsonl.gz"
  # Only for a new canonical reference: publish.py refuses to overwrite one.
  # python3 tests/projects/audioreverb/publish.py --release 6000.0.71f1 --rate $rate --capture "$work/native-$rate-a/capture"
done
python3 tests/projects/audioreverb/run.py --release 6000.0.71f1 --unbound "$UNBOUND"
```

`run.py` is the gate. It assembles the committed Editor-authored set
`Authored/unity-6000.0.71f1/default/` (`tests/authored_project.py`), so no Editor starts. It
captures the engine at both rates and writes the strict `all` reports. Its exit status is the
`gate` profile's result. `compare.py --profile` picks what is compared:

- `filter`: every case without a zone, plus the filter property tables;
- `zone`: the zone cases and zone tables;
- `gate`: `filter` plus the zone cases listed in `common.GATE_ZONE` (all of them) and the zone tables;
- `all`: everything.

The set was recorded from the 48 kHz Native project of the published capture. The authoring reads
no rate. When a fixture file or `native.py` changes, record a fresh Native project again:

```bash
python3 tests/authored_project.py record audioreverb default --release 6000.0.71f1 --project "$work/native-48000-a/project" \
  --command 'python3 tests/projects/audioreverb/native.py --rate 48000 --release 6000.0.71f1 --work <new cache directory>'
```

Tolerances are as in mixerreverb: ATOL `2e-5` and RTOL `2e-4` on floats, exact on metadata, and
`--pcm-steps 1` admits one 1/32768 step on device PCM only. The independent Native repeats agree
bit for bit at both rates (3047646/3047646 values each). The canonical reference is one capture
per rate in `Reference/unity-6000.0.71f1/rate-<rate>/`, with gzip sample files.

## Result

Gate: 3047646/3047646 values at 48 kHz and 3047646/3047646 at 44.1 kHz, every case and table.
The strict all-case report keeps only one-step device PCM flips (ledgered in
docs/COMPATIBILITY_GAPS.md): 53 at 48 kHz (`underwater` 6, `user_all` 4, `zone_11` 19,
`zone_12_5` 21, `zone_overlap_blend` 3) and 32 at 44.1 kHz (`default` 1, `user_all` 4, `zone_11`
23, `zone_12_5` 3, `zone_overlap_blend` 1). The filter cases' master floats pass; the zone cases
have no master floats.

The measured rules are in docs/AUDIO.md. Unmeasured: other releases, surround output,
parameter changes while a tail runs, values outside the documented ranges, unrouted sources with
`bypassListenerEffects`, listener reverb filters next to OnAudioFilterRead scripts, and other
sample rates.

Eight added `clip_end_*` cases retain the exact one-buffer tail cutoff on both
sides of DSP boundaries at both rates. The gate compares 3834102 values per rate;
every earlier oracle value is retained. Two independent repeats agree exactly.
NativeApi 33 sends `Compat.AudioComponentTailMeasured`; partial device periods
use the actual last voice's ending sample. Rust tests check complete output at
100/255/257/441/1000/1300-frame periods, including one-shots outliving the main voice.

Forty additional zone-send controls bring the gate to 90 cases and 8847759 values
per rate. They cover mono/stereo, 2D/3D, routed/unrouted sources, normalized
three-key reverbZoneMix curves at/between their keys, impulse/noise signals and
clip/setter order. Two fresh Editor captures per rate agree bit for bit; all
previous oracle samples and tolerances are retained. These captures configure
zones before rendering and stop before long sources finish. The separate
[audiodistortion spatial profile](../audiodistortion/README.md) retains unresolved
first-activation and source-completion send differences.

The additional lifecycle, curve-key and overlapping-voice controls bring the
reference to 136 cases and 13369927 values per rate. They retain every earlier
metadata value and sample. Two independent captures agree bit for bit at each
rate. Reset controls render one silent block before creating their source to
establish the restarted clock; every subsequent case buffer is retained.
The cases cover cold/warm zones, enabling or creating a zone during playback,
explicit Stop, natural completion, per-voice sends with an overlapping one-shot,
and source Distortion/Chorus. All four custom curve types record both the caller's
keys and GetCustomCurve's returned keys, including negative, oversized, short,
unsorted, weighted and single-key inputs.

The `--profile motion` reference separately records 30 source/listener controls
on the ordinary player loop. Each endpoint runs Auto, Fixed and Dynamic modes
with its own Rigidbody, a parent Rigidbody, or transform motion in FixedUpdate
or Update. Opposing Rigidbody velocity writes in FixedUpdate and Update expose
the phase that supplies the source's Doppler sample. Flat custom rolloff isolates
velocity from distance attenuation. The two independent repeats agree exactly
on 2949182 values per rate, with 24 complete 1024-frame stereo buffers per case.
Record and publish this profile with `native.py --profile motion` and
`publish.py --profile motion`; `motion.py --exact --reference FIRST/capture
--candidate SECOND/capture` checks independent agreement. Each command also
requires the explicit release, rate and a cache output path as above.
