# Post-reset startup diagnostics

The diagnostic-only `recovery` profile measures Unity 6000.0.71f1 Linux at
48000/44100 Hz and 1024-frame DSP buffers. Its 19 controls restart offline
rendering inside `OnAudioConfigurationChanged` before recreating generated clip
data and calling `Play`. They record DSP-frame advance and source position
around the first post-reset buffers. They do not have a certified PCM reference.

The [Audio Settings Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSettings.html)
says configuration changes reload audio objects and lose generated clip data and
playback state. [Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html)
invokes the configuration callback with `deviceWasChanged=false`, and can select
nearby supported settings. [AudioRenderer.Render](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioRenderer.Render.html)
describes interleaved output for each rendered sample frame. These documents do
not specify the first device buffer in which callback recovery becomes audible.

The fixture starts with audible playback and nonempty effect history, then records
eight buffers before Reset, 24 after it and eight during the stop/drain interval.
Quad/7.1 and 5.0/5.1 transitions run in both directions, with dry output, source
Chorus, listener Reverb and mixer Reverb. A Stereo-to-Quad control leaves the
source stopped. Two same-configuration Stereo controls distinguish resource
reset and playback loss from a change of layout. One recovered Stereo control
pauses the main thread for 60 ms after Play. Relative DSP time and source position
are recorded during the pause and after each post-reset render.

A five-control diagnostic at 48000 Hz had exact agreement across two Editor
captures and passed the existing engine PCM gate. Expanding to all 19 controls
exposed a source-position delay in the 5.0-to-5.1 mixer Reverb control: the first
render advanced DSP time by 1024 frames while source position stayed at zero.
Its first audible Master sample was frame 2048, where the earlier recordings
started at frame 1024. Restarting offline rendering before Play therefore does
not establish a fixed startup offset. The recorded source positions are observations. Verification checks their
completeness and preserves their differences without requiring a particular
startup buffer.

Record independent GPU Editor repeats with `native.py --release 6000.0.71f1
--rate RATE --set recovery --work FRESH_DIRECTORY`. Each uses the byte-identical
committed mixer inputs in a fresh private project, with an isolated eight-channel
PulseAudio server. Compare using `compare.py --profile recovery --exact
--reference FIRST/capture --candidate SECOND/capture --report REPORT.jsonl.gz`.
Run the engine against the first private project with `engine.py --cases` and
the names returned by `common.recovery_cases()`, then compare with the same
profile. All raw samples, source positions and discrepancies remain visible.
No alignment, reordered records, extra tolerance or sample limit is applied.

Q373-R1 remains open for callback playback publication, scheduled recovery and
real-device queue timing. The earlier [immediate and scheduled diagnostics](TRANSITIONS.md#reset-diagnostics)
remain runnable and unchanged. Neither those recordings nor this expanded
profile supports a constant reset offset or a scheduling correction. No reset
implementation, mixer admission or canonical reference changes in this task.
