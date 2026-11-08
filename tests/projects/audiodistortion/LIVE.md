# Live effect controls

The `live` profile extends this probe with independently authored mixers for
Distortion, Chorus, Flange, Normalize and Pitch Shifter. It captures stereo device
PCM and Master floats at strictly 48000 and 44100 Hz, in 24 buffers of 1024 frames.
Component controls include AudioDistortionFilter and AudioChorusFilter on sources,
mono input clips and listeners. The listener controls retain the upstream Master
sink as well as the processed device output.

Each control changes one parameter at buffers 6, 12, 13 and 18. This includes a
one-buffer reversal and a return to the initial value. Exposed setters, immediate
snapshots and four-buffer linear snapshots use the same signal. Every audible
parameter has an exposed-setter and timed-snapshot control. Static-wet controls
also change a kernel parameter while the wet wrapper is active. Wet changes cover
all five effects, zero, unity, gain above unity, the lower audibility boundary and
small gain deltas on either side of the gain-change boundary.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [Audio effects](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects) process AudioMixer output. [Mixer overview](https://docs.unity.com/en-us/engine/6000.0/manual/audio/mixer/overview) lists effect parameters and wet levels among snapshot values. |
| Documented, Unity 6000.0 | [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) overrides snapshot ownership of an exposed parameter, returns false for a missing parameter or edited snapshots, and should be called in Start or later, rather than Awake/OnEnable/AfterSceneLoad. These controls call it after playback starts. |
| Documented, Unity 6000.0 | [Distortion component](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html) and [Chorus component](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html) describe parameters and source/listener processing. They specify neither sample-level update timing nor history resets. |
| Documented, Unity 6000.0 | [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) specifies linear snapshot interpolation by default and target-snapshot transition overrides. Q167's existing scheduler supplies the values; the effect kernels do not implement another snapshot scheduler. |
| Measured, Unity 6000.0.71f1, this offline profile | Submitted kernel values take effect at the next buffer, without an additional parameter ramp or reset. The shared Chorus/Flange span slew remains active, their phase/history remain continuous, Normalize retains its peak follower and Pitch Shifter retains its FFT/FIFO/phase state. Snapshot values are constant inside each buffer. This is a capture observation, not a general synchronization promise for every Unity backend. |

Record every case twice in fresh private projects:

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
