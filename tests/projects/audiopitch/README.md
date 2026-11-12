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
| Unmeasured / rejected | Other releases, Prologic output, rates other than 48000/44100 Hz, DSP buffers other than 256/512/1024/2048 frames, and effect insertion/bypass/placement transitions. Max channels and the mono, quad, 5.0, 5.1 and 7.1 layouts are covered below. Static slot Wet is supported on the stereo 1024-frame profile through +30 dB. No filter component is added. The compatibility profile selects this exact release, managed configuration rejects other contexts, and native group validation rejects unsupported rate/buffer/layout and live changes. |

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

Certification: independent Editor repeats agree exactly across 12,845,768 stereo values
and 5,636,281 layout values per rate. At 48000 Hz one repeat differed from the other and
from the published reference only in the existing `reset_noise` control (up to 0.77); a
third repeat agreed exactly with the first, which matches the published values. Engine
strict comparisons pass every stereo value and every layout value bit-exactly (20,775,389 layout
values per rate). There are no added residuals. `regressions.py --release
6000.0.71f1` runs the unchanged prior DSP and ClearFloat comparators with one runtime snapshot.

Static effect-slot Wet is now covered by the additional `slot_wet_*` cases. See the
[shared contract, measured equation, controls and limits](../../../docs/AUDIO.md#static-effect-slot-wet-mixing).
These additive captures retain all earlier oracle values and comparator tolerances.

## FFT and output context controls

The additional controls use Unity 6000.0.71f1 at both 44100 and 48000 Hz. Stereo reference
metadata, PCM and comparison tolerances are unchanged. The quad, 5.0, 5.1 and 7.1 layout
controls were re-recorded with those layouts active (the earlier capture ran stereo on a
two-channel device); mono values are unchanged.

| Basis | Contract or observation |
| --- | --- |
| Documented FFT controls | The [6000.0 Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioPitchShifterEffect.html) gives FFT size 256..4096 and Overlap 1..32. It describes larger overlap as smoother and more expensive. The captures below measure this particular release rather than extending that description into an exact DSP guarantee. |
| Measured FFT rule | `fft_*` impulse, noise and shifted-tone controls cover 256, 512, 1024, 2048 and 4096. Intermediate exposed values select the next larger power of two, with endpoints clamped to 256..4096. GetFloat retains the raw value. The hop is FFT size / 4 and FIFO latency is FFT size minus the hop. The window is a reflected 1024-point Hann curve, interpolated with a backward adjacent entry on the descending half at larger FFT sizes and divided by sqrt(FFT size) for analysis and synthesis. |
| Measured overlap | Values 0, 1, 2, 3, 3.5, 5, 8, 16, 32 and 33 retain their exposed value and reproduce the default-overlap PCM. Effective overlap stays four in these controls. `overlap_live` checks a change after nonzero phase and FIFO history exist. |
| Measured live FFT | `fft_live_up` and `fft_live_down` change FFT size after nonzero audio has filled the old histories. They exercise clearing FIFO, overlap and phase history when the selected size changes. Buffers are allocated once on configuration changes; processing reuses them. |
| Documented output settings | [AudioSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.html) and [Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) allow configuration changes but do not guarantee the exact requested settings. [GetDSPBufferSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html) reports the processing block length. Each context control changes the previous configuration, reads back the actual rate/mode/buffer, and captures with those dimensions. |
| Measured stereo buffers | `buffer_256`, `buffer_512`, the original 1024-frame controls, and `buffer_2048` cover the four standard DSP buffer lengths. The added controls combine FFT 2048 with Pitch 1.5, including a hop larger than the smallest output buffer. The kernel keeps state across render boundaries. |
| Documented channel controls | The Manual gives Max channels as "the maximum number of channels (range 0 to 16, default = 0 channels)", a float. [AudioSpeakerMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html) gives 1/2/4/5/6/8 channels for Mono/Stereo/Quad/Surround/5.1/7.1 and warns that the device may override a requested mode. Neither page says what happens to channels beyond the limit, how sources are mixed into a layout, or anything about the LFE channel. |
| Measured Max channels | `maxch_*` (stereo) and `context_*_max*` controls: the effect processes when the group's channel count is at most the limit, where the limit is the clamped whole value of Max channels (0..16), or the layout's channel count when it is 0. Otherwise the input passes through unchanged, without the FFT delay: 1 and 1.5 in stereo, 2 in 5.1 and 6 in 7.1. 2, 2.5, 16, 17 and -1 process like the default. A live change that keeps the processed count (0 to 2 in stereo) keeps every history; one that changes it (0 to 4, 0 to 1 and back, 0 to 2 in mono) restarts them. GetFloat keeps the raw value. |
| Measured mono output | `context_mono_*`: a stereo clip enters the mono graph as `0.707·L + 0.707·R` of its panned stereo pair (pan -0.5 gives 0.707 and 0.3535); 0.707 is exact, not 1/sqrt(2). A mono clip keeps the length of its stereo pan gains, independent of 2D pan. Group effects then process that single channel: mono Pitch Shifter output equals the one-channel kernel applied to that mix (`context_mono`, `context_mono_clip_shift`, `context_mono_max1`, `context_mono_live`). |
| Measured quad, 5.0, 5.1 and 7.1 | For stereo clips FL/FR are bit-identical to the stereo controls and every other channel is silent, with and without the Pitch Shifter. A mono 2D clip uses the stereo pan law on FL/FR; in 5.1 and 7.1 it also feeds LFE (channel 3) at unity gain, independent of pan. Quad and 5.0 have no such send. |
| Measured surround 3D | `context_{quad,surround,mode5point1,mode7point1}_3d_*`: mono clips at azimuths 30°, 100°, 160° and 300°, a stereo clip, spread on a mono and a stereo clip, half blend with pan, an elevated source, and sources at and directly above the listener (every speaker at √2/n). Each direction pans between the two neighbouring speakers with power-normalized vector-base gains and feeds LFE (5.1/7.1) at unity; a mono clip averages its two spread directions. `context_mode5point1_clip_shift` and `_3d_shift` run the Pitch Shifter on the LFE and rear channels; `*_generator_all` write a distinct constant on every channel; `context_mode5point1_script` adds a constant on a rear channel from a clip source's script; `context_mode7point1_listener` writes a side channel. |
| Measured mono 3D and scripts | `context_mono_3d_*`: in mono output 3D directions all reach the one speaker at unity (spread has no effect), so a fully 3D source is its attenuation per channel; spatial blend mixes the 2D and 3D speaker levels before the mono downmix: a mono clip at half blend gives 0.9659 (pan -0.5) and 0.8660 (pan +0.5, `context_mono_3d_half_right`) times the blended attenuation, consistent with the 3D level landing on the first speaker level whatever the pan (partial blend is measured at 0.5 only); a stereo clip blends its 2D channel levels (0.707 times the pan gain) with unity (`context_mono_3d_stereo_half`). `context_*_generator` and `context_*_listener`: clipless-source and listener scripts receive the layout's channel count (1 mono, 2 stereo, 6 in 5.1); a listener write into channel 4 reaches the 5.1 output. `context_stereo_3d_clip` repeats the 3D setup in stereo. |
| Measured devices | `device_N` records, for an N-channel device (N = 1..8), every requested layout's getter value, `driverCapabilities` and the listener callback's channel count. Capabilities: 4 channels Quad, 6 5.1, 8 7.1, other counts Stereo; a request above them runs at them, Prologic runs as two channels, the getter keeps the request. Metadata only, at 48000 Hz (`Reference/unity-6000.0.71f1/devices`). |
| Capture device | A requested layout runs only up to the default output device's capabilities. `native.py` records the layout controls against a private PulseAudio server whose default sink has eight 7.1 channels, and each device control against a private sink of its own channel count (`PULSE_SERVER`; the session's own audio server is untouched). The engine gate gives its virtual device the same channel counts (`UNBOUND_AUDIO_DEVICE_CHANNELS`). |

