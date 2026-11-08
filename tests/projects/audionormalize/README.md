# Mixer Normalize

Unity 6000.0.71f1 Linux Editor, offline AudioRenderer, stereo output with
1024-frame DSP buffers, at 48000 and 44100 Hz. Mono cases use a mono clip
routed to stereo output. Fresh mixer instances start without prior audio;
eight silent buffers drain previous mixers. Every frame, including startup,
is recorded at the Master float sink and the device PCM sink.

| Basis | Contract or observation |
| --- | --- |
| Documented purpose and parameters, Unity 6000.0 | The [Normalize Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioNormalizeEffect.html) describes gain that brings average or peak amplitude to a target. Fade in time is 0..20000 ms, default 5000; Lowest volume is 0..1, default 0.1; Maximum amp is 0..100000, default 20. It does not define envelope arithmetic, channel linking, initialization or timing. |
| Documented placement and limitations, Unity 6000.0 | The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) defines ordered effect slots, complete slot bypass and optional wet mixing that adds a dry channel around the effect. [AudioMixer.SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) controls exposed parameters, taking control from snapshots, and returns false for missing parameters or snapshots being edited. It warns against calls in Awake, OnEnable or AfterSceneLoad. Native setters here run from Start. There is no Normalize component API. |
| Measured definitions and API | The installed Editor reports the documented three names, ranges and defaults. Exposed SetFloat/GetFloat keep out-of-range values; DSP output matches clamping to the reported ranges. Explicit defaults match omitted values. Static group/slot bypass remove the effect, while source bypass leaves the routed mixer effect active. |
| Measured practical profile | Impulses, positive/negative/tiny/large DC, deterministic noise, amplitude ramps, tones and amplitude steps in either or both channels; mono and stereo clips; defaults, individual controls, combined fast response, and routing the effect on Master. Default noise, DC, tone and ramp runs exceed ten seconds to include the 5-second envelope decay. |
| Algorithm inference constrained by captures | A linked absolute peak starts at 1. Each frame subtracts `1 / (fade_ms * rate / 1000)`, or 1 when fade is zero, then takes the maximum with Lowest volume and both input magnitudes. Output gain is `min(Maximum amp, 1 / peak)`. Floating operations retain single precision and their order. This is release-specific evidence, not a universal Unity contract. |
| Unmeasured / unsupported | Other output layouts, rates and DSP-buffer sizes, timed parameter/snapshot/bypass changes, effect insertion/removal/reordering during rendering. Static slot Wet is supported on the measured stereo profile through +30 dB. Active unmeasured rate/buffer settings and live Normalize changes fail clearly. Pitch Shifter has its own probe. |

`native.py --release 6000.0.71f1 --rate RATE --work FRESH_CACHE_DIRECTORY`
records one independent Native project under one heavy-job lease. Compare two
captures using `compare.py --release 6000.0.71f1 --rate RATE --reference
FIRST/capture --candidate SECOND/capture --exact --report CACHE/repeat.jsonl.gz`.
Publish one canonical copy with `publish.py` only after exact agreement. Preserve
both Native captures in the cache. `tests/authored_project.py record` commits
normalized mixer/scene inputs, with hashes checked during gate assembly.

The engine-only gate is `python3 tests/projects/audionormalize/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, using the checkout's release build via UNBOUND.
It never starts the Editor. Reports keep every missing, extra and failing value.
The unchanged audio tolerances are 2e-5 absolute plus 2e-4 relative. A separate
gate admits one 1/32768 device PCM step, with no additional float-sink slack.
RESIDUALS is empty. All run artifacts stay outside Git in the cache.

Certification: both independent Native repeats agree exactly across 36,831,499
values at each rate. The engine strict and separate gate comparisons each pass
36,831,499/36,831,499 at both rates, including all metadata and startup samples.
There are zero raw float/PCM failures and no added residuals.

Static effect-slot Wet is now covered by the additional `slot_wet_*` cases. See the
[shared contract, measured equation, controls and limits](../../../docs/AUDIO.md#static-effect-slot-wet-mixing).
These additive captures retain all earlier oracle values and comparator tolerances.
