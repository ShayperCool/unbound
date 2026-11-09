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
| Measured definitions | The installed Editor reports Pitch 0.5..2/default 1, FFT size 256..4096/default 1024, Overlap 1..32/default 4, Max channels 0..16/default 0. The Manual describes all four controls; the parameter definitions confirm their ranges. Defaults are queried through Editor MixerEffectDefinitions and exposed runtime getters. Explicit defaults reproduce omitted ones. Exposed Pitch keeps -1 and 3 verbatim while DSP matches 0.5 and 2 respectively. |
| Measured state and boundaries | Fresh mixers share the existing first-1024-frame group input fade, including bypass. Source bypass retains the mixer effect. Pitch 1 is delayed and is not an identity kernel. Stereo channels retain separate phase/history. Silence after frame 16384 exposes the full tail; the last nonzero output in the tone and clip-end fixtures is frame 18175. Reusing the previously rendered default mixer after AudioSettings.Reset reproduces its fresh noise output exactly. |
| Implementation inference | A 1024-point phase vocoder uses 256-frame hops, Hann analysis/synthesis windows divided by 32 and a 768-frame input FIFO, giving 1024-frame impulse-peak delay. Positive bins accumulate magnitudes at nearest output bins; the last contributing bin supplies frequency. Phase history, single-precision arithmetic, zero-imaginary phase handling, a reflected 32768-step cosine lookup and radix-4/2 FFT operation order matter to sample parity. These are measured-release facts/inferences, not universal Unity contracts. |
| Unmeasured / rejected | Other releases, nonzero Max channels, other rates and DSP buffers, and effect insertion/bypass/placement transitions. Mono and surround graph support is a follow-up backed by the separate layout captures below. Static slot Wet is supported on this measured stereo profile through +30 dB. No filter component is added. The compatibility profile selects this exact release, managed configuration rejects other contexts, and native group validation rejects unsupported rate/buffer/layout and live changes. |

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

Certification: both independent Editor repeats agree exactly across 11,403,885
stereo values per rate. Engine strict and separate gate comparisons each pass all
11,403,885 at 48000 and 44100 Hz, with zero raw float/PCM failures. There are no
added residuals. The separately recorded mono/fallback-layout pairs agree exactly
across 589,876 values per rate. `regressions.py --release 6000.0.71f1` runs the
unchanged prior DSP and ClearFloat comparators with one runtime snapshot.

Static effect-slot Wet is now covered by the additional `slot_wet_*` cases. See the
[shared contract, measured equation, controls and limits](../../../docs/AUDIO.md#static-effect-slot-wet-mixing).
These additive captures retain all earlier oracle values and comparator tolerances.

## FFT and output context controls

The additional controls use Unity 6000.0.71f1 at both 44100 and 48000 Hz.
Existing reference metadata, PCM and comparison tolerances are unchanged.

| Basis | Contract or observation |
| --- | --- |
| Documented FFT controls | The [6000.0 Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioPitchShifterEffect.html) gives FFT size 256..4096 and Overlap 1..32. It describes larger overlap as smoother and more expensive. The captures below measure this particular release rather than extending that description into an exact DSP guarantee. |
| Measured FFT rule | `fft_*` impulse, noise and shifted-tone controls cover 256, 512, 1024, 2048 and 4096. Intermediate exposed values select the next larger power of two, with endpoints clamped to 256..4096. GetFloat retains the raw value. The hop is FFT size / 4 and FIFO latency is FFT size minus the hop. The window is a reflected 1024-point Hann curve, interpolated with a backward adjacent entry on the descending half at larger FFT sizes and divided by sqrt(FFT size) for analysis and synthesis. |
| Measured overlap | Values 0, 1, 2, 3, 3.5, 5, 8, 16, 32 and 33 retain their exposed value and reproduce the default-overlap PCM. Effective overlap stays four in these controls. `overlap_live` checks a change after nonzero phase and FIFO history exist. |
| Measured live FFT | `fft_live_up` and `fft_live_down` change FFT size after nonzero audio has filled the old histories. They exercise clearing FIFO, overlap and phase history when the selected size changes. Buffers are allocated once on configuration changes; processing reuses them. |
| Documented output settings | [AudioSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.html) and [Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) allow configuration changes but do not guarantee the exact requested settings. [GetDSPBufferSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html) reports the processing block length. Each context control changes the previous configuration, reads back the actual rate/mode/buffer, and captures with those dimensions. |
| Measured stereo buffers | `buffer_256`, `buffer_512`, the original 1024-frame controls, and `buffer_2048` cover the four standard DSP buffer lengths. The added controls combine FFT 2048 with Pitch 1.5, including a hop larger than the smallest output buffer. The kernel keeps state across render boundaries. |
| Follow-up: mono graph | `context_mono` records an active one-channel DSP callback and mono PCM. Its canonical PCM lives under `Reference/unity-6000.0.71f1/layouts/rate-RATE`. The current engine group graph and AudioRenderer sink path carry two channels. Supporting mono correctly requires routing and channel conversion before nonlinear group effects, with matching group/device capture dimensions. Mono remains explicitly rejected by Pitch Shifter configuration and is covered by the guard test. |
| Deferred: unavailable surround output | `context_quad`, `context_surround`, `context_mode5point1` and `context_mode7point1` retain requested modes 4/5/6/8 channels in the configuration getter, but the listener DSP callback reports two active channels on this device. Buffer dimensions and metadata use that measured active count. These fallback controls are preserved beside mono; they do not certify the requested layouts. Actual quad/5.0/5.1/7.1 pitch output remains unmeasured and rejected. |

`native.py` captures both sets and splits layout controls into `WORK/contexts`.
Each nondefault DSP buffer uses a fresh Editor session initialized with that buffer size.
The engine gate likewise captures those controls in fresh sessions so it does not try to run
previously loaded wet-mix slots outside their measured buffer context.
The Editor runner waits a frame after device resets before re-entering recording.
Compare each independent pair with `compare.py --exact --reference A/capture
--candidate B/capture`; for layouts use `--reference A/contexts --candidate
B/contexts --profile layouts`. Publishing requires `--repeat` for an independently
verified capture; the layout set additionally uses `--contexts`. The `all` stereo
profile includes every supported case without truncation; layout reference
metadata is checked independently when recording.
