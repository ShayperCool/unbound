# Unscaled snapshot clocks, unclaimed experiment

This extends the existing DuckProbe privately with unscaled Send transitions at
half speed, dry volume transitions at half speed and zero scale, and the existing
unscaled paused Send and Threshold diagnostics. All five cases retain all 32
1024-frame stereo device/master buffers, available listener buffers, and per-block
public unscaledDeltaTime and parameter logs. The source and runner are the existing
snapshot fixture; the driver adds cases only to its private project copy.

The Unity 6000.0 Scripting API says
[updateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer-updateMode.html)
selects scaled game time or realtime independent of timeScale, and
[TransitionTo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixerSnapshot.TransitionTo.html)
interpolates towards the snapshot during the supplied relative interval.
These contracts do not make wall-time frame intervals repeatable under offline
AudioRenderer capture.

The two complete Native repeats differ in 676032 of 917525 compared records, all
in PCM streams. Recorded unscaled frame durations vary despite identical
captureDeltaTime. For the paused Send case, one capture's block 7 reports
unscaledDeltaTime 0.0018816893; its block 8 parameter is -0.2258027 dB after the audio
update between those coroutine observations. Using the new frame's public delta
would also assign the transition to the wrong update.

Follow-up: capture and replay the actual unscaled audio-update input clock, then
certify complete PCM for both independent input histories. A fixed offline DSP
step does not represent this input. Until a repeatable certification method exists,
timed unscaled sidechain transitions retain their explicit rejection; existing
scaled-time references, tolerances and supported ordinary fader behavior are
unchanged. No canonical unscaled PCM reference is published. Both raw runs and
the complete exact comparison remain outside Git.

```sh
python3 tests/projects/mixerduck/Diagnostics/unscaled-clocks-v2/run.py --release 6000.0.71f1 --work "$work/native-a"
python3 tests/projects/mixerduck/Diagnostics/unscaled-clocks-v2/run.py --release 6000.0.71f1 --work "$work/native-b"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 --exact --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
```

The existing comparator preserves every failing sample. No clock trace is used to
shift, align or omit PCM, and no comparison tolerance is relaxed.
