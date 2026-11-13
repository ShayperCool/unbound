# Sample scheduling and publication

This profile measures Unity 6000.0.71f1 Linux at 48000/44100 Hz and 1024-frame
DSP buffers. Twenty-one controls cover generated stereo clips in ordinary
Stereo output and recovery from Stereo to Quad. Two independent GPU Editor
captures agree exactly on all 4,063,955 metadata and sample values per rate.

| Public contract | Distinguishing control |
| --- | --- |
| [PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html) uses the absolute DSP timeline. | Quarter- and three-quarter-sample requests share the preceding whole sample. Both play at frame 8192 of the capture. The integer DSP-frame anchor keeps clock roundoff away from the requested fractions. |
| [SetScheduledStartTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetScheduledStartTime.html) changes an existing future start; its timing limitations can prevent an edit. | The initial request selects frame 12288. An immediate edit to frame 8192 is overwritten when the initial start is published on the audio update, including after two Play calls. The same edit after two rendered blocks takes effect and starts at frame 8192. A call without Play stays silent. |
| [SetScheduledEndTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetScheduledEndTime.html) uses an absolute end time. | The scheduled clip starts at frame 8192 and stops at 10240, leaving the rest of its 32768-frame clip unplayed. Both requested fractions are checked. |
| [PlayDelayed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayDelayed.html) takes seconds independently of the output sample rate. | The two fractions start at frame 8192 at both rates. |
| [Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) reports a configuration change; the [Audio Settings Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSettings.html) requires recreating generated clip data and playback state. | Reset follows eight audible buffers. The callback observes stopped playback and zeroed old clip data before creating and scheduling a new clip. The same start/edit/end comparisons run after recovery. |

The publication timing and fractional conversion above are observations of this
release, not additional promises in the public API. AudioOutputRules selects
whole-sample conversion and pending-start handling for this release. The initial
start remains pending until the audio update even when a second Play makes the
voice available to the DSP. Other releases retain the previous unmeasured
nearest-frame and immediate-edit behavior.

The engine replay performs an audio update at each reset render boundary,
matching the Editor coroutine's frame updates. This also publishes completed
source positions after a scheduled end. It does not insert or remove PCM frames.
The existing comparator checks every sample and field, including post-reset and
drain output; float bounds and the one-step device bound are unchanged.

The engine matches all 4,063,955 values per rate strictly, including every
Master float and device sample. The earlier engine comparison retained 163,927
failures per rate with the same fixture and bounds. Rust controls additionally
exercise 32000 Hz / 97-frame buffers, a nonzero starting clock, both publication
phases, two Play calls and the previous release path.

Record independent repeats with `native.py --release 6000.0.71f1 --rate RATE
--set schedule --work FRESH_DIRECTORY`. Publish only after exact agreement with
`publish.py --schedule --repeat REPEAT/capture`. Replay the canonical reference
with `context_run.py --release 6000.0.71f1 --profile schedule`.

This profile does not certify arbitrary immediate reset recovery or real-device
queue timing. The earlier [reset diagnostics](RECOVERY.md) retain their reported
counterexamples. Compressed/streamed publication, rescheduling already playing
voices, other output contexts and other releases remain unmeasured here.
