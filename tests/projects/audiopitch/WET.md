# Surround mixer wet slots

The independent `wet` profile records Unity 6000.0.71f1 Linux at 48000 and
44100 Hz, 1024-frame DSP buffers and Quad, Surround (5.0), 5.1 and 7.1 output.
Its 238 controls include stereo SFX Reverb and Send/Receive live controls.
The separate 18-control `wet-edges` profile checks full/dry placement boundaries.
Existing pitch, layout, device, surround and transition references are unchanged.

| Basis | Contract or measured context |
| --- | --- |
| Documented routing | The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) and [AudioMixer specifics](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerSpecifics.html) describe ordered effects, optional wet mixing, and Send/Receive routing. A pre-attenuation Send with a muted direct path makes the return independently audible. Separate controls move both routing units across attenuation and exercise their ignored bypass flags. |
| Documented effects | The [Normalize](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioNormalizeEffect.html), [Distortion](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionEffect.html), [Chorus](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusEffect.html), [Flange](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects/class-audio-flange-effect), [Pitch Shifter](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioPitchShifterEffect.html) and [SFX Reverb](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbEffect.html) Manuals define the parameters without specifying sample arithmetic or surround state sharing. |
| Documented parameter API | [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) controls an exposed parameter and warns against calls before Start. All live setters here run during playback, after Start, with nonempty effect histories. |
| Documented configuration limits | [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) and [AudioSpeakerMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html) allow the device to override a request. Every case reads the actual configuration and listener channel width; verification rejects a narrower output. |
| Wet contexts | Distortion, Chorus, Flange, Normalize, Pitch Shifter and SFX Reverb each have static -80, -70, -6, 0, +6 and +30 dB controls, a slot before attenuation, and live full/dry/floor/half/boost changes. Input differs on every active speaker. Normalize also has loud front, rear and LFE detector controls. |
| Measured Normalize rule | One peak envelope decays once per frame and is driven by every active speaker, including LFE. A five-channel layout excludes its unused storage slot. All speakers receive that envelope's gain. |
| Measured connection rule | Wet and dry connection gains share a 64-frame transition. Their small-change threshold sums the absolute gain steps across active speakers, so a -70 dB path can remain inaudible in stereo and become audible in surround. A unity connection passes directly, including at startup. Dry slots pause the effect kernel while their connection gains retain their own state. |
| Measured Send rule | The Send gain starts at zero, advances through its initial muted buffer, and then ramps across the interleaved active speakers over one DSP period. All groups start processing with the active mixer graph, including groups with no source yet. It retains accumulated floating-point endpoints; later periods correct the remaining error. Receive adds the returned channels at its position in the chain. |
| Measured reverb startup | First-period input taps and coefficients approach the requested parameters from an empty network's initial settings: 40/60 ms input delays, input pole 0.02, zero early/late gains, line pole 0.6, feedback gain 0.32 and unit rotation gains. Unwrapped reverb controls distinguish the kernel transition from the slot's wet mix; delayed-input controls distinguish startup from steady processing. `Compat.MixerEffectTransitionsMeasured` selects the startup transition. Other releases retain their previous initial parameter application and need startup measurement. |
| Controls outside the surround matrix | Stereo Distortion at -70/0 dB gives the same PCM with and without an exposed Wet parameter. Stereo SFX Reverb and Send/Receive controls exercise live histories. Rust controls also cover 32000 Hz, 97-frame periods, irregular processing chunks, silence and the unused slot of an odd layout without claiming an Editor recording at those device settings. |


The separate 18-control `wet-edges` session puts full and dry Distortion/SFX
Reverb slots before attenuation, with stereo Distortion controls outside the
surround matrix. It records the placement boundaries independently of the main
238-control session. Use `native.py --set wet-edges`, `publish.py --wet-edges`
and `context_run.py --profile wet-edges` with the same release/rate arguments.

`Compat.AudioSurroundEffectsMeasured` selects all-speaker Normalize and Send
processing, and the active-channel connection rule. Other releases retain the
previous per-pair Send ramp and need surround routing measurement; their wide
Normalize and static wet contexts remain refused. `Compat.MixerWetTransitionsMeasured`
selects live connection ramps. Every new allocation occurs during configuration;
processing uses existing histories and stack frames.

The capture runner starts a private eight-channel PulseAudio server selected by
`PULSE_SERVER`. It unloads its own sink by numeric module index. Each Editor log
must identify the GPU renderer. Four matching preflight buffers precede each
case on both backends, as in the transitions profile.

Record two independent projects with `native.py --release 6000.0.71f1 --rate RATE
--set wet --work FRESH_DIRECTORY`. Compare them with `compare.py --profile wet
--exact`; `publish.py --wet --repeat REPEAT` requires every metadata value and raw
sample to agree before publishing one canonical reference. Repeats and reports
stay in the private cache.

The engine-only command is `context_run.py --release 6000.0.71f1 --profile wet`.
It compares every sample with the existing ATOL 2e-5 / RTOL 2e-4 bounds and one
device PCM step; Master floats receive no extra allowance. Strict reports retain
every discrepancy. No residual or sample limit is added.

Two independent GPU Editor captures agree exactly on 88,213,522 values per rate
in `wet` and 6,291,602 in `wet-edges`. The engine passes all 94,505,124
values per rate at the unchanged bounds. The Q390 replay retains 224/200 strict
device PCM differences in `wet` and 29/33 in `wet-edges` at 48000/44100 Hz, all within
one PCM step. Every Master float passes the strict bounds. The existing audio
regression suite also exercises late-created sources in stereo through
`mixerduck`; their silent groups keep Send and parameter histories from the
active graph rather than restarting when the source arrives.

Other wet effect kinds and arbitrary slot topology changes remain unmeasured.
Separate [Mono](MONO.md), [Prologic fallback](PROLOGIC.md) and
[output-settings](SETTINGS.md) profiles cover only their listed contexts.
A requested wide mode on a narrower
device uses that device's actual output layout when validating the effect.
