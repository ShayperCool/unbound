# Mixer Pitch Shifter

The profile uses the Unity 6000.0.71f1 Linux Editor, offline AudioRenderer,
stereo output, 1024-frame render/DSP buffers and 48000/44100 Hz. Every Master
float and device PCM sample is compared, including startup and silence after
input ends. Signals are compact deterministic stereo impulses, noise, DC and
997 Hz tone bursts. Pitch values 0.5, 0.75, 1, 1.5 and 2 include actual shifts
in both directions, explicit/omitted defaults, source/slot/group bypass,
exposed out-of-range Pitch controls, clip endings and AudioSettings.Reset.

| Basis | Contract or observation |
| --- | --- |
| Documented purpose, Unity 6000.0 | The [Pitch Shifter Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioPitchShifterEffect.html) describes pitch shifting with multiplier 0.5..2, default 1. It specifies no FFT kernel, latency, sample reconstruction or state-reset guarantee. |
| Documented placement and API, Unity 6000.0 | [AudioMixer](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixer.html) routes sources through group effect chains and notes partial Web support. The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) defines slot bypass and optional dry/wet mixing. [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) takes exposed-parameter control from snapshots and warns against calls before Start. [GetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.GetFloat.html) reads the snapshot before setting or after clearing. Setters here run from Start. |
| Measured definitions | The installed Editor reports Pitch 0.5..2/default 1, FFT size 256..4096/default 1024, Overlap 1..32/default 4, Max channels 0..16/default 0. Only Pitch appears in the Manual. Defaults are queried through Editor MixerEffectDefinitions and exposed runtime getters. Explicit defaults reproduce omitted ones. Exposed Pitch keeps -1 and 3 verbatim while DSP matches 0.5 and 2 respectively. |
| Measured state and boundaries | Fresh mixers share the existing first-1024-frame group input fade, including bypass. Source bypass retains the mixer effect. Pitch 1 is delayed and is not an identity kernel. Stereo channels retain separate phase/history. Silence after frame 16384 exposes the full tail; the last nonzero output in the tone and clip-end fixtures is frame 18175. Reusing the previously rendered default mixer after AudioSettings.Reset reproduces its fresh noise output exactly. |
| Implementation inference | A 1024-point phase vocoder uses 256-frame hops, Hann analysis/synthesis windows divided by 32 and a 768-frame input FIFO, giving 1024-frame impulse-peak delay. Positive bins accumulate magnitudes at nearest output bins; the last contributing bin supplies frequency. Phase history, single-precision arithmetic, zero-imaginary phase handling, a reflected 32768-step cosine lookup and radix-4/2 FFT operation order matter to sample parity. These are measured-release facts/inferences, not universal Unity contracts. |
| Unmeasured / rejected | Other releases, FFT/overlap/channel-limit settings, output layouts/rates/buffers and live parameter/snapshot/bypass/placement transitions. Static slot Wet is supported on this measured stereo profile through +30 dB. No filter component is added. The compatibility profile selects this exact release, managed configuration rejects other contexts, and native group validation rejects unsupported rate/buffer/layout and live changes. |

Record two independent Native projects with `native.py --release 6000.0.71f1
--rate RATE --work FRESH_CACHE_DIRECTORY`. `compare.py --exact` must agree
before `publish.py` creates the one canonical `Reference/unity-6000.0.71f1`
profile. Both repeats remain outside Git. Record Editor-authored inputs using
`tests/authored_project.py record`; the gate assembles them without the Editor.

The engine-only gate is `python3 tests/projects/audiopitch/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, using `$UNBOUND`. The unchanged comparator
uses absolute 2e-5 plus relative 2e-4. A separate gate allows one device PCM
step of 1/32768 and no added Master-float slack. RESIDUALS is empty. Reports
preserve every missing, extra and failing value; no sample limits apply.
All captures, diagnostic candidates and reports remain under the cache.

Certification: both independent Native repeats agree exactly across 3,932,347
values per rate. The engine strict and separate gate comparisons each pass all
3,932,347 at 48000 and 44100 Hz, with zero raw float/PCM failures. An additional
exact comparison also passes every value at both rates. There are no added
residuals. `regressions.py --release 6000.0.71f1` runs the unchanged prior DSP
and ClearFloat comparators with one runtime snapshot to limit disk duplication.

Static effect-slot Wet is now covered by the additional `slot_wet_*` cases. See the
[shared contract, measured equation, controls and limits](../../../docs/AUDIO.md#static-effect-slot-wet-mixing).
These additive captures retain all earlier oracle values and comparator tolerances.
