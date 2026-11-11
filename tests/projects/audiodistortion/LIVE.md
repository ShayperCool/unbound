# Live effect controls

The `live` profile extends this probe with independently authored mixers for
Distortion, Chorus, Flange, Normalize and Pitch Shifter. It captures stereo device
PCM and Master floats at strictly 48000 and 44100 Hz, in 24 buffers of 1024 frames.
Component controls include AudioDistortionFilter and AudioChorusFilter on sources,
mono input clips and listeners. The listener controls retain the upstream Master
sink as well as the processed device output.

The canonical profile contains 154 cases and nine independent sessions per rate, declared by
`live.sections()`. Each session begins with a fresh Editor or engine process.
The device output includes preceding mixers' tails inside that session, so the
same session boundaries are part of both recording and replay. The merged
capture records these case lists in `sessions`; every earlier sample is retained.
The seven original sessions retain all 10,223,827 checked values per rate. The two appended lifecycle sessions add 4,915,300 values from independently agreeing Editor repeats. See [live component histories](LIFECYCLE.md). Separate
public parameter/time traces of the timed Pitch control agree across another 96
fields per rate; `parameter_trace.py` checks them exactly during engine replay.

The main controls change one parameter at buffers 6, 12, 13 and 18. This includes a
one-buffer reversal and a return to the initial value. Exposed setters, immediate
snapshots and four-buffer linear snapshots use the same signal. Every audible
parameter has an exposed-setter and timed-snapshot control. Static-wet controls
also change a kernel parameter while the wet wrapper is active. Wet changes cover
all five effects, zero, unity, gain above unity, the lower audibility boundary and
small gain deltas on either side of the gain-change boundary.

The `reverb_transitions` session adds mixer SFX Reverb Room, Decay Time and Reflect
Delay setter changes, two static mixer controls at HF Reference 50 and 20 Hz that reach
the Room HF pole limit, and AudioReverbFilter room and decayTime setter changes. Each
change waits for the next DSP buffer and crossfades gains, delays and filters through
it, as zones do. The appended lifecycle session covers Dry Level setters and a timed snapshot; they apply at
that buffer without the wet network crossfade.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [Audio effects](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects) process AudioMixer output. [Mixer overview](https://docs.unity.com/en-us/engine/6000.0/manual/audio/mixer/overview) lists effect parameters and wet levels among snapshot values. |
| Documented, Unity 6000.0 | [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) overrides snapshot ownership of an exposed parameter, returns false for a missing parameter or edited snapshots, and should be called in Start or later, rather than Awake/OnEnable/AfterSceneLoad. These controls call it after playback starts. |
| Documented, Unity 6000.0 | [Distortion component](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html) and [Chorus component](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html) describe parameters and source/listener processing. They specify neither sample-level update timing nor history resets. |
| Documented, Unity 6000.0 | [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) specifies linear snapshot interpolation by default and target-snapshot transition overrides. Q167's shared scheduler supplies the values; the effect kernels do not implement another snapshot scheduler. The public parameter trace measures accumulated normalized progress, including float rounding across frame durations. |
| Measured, Unity 6000.0.71f1, this offline profile | Submitted values are constant inside the next buffer, without an additional parameter ramp. A Chorus delay change alone leaves the span target unchanged; a depth change rebuilds that target from the latest valid delay. Other Chorus controls and Flange retain their span slew, phase and history. Normalize retains its peak follower. Pitch changes retain FFT/FIFO/overlap audio and restart analysis and synthesis phase tracking. These per-kernel history rules share the existing release-gated cores. This is a capture observation, not a general synchronization promise for every Unity backend. |

The last session primes its first memoryless control with a scheduled start, then
seeks to integer sample zero before recording. [PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)
uses the absolute DSP clock and allows preparation time; the probe aligns its
input without claiming a cross-backend scheduling-rounding guarantee. Two additional
Chorus controls check delay recreation and a depth change after a deferred delay setter.
Four additional controls hold or edit Pitch/Chorus parameters while Wet is zero:
Wet changes at buffers 4/12, parameters change and return at 6/8. Pitch ratio
submissions restart phase tracking during that pause, while queued audio survives.
Chorus's deferred span behavior remains unchanged when Depth returns before processing resumes.

Record every session twice in fresh private projects:

```sh
python3 tests/projects/audiodistortion/native.py --profile live --release 6000.0.71f1 --rate RATE --work CACHE_A
python3 tests/projects/audiodistortion/native.py --profile live --release 6000.0.71f1 --rate RATE --work CACHE_B
python3 tests/projects/audiodistortion/live.py --release 6000.0.71f1 --rate RATE --reference CACHE_A/capture --candidate CACHE_B/capture --exact --report CACHE/repeat.jsonl.gz
python3 tests/projects/audiodistortion/publish.py --profile live --release 6000.0.71f1 --rate RATE --capture CACHE_A/capture
```

`--cases` selects diagnostic subsets, never a reduced engine gate. The engine-only
gate is `python3 tests/projects/audiodistortion/live_run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`. It uses the committed `live` authored set, compares
every sample with the original 2e-5 absolute plus 2e-4 relative bounds, and records
both a strict report and a separate one-device-PCM-step gate report. No float-sink
slack or sample limits are added. Reports and both independent repeats remain in
the cache. Existing static and spatial references are unchanged.
