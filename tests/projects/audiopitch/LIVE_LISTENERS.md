# Live listener connections

Unity 6000.0.71f1 on Linux, the NVIDIA driver, a private eight-channel
PulseAudio device, 48000/44100 Hz and 1024-frame DSP periods. These measurements
separate the hearing pose from the connected listener effects.

## Contracts and observations

| Basis | Distinguishing control |
|---|---|
| The [Audio Listener manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioListener.html) requires one active listener. It describes positional processing for 3D sources and says 2D sources ignore that processing. | A constant source at the origin, a near listener at zero and a far listener at 100. Linear attenuation ends at distance 10. The pose diagnostic changes 2D to 3D before switching listeners, so selecting a different pose changes the output. |
| The manual gives no order guarantee for simultaneous listeners. | Two active listeners, disable/re-enable, setting an already enabled listener to true, movement, GameObject deactivation and destruction are recorded separately. Their observed selection is not a universal ordering contract. |
| [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) inserts a filter in Inspector order, with interleaved samples on the audio thread. [Behaviour.enabled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Behaviour-enabled.html) controls behaviour updates. | An identity script is added after eight excited buffers, disabled immediately, enabled at block 16 and disabled at block 24. Its channel recorder stays zero during the disabled period and becomes nonzero while enabled. Existing component histories distinguish node creation from later activation. |
| Connection and creation histories below are release measurements. | A component with no audio callback is added, removed and added again. Source-side controls distinguish this from a general restart on every GameObject edit. Disabled-listener controls keep another listener active, with a matching steady control. |

## Hearing pose and messages

The separate 48000 Hz Stereo pose diagnostic has 48 exact boolean observations
in two independent GPU Editor captures. All 48 pass in both the earlier engine
and the corrected engine.

With two active listeners, the most recently enabled pose hears in this capture.
Re-enabling the near listener makes the source audible; re-enabling the far
listener makes it silent. Assigning true to an already enabled listener keeps
the pose. Disabling or deactivating the selected listener falls back to the
remaining listener. A single far listener becomes audible after moving to the
origin. No active listener produces silence. The 2D control stays audible with
the far listener selected.

The Editor reports:

```text
There are 2 audio listeners in the scene. Please ensure there is always exactly one audio listener in the scene.
```

Every run also retains per-phase main/threaded message counts in
`warning-counts.jsonl` and all six rendered buffers per phase as raw PCM. The
presence check uses the last buffer and a `1e-6` threshold, below one 16-bit PCM
step. It does not compare waveform precision. Warning cadence remains
unqualified: these Editor runs repeat messages while the engine reports count
changes. The boolean reference records that a message was received, without
turning the sampled update cadence into a timing contract.

## Component and script connections

The complete `topology-diagnostics` profile now has 30 controls and 4,587,849
metadata/sample values per rate. Independent Editor repeats agree exactly.
Every value and PCM byte of the original six controls remains unchanged.

In these controls, ordinary component additions reconnect the listener's units,
including additions without an audio callback. The owner's units rejoin after
the already connected units. An addition to a disabled listener can reconnect
its effects while another listener supplies the hearing pose. Removing the
neutral component preserves the surviving histories. The source-side neutral
component does not change its audio chain.

The engine remembers node creation during runtime component attachment to an
active GameObject. Attachment to an inactive GameObject or a prefab asset is
unmeasured: it neither reconnects listener units nor creates a script node, and
keeps the prior first-activation path. Two engine unit tests go beyond these
controls and are unmeasured: an OnAudioFilterRead script on a disabled listener
runs again after that listener reconnects (the disabled-listener controls carry
Echo/Chorus only), and a source script added and destroyed before the next audio
update still restarts the source's units once.
The recorded immediate add/disable sequence retains this node. Enabling it later
does not reconnect the other units. The engine preserves its prior first-activation path for a script loaded
disabled. Serialized and clone publication still need Editor controls.

The engine keeps connection order separately from enabled pose registration,
publishes component positions with the matching immutable script plan and
queues history changes once. Connection updates reuse an owner node; unchanged
frames reuse the snapshot without allocations. Existing
`Compat.AudioComponentActivationMeasured` and `AudioListenerChainMeasured`
select this release. Other releases retain their previous unqualified listener
collection and history paths. The wire layout and cache formats do not change.

| Complete diagnostic | Before gate failures | After gate failures | After strict failures |
|---|---:|---:|---:|
| 48000 Hz | 895702 | 2418 | 3222 |
| 44100 Hz | 893991 | 3398 | 4422 |

The remaining values are confined to initial Mono Echo histories. The original
six-control slice falls from 123337/123586 gate failures to 610/864. No Master
or metadata failures remain in this complete diagnostic. The existing full
135-control `mono-live` comparison remains at 29638/31048 gate failures with
unchanged bounds; startup and composed-source precision remain separate work.

The `listener-live` gate qualifies 16 controls, with **2,621,632 values per
rate passing strictly**. It covers one active listener: Mono/Stereo/Quad neutral
edits and runtime script creation, plus Stereo/Quad disabled-listener
reconnection. Independent captures agree exactly with their parent diagnostic
samples. Its canonical PCM links to that parent after byte verification.
Simultaneous-listener ordering is kept in the complete diagnostic, outside this
gate. The existing 21 recipes remain unchanged and the live recipe is appended.

## Reproduction and remaining scope

Record two independent captures with `native.py --release 6000.0.71f1 --rate
RATE --set topology-diagnostics --work FRESH_CACHE_DIRECTORY`. Use `--set
listener-live` for its qualified subset. Publish with the matching
`publish.py --topology-diagnostics` or `--listener-live`, `--capture A/capture`
and `--repeat B/capture`. Replay with `context_run.py --profile listener-live`
or `--profile topology-diagnostics`; both return their actual comparison result.

For pose recording, `listener_pose.py record --release 6000.0.71f1 --work A`
requires `PULSE_SERVER` to identify a private server. Start PulseAudio with
`-n --daemonize=no --exit-idle-time=-1 --use-pid-file=no --system=false`, a
private native-protocol Unix socket, and a null sink at 48000 Hz with eight
channels. Use separate `PULSE_RUNTIME_PATH` and `PULSE_STATE_PATH` directories,
restart it for the independent repeat, and unload its numeric null-sink module
index before stopping the server. The existing `native.py` recorder uses this
setup automatically for the component profiles.

Publish the two pose roots with `listener_pose.py publish --release
6000.0.71f1 --a A --b B`. Replay with `listener_pose.py engine --release
6000.0.71f1 --runtime IMMUTABLE_RUNTIME --work FRESH_CACHE_DIRECTORY`. This is
an additional diagnostic, not a normative gate for invalid simultaneous-listener
order. Its canonical observations are under `Reference/unity-6000.0.71f1/listener-pose`.

Mono isolated Echo startup, sparse composed-source precision, serialized/clone
publication histories, special component replacement, warning cadence and
contexts beyond these measured listeners remain unqualified. The captures use
one backend and the NVIDIA driver, not multiple physical hardware drivers.
