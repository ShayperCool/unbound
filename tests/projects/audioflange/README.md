# Mixer Flange

Unity 6000.0.71f1 Linux Editor, offline AudioRenderer, stereo output, 1024-frame
DSP buffers at 48000 and 44100 Hz. Every case records device PCM and a Master
float sink. Default noise, tone and ramp cases run 2097152 frames, more than
four cycles at the measured 0.1 Hz default. No startup frames or phases are
excluded. Mono means a mono clip routed into stereo output, not mono output.
Each fresh mixer starts with no previous audio. Eight silent buffers drain
previous mixers between cases. Two fresh private Native projects per rate must
agree bit for bit before publishing one canonical reference.

| Basis | Contract or observation |
| --- | --- |
| Documented purpose, Unity 6000.0 | The [Flange Effect Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioFlangeEffect.html), also available in the [6000.0 Japanese Manual](https://docs.unity3d.com/ja/6000.0/Manual/class-AudioFlangeEffect.html), describes mixing the original signal with a varying delay, usually below 20 ms. It lists Drymix and Wetmix at 0..100%, defaults 45% and 55%; Depth 0.01..1, default 1; Rate 0.1..20 Hz, default 10 Hz. It specifies neither interpolation nor feedback arithmetic nor startup phase. |
| Documented placement and limitations, Unity 6000.0 | The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) defines effect ordering, complete slot bypass, and optional slot wet mixing that adds a dry path around the whole effect. That option is separate from Flange's intrinsic Drymix/Wetmix. [AudioMixer.SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) controls exposed parameters and returns false for missing names. It warns against calls in Awake, OnEnable and AfterSceneLoad. Native capture sets values from Start. There is no Flange component API in this task. |
| Measured parameter definitions in this release | The installed Editor exposes exactly Drymix, Wetmix, Depth and Rate. Their ranges are 0..1, 0..1, 0.01..1 and 0..20. Defaults are 0.45, 0.55, 1 and 0.1. The Rate minimum and default differ from the Manual. Delay and Feedback are absent; SetFloat and GetFloat return false. No invented controls are sent to the DSP. |
| Measured API and output in this context | Serialized values and exposed setters retain values outside those ranges. The DSP clamps the mixes to 0..1 and Rate to 0..20. Below-minimum Depth, zero and 0.01 give identical samples because of the effective delay-span floor; the exact internal lower clamp cannot be distinguished by these captures. Depth above 1 matches Depth 1. Group and slot bypass remove the effect; source bypass does not remove its routed mixer effect. Routing to Music or applying Flange on Master, impulses, DC, tones and deterministic noise are captured, including mono clips. |
| Algorithm inference constrained by these captures | Both stereo channels share one LFO phase and use a Q14 input history with wrapping at large amplitudes. The effective delay span is `max(4, ((40 * depth) * sample_rate / 1000) * (depth * 0.5))` samples and slews by 1/64 sample per frame from zero. Cosine lookup and rounded floating read coordinates reproduce delay interpolation. The history rounds 40 ms up to whole DSP buffers, at least two. This is release-specific implementation evidence, not a universal Unity guarantee. Isolated impulses show a feedforward delayed tap, with no recurrent feedback tail and no exposed feedback control. |
| Unmeasured / unsupported | Live timed parameter or bypass changes, other output layouts and DSP-buffer sizes, and cross-mixer phase relationships. Enabled slot wet mixing remains a loud unsupported operation. Normalize, Pitch Shifter and other mixer effects are outside this task. |

Record repeats with `native.py --release 6000.0.71f1 --rate RATE --work
FRESH_CACHE_DIRECTORY`. Each invocation takes one heavy-job lease. Compare
with `compare.py --release 6000.0.71f1 --rate RATE --reference FIRST/capture
--candidate SECOND/capture --exact --report CACHE/repeat.jsonl.gz`. Only publish
with `publish.py` after exact agreement. Keep both repeat captures outside Git.
Commit normalized mixer and scene inputs with `tests/authored_project.py record`.

The engine-only gate is `python3 tests/projects/audioflange/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, with UNBOUND pointing to this checkout's release
build. Authored assembly verifies content hashes and never starts the Editor.
The strict report preserves all failing, missing and extra values. Tolerances
are unchanged from the existing audio probes: 2e-5 absolute plus 2e-4 relative.
The separate gate admits one 1/32768 device PCM step and no extra float-sink
slack. RESIDUALS is empty. Artifacts and reports remain in the cache.

Certification: both independent Native repeats agree exactly at each rate across
85721421 values. The engine strict comparison passes 85721421/85721421 at each
rate, with zero raw float/PCM failures and no added residuals.
