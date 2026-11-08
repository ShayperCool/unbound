# Chorus

Unity 6000.0.71f1 Linux Editor, offline AudioRenderer, stereo output, 1024-frame
DSP buffers at 48000 and 44100 Hz. The probe records device PCM and each case's
Master float sink. Modulation cases run 262144 frames, including startup and at
least four cycles at the default rate. Short controls run 32768 frames. Eight
silent DSP buffers drain previous mixer tails between cases. Two fresh private
Native projects per rate must agree bit for bit before one reference is published.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | The [Chorus Effect Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusEffect.html) and [Chorus Filter Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html) describe a sinusoidal LFO and three wet taps, each successive tap offset by 90 degrees. Dry/wet volumes range 0..1, default 0.5; delay 0.1..100 ms, default 40; rate 0..20 Hz, default 0.8; depth 0..1, default 0.03. They describe feedback, but the current API deprecates it. Neither specifies interpolation, channel allocation, startup phase or clamping. |
| Documented API, Unity 6000.0 | [dryMix](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-dryMix.html), [wetMix1](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-wetMix1.html), [wetMix2](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-wetMix2.html), [wetMix3](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-wetMix3.html), [delay](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-delay.html), [rate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-rate.html) and [depth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter-depth.html) expose those controls. The [6000.0 bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Modules/Audio/Public/ScriptBindings/Audio.bindings.cs) mark feedback obsolete and implement its getter as zero and setter as a no-op. |
| Documented placement and limits, Unity 6000.0 | [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) requires enabled filters in Inspector order. [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) occupies its Inspector position, receives interleaved samples on the audio thread, and is unavailable on Web. |
| Measured in this release/context | Setters retain -1 and 200. The DSP clamps volumes/depth to 0..1 and rate to 0..20. Negative delay becomes zero. Delay above 100 is ignored, retaining the previous DSP delay, initially 40 ms. Depth-one controls distinguish that behavior from upper clamping; the Editor also permits zero delay. Source feedback always reads zero. Mixer feedback -1/0/0.25/0.5/1 changes no output. The obsolete mixer default feedback getter is excluded: the Editor parameter definition contains an unstable uninitialized default, so it is not a useful oracle. No sample is excluded. |
| Measured in this release/context | In stereo, wet tap 1 processes left and wet tap 2 right, with a quarter-cycle LFO offset. Tap 3 produces no stereo wet output, including for mono clips routed into the stereo mix. A mono source has the same stereo modulation as a routed mono clip. The wet history has Q14 quantization and wraps at large inputs. Defaults, isolated taps, delay/depth/rate endpoints and intermediates, combinations, impulses, tones, deterministic noise, small/large DC, serialized fields, exposed setters, group/slot/source/component bypass and source placement around a halving callback are retained. |
| Algorithm inference, constrained by those samples | The effective delay span is `max(4, delay_ms * depth * sample_rate / 1000 * depth)` samples. It slews from zero by 1/64 sample per frame. A cosine quarter-wave lookup and floating read coordinates reproduce modulation and interpolation, including the rare near-zero-delay read of a following input frame and zero at a rounded history endpoint. This is an inference from these captures, not a documented cross-release contract. |
| Unmeasured / unsupported | Listener Chorus, 3D sources, live parameter/bypass changes and other output layouts. Enabled mixer slot wet mixing is a separate feature and fails clearly; Chorus's own dry/wet controls work without it. Other DSP-buffer sizes have no reference certification. |

Record each independent repeat with `native.py --release 6000.0.71f1 --rate RATE
--work FRESH_CACHE_DIRECTORY`. It takes one installed heavy-job lease. Compare
with `compare.py --release 6000.0.71f1 --rate RATE --reference FIRST/capture
--candidate SECOND/capture --exact --report CACHE/repeat.jsonl.gz`.
Publish one with `publish.py` only after exact agreement. `--extend` adds controls only after verifying every existing metadata value and sample is identical; it cannot replace an existing sample. Record authored mixer,
prefab and scene inputs with `tests/authored_project.py record`; gate assembly
verifies input hashes and never starts the Editor.

The engine gate is `python3 tests/projects/audiochorus/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, with `UNBOUND` pointing to this checkout's release
build. Every missing, extra or failing value remains in the compressed report.
Float tolerances are the existing audio bounds, 2e-5 absolute plus 2e-4 relative.
The strict report applies them to every sample. The separate gate admits one
1/32768 device PCM step and never adds float-sink slack. No modulation phase or
startup frame is dropped. All run artifacts stay in the cache.

Static effect-slot Wet is now covered by the additional `slot_wet_*` cases. See the
[shared contract, measured equation, controls and limits](../../../docs/AUDIO.md#static-effect-slot-wet-mixing).
These additive captures retain all earlier oracle values and comparator tolerances.