`native.py` captures the stereo set in `WORK/capture`, the layout set in `WORK/contexts` and,
at 48000 Hz, the device set in `WORK/devices/capture`; `--set layouts` or `--set devices`
records one set alone. Publish them with `publish.py --contexts` and `--devices`.
Each nondefault DSP buffer uses a fresh Editor session initialized with that buffer size.
The engine gate likewise captures those controls in fresh sessions so it does not try to run
previously loaded wet-mix slots outside their measured buffer context, and compares the layout
controls (`--profile layouts`) from one more engine session.
The Editor runner waits a frame after device resets before re-entering recording.
Compare each independent pair with `compare.py --exact --reference A/capture
--candidate B/capture`; for layouts use `--reference A/contexts --candidate
B/contexts --profile layouts`. Publishing requires `--repeat` for an independently
verified capture; the layout set additionally uses `--contexts`. The `all` stereo
profile includes every supported case without truncation; layout reference
metadata is checked independently when recording.

## Surround effect controls

The independent `surround` profile adds 106 controls for Compressor, Duck Volume,
Chorus, Flange, SFX Reverb, source/listener filters and reverb zones in
quad/5.0/5.1/7.1. See [the contracts, measured rules and remaining scope](SURROUND.md).
`native.py --set surround` records it separately; `publish.py --surround` requires
an independent repeat. `run.py` gates it alongside every prior profile.

The independent `wet` and `wet-edges` profiles add 256 controls for surround
wet slots, Send/Receive routing, all-speaker Normalize and SFX Reverb startup.
See [WET.md](WET.md) for the contracts, exact-repeat checks and measured limits.
Their engine-only commands are declared separately in `gate.json`.

Additional rate/buffer controls and their capture limits are documented in
[SETTINGS.md](SETTINGS.md). They use an independent reference profile and exact
rate/buffer pairs; the original stereo, layout and surround references remain
unchanged.
