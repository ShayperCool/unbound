# Completion clock controls

Unity 6000.0.71f1, Linux Editor, AudioRenderer, stereo 48000 Hz, 1024-frame DSP
buffers. Two independent captures agree exactly on all 2,361,794 records, including
complete PCM. The engine agrees bit for bit without a clock implementation change.
The engine-only gate is `stream_clocks.py gate --release 6000.0.71f1`.

The 24 controls cross PCM DecompressOnLoad/Streaming, 36000-frame stereo and
11265-frame mono clips, looping on/off, Play/PlayScheduled/scheduled end. Every
case retains 48 complete buffers and getter observations before and after each
render, plus observations after wall-only waits. Captures allow background state
publication to settle. Requested DSP times lie one quarter sample away from a
frame boundary; this avoids converting a floating-point tie into a scheduling rule.

| Contract or observation | Divergent control and result |
| --- | --- |
| Documented: [timeSamples](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-timeSamples.html) reports sample position; [time](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-time.html) can be imprecise for compressed audio. | Sample positions and exact float time bits are retained independently. The canonical clips use PCM compression; compressed timing is not generalized from them. |
| Documented: [PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html) uses the absolute DSP clock. | A future start leaves four rendered buffers silent with position zero while immediate Play advances. |
| Documented: [SetScheduledEndTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetScheduledEndTime.html) sets an absolute DSP end. | The scheduled end cuts a still-playing clip at position 9216, including a looping source which otherwise continues beyond that time. |
| Documented: [loop](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-loop.html) repeats a clip; [Streaming](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClipLoadType.Streaming.html) loads audio on demand. | Looped positions wrap at both clip lengths; nonlooped controls stop. Both load types have identical settled clocks and PCM here. |
| Measured, natural completion | The completing render reports isPlaying false and the exact clip length, 36000 or 11265 samples. The next audio update clears timeSamples/time to zero. A wall-only wait does not advance the DSP clock. |
| Measured, scheduled end | The render crossing the requested end reports isPlaying false and position 9216; the next update clears both positions. Looping does not prevent the requested stop. |

These observations describe settled publication around explicit AudioRenderer
renders. They do not promise synchronous getter publication on a real device.
The earlier burst diagnostic remains unchanged. `--vorbis` records the same full
control matrix as a diagnostic: independent compressed captures disagree on
background publication and one short clip's initial decoded buffer. Both complete
private captures and their unfiltered comparison are retained outside Git; no
compressed oracle is published. Publication latency, floating sample-boundary ties
and real-device queued periods are Deferred, not inferred scheduling guarantees.
