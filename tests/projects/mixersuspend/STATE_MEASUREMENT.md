# Suspension state controls

The `state.py` experiment records 27 independent mixers at 48000 and 44100 Hz,
stereo, 1024-frame DSP buffers. Each mixer has 512 complete blocks of device PCM,
listener samples and master sink samples. Every GetFloat observation is a separate
one-frame float stream, using the original comparator's float tolerance; metadata
and independent Native repeats compare exactly. No blocks are shifted or removed.

Echo, Lowpass, Highpass, SFX Reverb and Duck Volume each have an enabled-suspension,
disabled-suspension and fresh-history control. The short source ends after four
blocks. Suspension threshold is 0 dB, so the silent input permits suspension after
the one-second wait even when an effect has history. A new looping source wakes the
mixer at block 256. Echo/Reverb/filter wake input is silence. Duck wake input is
quiet constant stereo, making retained detector attenuation observable. Enabled,
disabled and fresh cases retain all silent blocks and retained sink buffers.

Snapshot cases explicitly select Base before the first audio update, except
`snap_selected`, which observes saved Editor selection. Instant, four-second and
ten-second transitions, Normal with timeScale 0/0.5/1, exposed overrides, ClearFloat
at wake, and live gain/snapshot/clear controls distinguish parameter ownership from
DSP suspension. The UnscaledTime case uses an instant transition with timeScale 0.
It measures applying a snapshot under pause, not a fixed realtime duration.

The earlier complete diagnostic experiment is preserved in
`Diagnostics/state-timeline-v1/`. Its timed UnscaledTime case progresses with wall
time while captureDeltaTime fixes the game clock. Independent Native runs disagree
on that case's entire timing-dependent output. It has no canonical reference and
is not a source of accepted samples for this experiment. Its complete captures,
comparisons and original private projects remain outside Git. The current experiment
adds active parameter controls and measures instant UnscaledTime separately; timed
UnscaledTime PCM remains unclaimed. No original suspension oracle or tolerance changes.

Record twice with fresh directories before publishing:

```sh
python3 tests/projects/mixersuspend/state.py native --release 6000.0.71f1 --rate 48000 --work "$work/a"
python3 tests/projects/mixersuspend/state.py native --release 6000.0.71f1 --rate 48000 --work "$work/b"
python3 tests/projects/mixersuspend/state.py publish --release 6000.0.71f1 --rate 48000 --candidate "$work/a/capture" --repeat "$work/b/capture" --report "$work/repeat.jsonl.gz"
```

Repeat at 44100 Hz. `state.py gate` uses only the committed authored project and
canonical references, without launching the Editor.

## Contract and measured causes

Unity 6000.0 documentation:

- [Audio Mixer window](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixer.html)
  describes routing and snapshots and notes partial Web-platform support.
- [AudioGroup Inspector, Audio Mixer inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html)
  says source/submixer input keeps a mixer active. After the final source ends it
  waits one second, then measures output loudness. Auto Mixer Suspend controls
  suspension; the documented default threshold is -80 dB.
- [AudioMixer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.html)
  and [updateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer-updateMode.html)
  define Normal as scaled game time and UnscaledTime as realtime. GetFloat follows
  the current snapshot/transition unless SetFloat owns the parameter; ClearFloat
  returns control to snapshots. These pages do not specify DSP-history disposal,
  saved Editor selection on startup, or sample-level parameter submission.

