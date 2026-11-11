# Surround effects

The independent `surround` profile captures Unity 6000.0.71f1 Linux at 48000 and
44100 Hz, 1024-frame DSP buffers, and Quad, Surround (5.0), Mode5point1 and
Mode7point1 output. Its 106 controls compare every Master float and device PCM
sample. A stereo control checks Duck timing; additional stereo controls check
Compressor DSP parameter limits. Existing pitch, layout and device references
remain byte-identical.

| Basis | Contract or measurement |
| --- | --- |
| Documented effect purpose | The Unity 6000.0 [Compressor](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioCompressor.html), [Chorus](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusEffect.html), [Flange](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects/class-audio-flange-effect) and [SFX Reverb](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbEffect.html) Manuals describe the effects and their parameters. Chorus describes quarter-period tap offsets. They do not specify speaker allocation, detector linking, network topology or exact sample arithmetic. |
| Documented routing | The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) describes Send routing into Duck Volume, effect order and slot bypass. The Duck controls use an actual Music Send into Master Duck, with a loud channel and quiet signals on every other channel. Existing mixerduck placement and bypass controls remain required. |
| Documented components | [AudioChorusFilter](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter.html) and [AudioReverbFilter](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbFilter.html) expose their controls without an exact surround kernel guarantee. Impulses on every speaker exercise each filter on a source and on the listener. |
| Documented zones | The [Reverb Zone Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbZone.html) and [Scripting API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbZone.html) describe the listener-distance blend and overlapping zones. Existing audioreverb `zone_11`, `zone_12_5`, `zone_20` and overlap controls test audible changes to those guarantees. The new controls place mono 3D sources at front and rear azimuths with the listener fully inside a Generic zone. |
| Documented device limitation | [AudioSpeakerMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html) warns that the device can override a request. Each control measures the actual listener callback width before capture. Verification rejects a surround profile running fewer speakers than requested. |
| Measured linking | Every speaker, including LFE, can drive the Compressor peak detector and Duck's common gain. The 23 independent loud-speaker controls per effect cover every active speaker of all four layouts. Duck's detector smoothing time scales with active channel count relative to stereo; its eight-frame gain interpolation remains shared. A five-channel layout excludes its unused storage slot from the timing count. |
| Measured Compressor limits | DSP clamps Threshold, Attack, Release and Make up gain to their Inspector ranges; exposed values stay raw. Attack 8/10/500 ms, Release 10/1500 ms, zero/negative times, out-of-range threshold and gain controls exercise both boundaries. Nonfinite controls remain unsupported. `Compat.MixerCompressorParametersMeasured` selects this rule; other releases retain their prior admission checks. |
| Measured modulation | Chorus assigns channels cyclically to wet taps 1/2/3, at phase offsets 0/0.25/0.5. The isolated tap-3 control makes its contribution audible. Flange uses one sweep coordinate and separate histories for every channel. Distinct speaker impulses and noise bursts test both effects. |
| Measured reverb | All speakers feed one eight-line network. Outputs fold `floor(8 / channels)` taps per speaker, taking indices `speaker + k * channels`; a five- or six-channel layout leaves the remaining taps unused. Mixer SFX Reverb and source/listener Reverb filters share this rule. Zone sends include the source's active surround speakers, and the return reaches every output speaker. |
| Implementation scope | `Compat.AudioSurroundEffectsMeasured` selects shared reverb, Chorus speaker allocation, Duck timing and full-layout zone sends/returns. NativeApi 57 carries source/frame flags without changing packet sizes. Unmeasured releases keep their previous per-pair effect paths and front-pair zone path, including the SFX Reverb warning. No audio processing allocation or lock is added. |

Record independent captures with:

```sh
python3 tests/projects/audiopitch/native.py --release 6000.0.71f1 --rate RATE --set surround --work FRESH_CACHE_DIRECTORY
```

The runner starts a private eight-channel PulseAudio server and selects it with
`PULSE_SERVER`. Its runtime, state and bus address are isolated. Cleanup queries
only that server and unloads its null sink by numeric module index before
terminating it. It never changes the session's default sink. Check the NVIDIA
Renderer line in each Editor log. Compare two captures with `compare.py --profile
surround --exact`, then publish one with `publish.py --surround --repeat REPEAT`.
Repeats and all reports stay outside Git.

Independent Editor repeats agree exactly across 38,404,832 values per rate. The engine
gate passes every value. Strict device-only reports retain 251,576 one-step differences
at 48000 Hz and 457,984 at 44100 Hz; every Master float passes the original bounds.

The engine-only `run.py` gate runs this profile in its own session, as recorded,
and also checks every prior pitch/layout/device control. ATOL 2e-5 and RTOL 2e-4
are unchanged; the gate admits one 1/32768 device PCM step and no Master-float
slack. Missing, extra and failing values stay in the reports. No residual or
sample limit is added.

This measures static effects at the listed rates, buffer and layouts. The separate
[transitions profile](TRANSITIONS.md) adds live parameter, filter enabled and zone
activation/preset controls. Reset PCM is not repeatable in the recorded diagnostic
sessions and remains open. Mixer topology changes, other rates/buffers, Prologic,
surround wet-slot controls, scripted zone send placement and overlapping surround
zones remain unmeasured. General layout
reconfiguration updates component channel counts and offsets; a Rust control
checks a retained listener reverb after quad-to-7.1 reconfiguration without
claiming an Editor history-reset contract.
