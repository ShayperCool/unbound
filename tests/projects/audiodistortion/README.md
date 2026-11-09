# Distortion

Unity 6000.0.71f1 Linux Editor, offline AudioRenderer, stereo output, 1024-frame
buffers at 48000 and 44100 Hz. Each case records 32768 frames of device
PCM and the mixer's Master float sink. Two fresh private Editor projects per
rate must agree bit for bit before publishing one reference.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [Mixer Distortion](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionEffect.html) has Distortion 0..1, default 0.5. The Editor names the serialized parameter `Level`. |
| Documented, Unity 6000.0 | [AudioDistortionFilter](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html) applies to sources or the listener. [distortionLevel](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiodistortionfilter/distortionlevel) has range 0..1, default 0.5. Neither page specifies a formula or setter clamping. |
| Documented, Unity 6000.0 | [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) requires enabled components in Inspector order. [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) occupies its Inspector position, receives interleaved floats on the audio thread, and is unsupported on Web. |
| Measured in this release/context | Mixer and source share `y = (1+k)*x/(1+k*abs(x))`, `k = 2*d/(1-d)`, `d = min(clamp(Level,0,1),0.9999)`. No history or sample-rate dependence is visible. These equations describe the captured samples; the internal implementation is an inference. |
| Measured in this release/context | Component and exposed mixer setters retain -1 and 2, while their DSP clamps. Defaults, endpoints, intermediate levels, stereo ramps of both signs, small/large/DC/impulse/noise/tone, mono source, source/component/group/slot bypass, parent/prefader routing, serialized 0.37, and a halving callback before/after the source filter are captured. |
| Measured in this release/context | Source startup has the existing 64-frame input fade. An active mixer slot retains the group's 1024-frame input fade. `mix_clear` sets and clears an override before its first audio update, and its immediate getter reads the snapshot value. The [GetFloat contract](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audio/audiomixer/getfloat) describes returning to snapshot control; it does not specify synchronization. The [mixerparams controls](../mixerparams/README.md) reconcile this with lowpass and Distortion after an override has been applied: the immediate getter then retains the applied value until the audio update. DSP submission remains unchanged. |
| Unmeasured / unsupported | Timed parameter transitions, 3D sources, startup while bypassed, changing bypass during playback, and more than two channels. Listener scripts interleaved with components and a listener sharing its GameObject with a source fail clearly. Static slot Wet is supported on the measured stereo profile through +30 dB; its live changes and other layouts/buffers fail clearly. |

Record each Native repeat with `native.py --release 6000.0.71f1 --rate RATE
--work FRESH_CACHE_DIRECTORY`. It takes one heavy-job lease. Compare with
`compare.py --release 6000.0.71f1 --rate RATE --reference FIRST/capture
--candidate SECOND/capture --exact --report CACHE/repeat.jsonl.gz`.
Use `publish.py` only after exact agreement. Authored mixer/prefab/scene inputs
are recorded with `tests/authored_project.py record`; gate assembly verifies
their input hashes and never launches the Editor.

The engine gate is `python3 tests/projects/audiodistortion/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, with `UNBOUND` pointing at this checkout's
release build. The comparator retains every failing or missing/extra value,
using the existing audio float tolerances of 2e-5 absolute plus 2e-4 relative.
The strict report uses those bounds for everything; the gate separately admits
one device PCM step, never extra float-sink slack. All reports stay in the cache.

Static effect-slot Wet is now covered by the additional `slot_wet_*` cases. See the
[shared contract, measured equation, controls and limits](../../../docs/AUDIO.md#static-effect-slot-wet-mixing).
These additive captures retain all earlier oracle values and comparator tolerances.

The additional listener controls cover listener Distortion and Chorus,
both orders with LowPass, HighPass, Echo and Reverb, final-mix placement, two
summed sources, disabled filters and both source bypass flags with and without
mixer routing. See the [contract, observations and limits](../../../docs/AUDIO.md#audiolistener-component-chains).
Together with 13 Echo delay/source controls, two independent captures agree
exactly on 13369637 static values per rate; the earlier
8782034 values per rate are unchanged. `publish.py --extend` checks every earlier
metadata value and sample before it adds controls.

The separate `--profile spatial` records 226 mono/stereo placement controls, each
with 32768 frames, under `Reference/unity-6000.0.71f1/spatial-rate-RATE`.
Record two independent runs with `native.py --profile spatial`; verify their
metadata and every raw sample are identical, then publish one with
`publish.py --profile spatial`. The static and spatial profiles share the
Editor-authored assets recorded by the default authoring command.

All 226 spatial controls now pass at both rates, including the original 42-case
matrix and 3D Distortion/Chorus placements. The components are enabled for the
measured release. The gate compares every placement and rejects unexpected source
diagnostics. Full strict reports retain all one-step device PCM flips; Master
float tolerances are unchanged. See the [measured spatial rules](../../../docs/AUDIO.md#3d-component-placement).
All prior static values and comparator tolerances remain unchanged.

The additional dry controls measure logarithmic/linear/custom rolloff, min/max
boundaries, partial blend, normalized multi-key CustomRolloff/SpatialBlend/Spread
curves, listener rotation, spread, stereo pan, 64-sample gain transitions, source
and listener motion, direct Rigidbody velocity, non-integer pitch and clip rates,
and extreme Doppler. `spatial-factor0` and `spatial-factor2` repeat 18 moving
controls per rate with the corresponding project Doppler Factor. Record/publish
these profiles by the same independent-repeat procedure. The gate runs every
profile and requires every spatial output, including both original zone cases,
to pass. Zone sends precede source components and omit each naturally ending
voice's final DSP buffer. The separate audioreverb lifecycle profile retains the
unsupported activation of zones during existing playback, with explicit
diagnostics and an unchanged dry return.

Four additional Loop-wrap controls evaluate CustomRolloff, SpatialBlend, Spread
and ReverbZoneMix at twice maxDistance. On 6000.0.71f1 they return the first
curve value (0.8), rather than holding the final value (0.2). The reverb case
uses a long clip to isolate curve evaluation from source completion. All prior
oracle samples remain unchanged.

Static spatial Distortion/Chorus combined with source OnAudioFilterRead scripts is
measured in [SCRIPT_MEASUREMENT.md](SCRIPT_MEASUREMENT.md). Live scripted clip
reconnection remains unsupported and doctor-listed; its full raw comparisons remain visible.
