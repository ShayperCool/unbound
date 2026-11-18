# Low-pass and high-pass filters on an AudioSource

Unity 6000.0.71f1 Linux Editor, offline AudioRenderer, stereo output, 1024-frame DSP buffers at
48000 and 44100 Hz. Each case plays one clip (a logarithmic sweep from 20 Hz to 20 kHz, or two
impulses) through one AudioSource routed to the `Music` group of the probe's mixer (attenuation
only), with the filter components the case names, and records 32768 frames (65536 for
`curve_moving`) of device PCM and the mixer's Master float sink, with the filters' getters after
the case is set up and the errors logged meanwhile. Eight silent DSP buffers drain the mixer
between cases. Two fresh private Native projects per rate agree bit for bit; one is published.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | The [Audio Low Pass Filter](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/filters/class-audio-low-pass-filter) has Cutoff Frequency "range 0.0 to 22000.0, default = 5000.0" and Lowpass Resonance Q "range 1.0 to 10.0, default = 1.0", and "a Rolloff curve associated with it, making it possible to set Cutoff Frequency over distance between the AudioSource and the AudioListener". The [Scripting API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiolowpassfilter): `customCutoffCurve` "Returns or sets the current custom frequency cutoff curve." [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html): enabled filters apply in Inspector order. None of the pages says how the curve maps to a cutoff. |
| Measured in this release/context | The low-pass cutoff is that curve: each key a cutoff stored as (cutoff - 10) / 21990 clamped to 0..1, read back as 10 below zero and `min(1, key) * 21990 + 10` above; each key time a distance over the source's maxDistance. `cutoffFrequency` reads the first key and sets a curve of one key at time 0 (`low_c_*`, `curve_cutoff`: 2000 reads 2000.00012). A new filter's key is 5000 / 22000, so its cutoff reads 5007.72754, not the documented 5000 (`low_default`). |
| Measured in this release/context | A curve of more than one key sets the cutoff at the listener's distance over maxDistance at every source update, for a 2D source as for a 3D one, with logarithmic or linear rolloff (`curve_2d`, `curve_3d_log`, `curve_3d_linear`), held at its ends past them (`curve_far`, `curve_near`), and followed while the source moves (`curve_moving`). A curve of one key is its value anywhere (`curve_one`). The AudioListener's filter reads the first key (`curve_listener`). |
| Measured in this release/context | `customCutoffCurve` returns a copy; a set curve is copied, its times and tangents scaled to end at one when it ends past one (`curve_long`). A curve without keys logs "AudioLowPassFilter.customCutoffCurve must be passed an AnimationCurve with at least 1 element." and changes nothing (`curve_empty`). Setting null crashes the Editor, so it is not measured; Unbound throws `ArgumentNullException`. |
| Measured in this release/context | The resonance setters and the high-pass cutoff setter keep what they are given (5, 30000, 0.5 and 20 read back); the DSP clamps the cutoff to 10..22000 and the resonance to 1..10 (`low_c_below`, `low_c_above`, `low_q_*`, `high_c_*`, `high_q_*`). Order with a nonlinear filter (`chain_low_dist`, `chain_dist_low`), a band of both kinds, a filter disabled and enabled again while playing (`low_toggle`) and a cutoff set while playing (`low_live`) are recorded. |
| Measured in this release/context | Serialized: the low-pass filter keeps `m_LowpassResonanceQ` and `lowpassLevelCustomCurve` (no cutoff field). A filter of serialized version 2 or older keeps its curve's key times in world units, read back over the source's maxDistance when it loads (`serialized_legacy`: the prefab of `serialized_low` written as version 2, times in units of maxDistance 10). The high-pass filter keeps `m_CutoffFrequency` and `m_HighpassResonanceQ`. |
| Unmeasured | Other output layouts and buffer sizes, Doppler with a moving source (the 3D cases play a mono clip with Doppler off: the sweep's channels are opposite and a 3D source mixes them), a curve with other wrap modes, the listener's filter at a distance or with a first key at another time than 0, a NaN cutoff (Unbound sets the highest key), serialized version 1 or none (Unbound reads them as version 2), a serialized curve without keys (Unbound reads the default curve), an old file's `m_CutoffFrequency` (not read, as the curve is the cutoff), and when an older version's curve is scaled (Unbound scales it at its first use, by the source's maxDistance then). Other releases than 6000.0.71f1 keep a cutoff clamped on set and refuse a custom cutoff curve (`Compat.AudioLowPassCurveMeasured`). |

The engine reaches each block's changes before the block renders, as the Editor's frame before
each block does (the probe's engine path flushes the audio update there).

Record each Native repeat with `native.py --release 6000.0.71f1 --rate RATE --work
FRESH_CACHE_DIRECTORY` (one heavy-job lease). Compare with `compare.py --release 6000.0.71f1
--rate RATE --reference FIRST/capture --candidate SECOND/capture --exact --report
CACHE/repeat.jsonl.gz`; publish one with `publish.py` only after exact agreement. The Editor's
mixer and prefabs are recorded with `tests/authored_project.py record`; gate assembly verifies
their input hashes and never starts the Editor.

The engine gate is `python3 tests/projects/audiolowpass/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, with `UNBOUND` pointing to this checkout's release build. Every
missing, extra or failing value stays in the compressed report. Float tolerances are the existing
audio bounds, 2e-5 absolute plus 2e-4 relative; the strict report applies them to every sample,
and the gate admits one 1/32768 device PCM step (never of master floats). All run artifacts stay in
the cache.
