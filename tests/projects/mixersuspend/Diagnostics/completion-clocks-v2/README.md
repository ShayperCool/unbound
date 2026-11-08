# Stream completion clocks, unclaimed experiment

This complete Unity 6000.0.71f1 experiment compares imported PCM Streaming and
DecompressOnLoad clips, both loop settings, ordinary Play, PlayScheduled, and
PlayScheduled with SetScheduledEndTime. Clips have 36000 stereo frames at 48000 Hz.
Each of twelve cases retains all 48 rendered 1024-frame buffers, requested state,
state before/after every Render, state after a 50 ms wall wait at block 40 without
an audio update, and explicit Stop state. All time values retain their float bits.

The Unity 6000.0 Scripting API defines
[timeSamples](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-timeSamples.html)
as the PCM playback position and notes that
[time](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-time.html)
can be imprecise for compressed packets.
[PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)
and [SetScheduledEndTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetScheduledEndTime.html)
use the absolute DSP timeline, with limitations on late rescheduling.
[Streaming](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClipLoadType.Streaming.html)
loads from disk on a dedicated streaming thread. These contracts do not define an
exact publication instant for source getters after streamed output finishes.

The two full Native repeats differ in 16115 of 1180848 compared records. The decoded
controls agree. Streaming position can lag multiple Render calls and advance
during the wall wait alone. In one scheduled non-looping case, the wall wait changes
playing from true to false and position from 28672 to 36000; the next audio update
then resets position to zero. Looped positions can similarly advance during the
wait. These are observations, not a fixed latency or a universal completion rule.
Streaming PCM also differs between repeats, so this cannot certify clocks by
pretending every Render submitted an identical stream buffer.

Follow-up: implement and certify asynchronous streamed data delivery and position/
completion publication together, using a repeatable synchronization experiment.
A fixed number of extra DSP blocks would fit only the captured scheduling. No new
reference is published and the engine's existing completion behavior is unchanged.
Both raw runs and their full comparison remain in the run cache.

```sh
python3 tests/projects/mixersuspend/Diagnostics/completion-clocks-v2/run.py --release 6000.0.71f1 --work "$work/native-a"
python3 tests/projects/mixersuspend/Diagnostics/completion-clocks-v2/run.py --release 6000.0.71f1 --work "$work/native-b"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 --exact --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
```

The GPU Editor launcher supervises each process. Missing or failed captures remain
failures. No case, callback, sample or getter is filtered from the comparison.