| Mechanism | Observation in Unity 6000.0.71f1 Linux | Implementation / limit |
|---|---|---|
| Suspension decision | Existing activity, mute, threshold and clock matrix retains all blocks. A silent playing voice keeps an unmuted route awake. Wait/measurement uses rendered DSP time observed at audio updates, independently of captureDeltaTime and wall time. | Existing DSP-time/RMS mechanism remains. enableSuspend=false prevents suspension. The existing stereo RMS and threshold conversion remain unchanged. |
| DSP history | Echo delay contents/cursor, Lowpass/Highpass history, Reverb tail and Duck detector attenuation survive the silent gap. Enabled, disabled and fresh controls distinguish frozen histories from discarded histories. | Suspension skips the graph and wake resumes it. Neither transition calls the effect reset routine or restarts the graph's input fade. Ordinary DSP updates continue while suspension is disabled. |
| Snapshot in progress | Normal transitions started before sleep or during sleep advance at scaled game time. timeScale=0 freezes the timed transition; 0.5 halves progression. Instant UnscaledTime transitions apply under pause. | Snapshot ownership/time advances on audio updates even while DSP is suspended. Wake uses the current interpolated value, not the value at suspension or the destination unconditionally. Timed UnscaledTime wall-clock output has no deterministic PCM oracle. |
| Exposed parameters | A SetFloat override persists across sleep/wake. A submitted override still reads immediately after ClearFloat until the next audio update. At that update the current snapshot value takes over. | Existing override/clear submission rule remains; no snapshot, override or transition is cleared on suspension/wake. |
| Attenuation submission | Active SetFloat, instant/timed snapshots and ClearFloat at wake apply the submitted gain across the DSP block. The live cases control for suspension being unrelated. | These gain controls use a trailing Attenuation, where MixerParameterGainMeasured selects direct submission. The mixerduck position controls additionally measure per-frame gain ramps before another unit, including retained rounding between blocks. Source fades and Send ramps are separate mechanisms and remain. Other releases retain an explicitly unmeasured attenuation ramp. |
| Initial snapshot | A saved Other selection reads -12 dB on load even when Base is the serialized start snapshot. Explicit Base transition changes it on the next update. Original wake_snapshot has the same mismatch at -6 dB, before its wake request. | MixerSnapshotSelectionMeasured selects the saved target snapshot with start-snapshot fallback. Other releases retain start-snapshot initialization until measured. |

Both independent Native captures agree bit-for-bit on all **84,935,303 values per
rate** before publishing one canonical `state-rate-<rate>` reference. This includes
all parameter observations, all complete PCM/master/listener streams and metadata.
The engine has no raw master, listener or parameter failures in this profile. Its
only raw failure at each rate is the Reverb PCM sample listed below.

## Retained raw failures

The original comparator and its ATOL `2e-5` / RTOL `2e-4` are unchanged. `gate.py`
writes a complete raw report for every original profile and both new control profiles,
then `acceptance.py` classifies only these specific records. Unknown failures,
truncated reports and a changed reset failure shape fail the gate. The suite keeps
the original strict comparison visible, including its remaining 2049 failures per
rate, and also runs the state gate.

The original `reset` case calls AudioSettings.Reset at block 384 while the mixer is
asleep, restarts capture and calls Play in the same frame. This is a device reset,
not a suspension/wake control. Its remaining **2049 raw failures per rate** are:

- `reset.listener` metadata: Native has 1049600 stereo frames, engine 1048576.
- `reset.listener` samples 2097152 through 2099199: Native's additional callback
  block, near `+/-1e-20`, has no engine counterpart. Every missing value stays in
  the raw report.

The separate reset-playback experiment already records inconsistent independent
Native repeats. No fixed callback count or special replay rule is inferred from
that experiment. Generated-data loss now resolves replay, as [RESET_DATA_MEASUREMENT.md](RESET_DATA_MEASUREMENT.md) records. Device-reset listener delivery remains a functional compatibility
follow-up; these 2049 records are not classified as insignificant precision error.

The state profile has one insignificant device-PCM difference per rate, in
`reverb_disabled`: sample 46619 at 48000 Hz is Native `-3/32768`, engine `-2/32768`;
sample 28553 at 44100 Hz is Native `-3/32768`, engine `-4/32768`. Each differs by
one 16-bit step, `3.0517578125e-5`; every corresponding master/listener float passes.
Rounding near the PCM boundary is the suspected cause. These exact sample positions
and the one-step bound are checked without changing the original comparator.
