# Listener scripts and zone returns

Unity 6000.0.71f1 on Linux, 48000/44100 Hz and 1024-frame DSP buffers. The
independent `listener-scripts` profile has 66 controls. Stereo, Quad, Surround,
5.1 and 7.1 controls move the listener from outside the zone to its center at
block 4, outside at block 12 and back at block 20. Mono controls keep a static
zone and do not certify live Mono-zone transitions.

| Basis | Distinguishing control |
| --- | --- |
| [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) receives interleaved audio from the preceding processing stage. | A script scales every channel by zero, one, two or minus one. Dry controls prove that it changes the signal; a no-script control records the ordinary path. |
| [Reverb Zones](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbZone.html) depend on listener position. | Continuous input stays audible during entry, departure and return. Zero-script controls distinguish an independent wet return from audio that the script already processed. |
| [bypassListenerEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassListenerEffects.html) excludes listener effects for an unrouted source; the property does not apply to mixer-routed sources. | Stereo controls compare routed and unrouted sources with bypass off/on and a zeroing script, with and without a zone. The state has a different audible result. |

Two independent GPU Editor captures agree exactly on all 17,826,478 values per
rate. The zone return joins after the listener scripts: the zero script silences
dry output but leaves the zone return audible. An unrouted bypassing source also
joins after the scripts; its routed control remains processed by the script.
These are measured processing rules for this release and context.

The shared return retains its initial processing values until a parameter request
changes, including when the listener starts outside and a return is recreated.
Requests persist across return lifetimes. Removing a zone updates the remaining
zone blend immediately, even if replacement precedes the next audio frame. The
existing reverb controls also exercise that replacement context. The engine uses
the same rule for every
zone parameter, selected by `Compat.AudioZoneStartupMeasured`. The existing
surround compatibility profile selects the independent post-script return and
unrouted bypass paths. Other releases retain their previous unmeasured paths.
Both live device blocks and complete offline DSP blocks use the same return
processing; scripts keep their existing callback sizes. Cold preparation reserves
the temporary return buffer. Rendering adds no allocation or lock.

The corrected engine passes all 17,826,478 values per rate. Strict comparisons
retain 2528 device-only differences at 48000 Hz and 2902 at 44100 Hz, each within
the original one-step PCM gate. Every Master float passes ATOL 2e-5 / RTOL 2e-4.

The engine before the correction retains 2,361,251 gate failures at 48000 Hz and
2,325,610 at 44100 Hz. Every upstream Master float passes. Complete repeat,
strict and gate reports stay outside Git, including all raw failures.

Record independent projects with `native.py --release 6000.0.71f1 --rate RATE
--set listener-scripts --work FRESH_CACHE_DIRECTORY`. Each project uses a private
eight-channel PulseAudio device and GPU Editor. Its controls alias an existing
attenuation-only mixer; authored assets and earlier reference rows are unchanged.
Compare the repeats with `compare.py --profile listener-scripts --exact` before
publishing with `publish.py --listener-scripts --repeat REPEAT`.

Listener filter components interleaved with scripts remain outside the admitted
component-chain scope. These controls add a script and a global zone; they do
not change that existing limitation. Original float tolerances, the one-step
device PCM gate and full sample ranges remain unchanged.

The engine-only land gate is `context_run.py --release 6000.0.71f1
--profile listener-scripts`.
