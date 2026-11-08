# Exposed mixer parameter ownership

Q100 / A15 follow-up, Unity 6000.0.71f1 Linux Editor, stereo, 1024-frame
capture blocks at 48000 and 44100 Hz. Two independent fresh Native projects
agree exactly on all 147 fields per rate. Only these new keys are published
here. Existing mixerfilters and audiodistortion references remain unchanged.

| Basis | Contract, observation or cause |
| --- | --- |
| Documented, Unity 6000.0 | [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) takes ownership of an exposed parameter from snapshots. It returns false for a missing name or while snapshots are being edited. Calls in Awake, OnEnable and AfterSceneLoad can behave unexpectedly; this probe uses Start and later coroutine resumptions. |
| Documented, Unity 6000.0 | [ClearFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.ClearFloat.html) resets an exposed parameter and returns false when it cannot find or set it. [GetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.GetFloat.html) returns the current snapshot or transition value before SetFloat and after ClearFloat, and false for missing exposed names. These pages do not specify synchronization of successive calls with parameter application. |
| Documented, Unity 6000.0 | The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) says exposed overrides remain locked across snapshot changes. Snapshots store mixer parameter settings, as described in the [overview](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerOverview.html). [TransitionTo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixerSnapshot.TransitionTo.html) approaches its snapshot over the supplied interval; this probe uses only zero-duration targets. |
| Documented capture limits, Unity 6000.0 | [AudioRenderer.Render](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioRenderer.Render.html) requires recording mode and a buffer sized from GetSampleCountForCaptureFrame times channels. Call it each frame, including zero-sample frames. Oversized buffers advance capture ahead of visuals. No getter synchronization contract is specified. |
| Measured, both effects and rates | Before the first parameter application, Set/Get/Clear/Get reads the immediate override then the start snapshot, 1000 Hz or 0.5. After four main frames with an override applied, Clear/Get repeatedly reads 4000 Hz or 0.9 until the next audio update. Set-after-clear immediately reads 6000 Hz or 0.75; clearing that unapplied set returns the older applied 4000 Hz or 0.9. Repeated getters and clears do not advance state. |
| Measured controls | The same results occur with Render calls and with no Render calls while recording. Another Render in the same callback does not advance getter state. These deliberately skipped and repeated Render calls are diagnostic stress controls outside the documented per-frame capture schedule, not a supported PCM capture recipe or a general Unity timing guarantee. No-render results rule out a requirement for an effect DSP callback to produce the getter value in these contexts. |
| Measured snapshots | A zero-duration transition while overridden leaves the getter at the override. After clearing and crossing the next audio update, it reads the new snapshot, 2000 Hz or 0.25. A further snapshot target becomes visible at the following update. Missing names and a real but nonexposed Volume parameter return false, including repeated control operations. |
| Cause and implementation inference | Getter state needs to distinguish a script override from the override applied at the most recent audio update. Unbound's pending-clear getter discarded that distinction and always read the snapshot. Retaining applied overrides at the existing mixer Tick reproduces these observations without modifying DSP submission or PCM. This is an implementation model constrained by the measured contexts, not a claim about Unity's internal threads. |
| Reconciliation and limits | audiodistortion `mix_clear` sets and clears before its first update, so its immediate 0.5 is consistent with mixerfilters `set_lp.cutoff.8` retaining the already applied 4000. Neither behavior depends on effect type. Other releases, timed transition curves, suspend/reload interactions and other buffer layouts remain unmeasured. No release difference was established. |

Record each Native repeat with `python3 tests/projects/mixerparams/run.py native
--release 6000.0.71f1 --rate RATE --work FRESH_CACHE_DIRECTORY`. Check and publish
with `run.py publish --release RELEASE --rate RATE --candidate FIRST/capture
--repeat SECOND/capture --report CACHE/repeat.json`. Publish refuses mismatching
or incomplete captures and existing reference directories. Keep both raw repeats
outside Git. Record the authored inputs with `tests/authored_project.py record`.

The engine-only gate is `python3 tests/projects/mixerparams/run.py gate --release
"$UNBOUND_PROBE_UNITY_VERSION"`, with UNBOUND set to this checkout's release CLI.
It assembles the content-checked authored project without the Editor, runs both
rates, and compares the complete key union exactly. Reports retain every missing,
extra and differing key under the cache. The original audio probes retain their
original float and PCM tolerances and all raw residuals.

The static Wet extension adds 35 exact fields per rate, for 182 total. It tests Wet
ownership and snapshot values on Distortion, Chorus, Flange, Normalize and Pitch
Shifter without a playing source. Two independent captures agree exactly. The
[shared wet notes](../../../docs/AUDIO.md#static-effect-slot-wet-mixing) describe
the separate rendered controls and the retained rejection of live DSP changes.
