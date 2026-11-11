# Surround transitions

The independent `transitions` profile covers 45 live controls in Unity
6000.0.71f1 Linux at 48000 and 44100 Hz, 1024-frame DSP buffers and Quad,
Surround (5.0), 5.1 and 7.1 output. A stereo Compressor control checks the
same mechanism outside the surround matrix. Reset PCM remains an uncertified
diagnostic, described below.

| Mechanism | Controls and measured rule |
| --- | --- |
| Compressor | Threshold, Attack and Release change while a loud rear channel drives the shared gain. A stereo control lowers and raises Threshold and changes Make up gain. The follower retains its state in threshold-relative units, so a threshold change approaches its new gain through that follower. `Compat.MixerEffectTransitionsMeasured` selects this rule; other releases retain the previous amplitude-relative follower and need live-threshold measurement. |
| Duck | Threshold and Attack change during the loud interval, and Release changes before it ends. The existing linked detector and gain path match the recorded transitions on every output speaker. |
| Chorus and Flange | Delay/Depth/Rate and Depth/Rate respectively change while a multichannel noise burst is audible. Delay histories and the common sweep persist. Chorus uses the previously measured speaker-to-tap allocation. |
| SFX Reverb | Room HF, Dry Level and Decay Time change during an audible burst. The shared surround network retains its history; the existing parameter transition and dry gain paths match these controls. |
| Components | A source or listener Chorus/Reverb filter is disabled with nonempty history, re-enabled four buffers later and then given new parameters while its input is audible. The existing retained-history path matches these contexts. |
| Zones | A mono 3D source plays fully inside one zone. Separate controls disable/re-enable the zone or switch Generic/Cave/Generic presets. The existing send and shared return paths match these surround contexts. |

The [Compressor](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioCompressor.html),
[Chorus](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusEffect.html),
[Flange](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects/class-audio-flange-effect)
and [SFX Reverb](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbEffect.html)
Manuals define the controls without specifying exact transition samples or
history retention. The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html)
describes Send routing to Duck. The [component](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter.html)
[APIs](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbFilter.html)
and [Behaviour.enabled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Behaviour-enabled.html)
define their controls. The [zone Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbZone.html)
and [AudioReverbZone](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbZone.html)
define listener-distance blending and combined zones. Exact DSP histories and
arithmetic here are measurements, limited to this release and configuration.

Record two independent captures with `native.py --release 6000.0.71f1 --rate RATE
--set transitions --work FRESH_DIRECTORY`. The runner uses a private eight-channel
PulseAudio server, selected through `PULSE_SERVER`, and unloads its own sink by
numeric module index. Every Editor log must identify the GPU renderer. The live
profile pumps four preflight buffers on each backend before creating the case's
source or mixer. This keeps any preceding mixer tails at the same processing age.
The Native listener observer only reads channel width; the virtual backend knows
its active layout and does not attach that observer. This does not measure listener
scripts interleaved with component filters.

Two independent GPU Editor repeats agree exactly on 16,712,055 values per rate.
The engine gate passes all of them. Strict reports retain 1,745 and 1,956
one-step device PCM differences at 48000 and 44100 Hz respectively; every Master
float passes the unchanged bounds.

The engine-only `context_run.py` gate compares every recorded sample using the
existing ATOL 2e-5 / RTOL 2e-4 bounds and one device PCM step, with no extra Master
float allowance. Strict reports preserve every difference. Existing pitch, layout,
device and static surround references stay byte-identical.

## Reset diagnostics

The [Audio Settings Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSettings.html)
says a configuration change reloads audio objects and loses generated clip data
and playback state. [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html)
notifies scripts with `deviceWasChanged=false` and may choose nearby supported
settings. [OnAudioConfigurationChanged](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.OnAudioConfigurationChanged.html)
describes configuration notifications. The diagnostic starts with audible playback,
changes the layout, records callback count/flag/mode/playing state and reads the
old generated clip before optionally recreating it in the callback. A no-recovery
control leaves playback stopped. It records PCM on both sides and through the
existing stop/drain interval.

`native.py --set resets` retains the 62-control session with immediate `Play()`
recovery. `native.py --set scheduled-resets` records the 65-control session with
[PlayScheduled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)
eight DSP buffers ahead and fractional scheduling controls. Compare complete
independent captures with `compare.py --profile resets` or `--profile
scheduled-resets`, `--reference FIRST/capture --candidate SECOND/capture --exact`.
Neither diagnostic has a certified PCM reference or runs in the land gate.

The two immediate-recovery repeats differ by 237,422 values at 48000 Hz in the
7.1-to-quad mixer Reverb case, and by 126,562 at 44100 Hz in the 5.0-to-5.1 listener
Reverb case. Scheduled-recovery repeats differ by 491,726 and 547,193 values
respectively, across nine and three cases. Their API observations agree. The first
audible post-reset buffer can differ, and a later case can receive the changed tail.
These are raw repeat failures with no alignment, reordered records or tolerance
allowance. Asynchronous playback publication is an unresolved explanation.

The fractional controls also expose unresolved command timing. The
[SetScheduledStartTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetScheduledStartTime.html)
and [SetScheduledEndTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetScheduledEndTime.html)
APIs warn that timing can prevent rescheduling. Immediate edits following
PlayScheduled do not establish a universal rescheduling rule. Scheduling admission,
whole-frame boundaries after reset, callback recovery PCM and real-device queue
timing remain open. Raw captures and their complete reports stay outside Git.

Further unmeasured contexts include mixer-slot insertion/retargeting/bypass,
component membership changes in surround, other parameter combinations, other
rates/buffers, Prologic, scripted zone sends and overlapping surround zones.
