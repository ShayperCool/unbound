# Live Mono and component topology diagnostics

Unity 6000.0.71f1, Linux, 48000/44100 Hz, 1024-frame DSP buffers, and a private
eight-channel device. Two independent GPU Editor captures agree exactly on
19,399,899 metadata and sample values per rate in `mono-live`, and 917,566 per
rate in the separate `topology-diagnostics` profile. Earlier references are
unchanged. These diagnostics retain their complete PCM and metadata comparisons;
they do not establish universal graph-edit or multilistener ordering guarantees.

## Contracts and controls

| Basis | Distinguishing controls |
| --- | --- |
| [Use audio filters](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/filters/class-audio-effect), Unity 6000.0, specifies Inspector order and whether an enabled filter applies. | Echo/Chorus, Lowpass/Chorus and Reverb/Echo on sources and listeners. Existing histories are excited before toggling the first unit at blocks 8, 16 and 24. Separate steady and single-unit cases identify composition and startup differences. |
| [AudioReverbZone](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbZone.html) specifies full application at the inner radius, no application at the outer radius, and combined zones. | Mono listener/zone movement, radii, scalar and preset edits, and overlapping zones. Stereo and Quad overlap controls exercise the same mechanism outside Mono. Dry controls and positions before and after crossing a boundary distinguish the documented states. |
| [AudioSpeakerMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html) allows the output device to override the requested mode. | Every case checks the actual renderer width after preflight. The topology matrix uses Mono, Stereo and Quad output, including mono clips and a one-shot-only source. |
| [AudioListener](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioListener.html) calls for one active listener. | Owner-disable controls swap to a plain listener, and owner-pulse controls disable and re-enable the same listener before the next audio update. The separate six-case profile deliberately uses two listeners to investigate retained histories; its observed ordering is not a public guarantee. |
| Component membership and duplicate rejection are release measurements. | Add/remove/re-add a zero-distortion unit, an initially disabled unit, or an identity script after playback starts. Separate script activation controls retain the same output as the steady chain across disabling and re-enabling. Six steady zero-distortion controls are bit-identical to the matching Echo/Chorus controls. Each of the six filter types rejects a second instance, retains the first even when disabled, and permits a replacement after destruction. |

`mono-live` contains 135 controls. All prior 129 controls retain every metadata
value and PCM byte after six script activation controls are added. Every older
102-control value also remains unchanged after owner-pulse and single-unit
controls were added.
The diagnostic also records log callback counts during duplicate attempts;
console text and callback publication remain distinct observations.

## Reproduction

Record two fresh private projects per rate with:

```sh
python3 tests/projects/audiopitch/native.py --release 6000.0.71f1 --rate RATE --set mono-live --work FRESH_DIRECTORY
python3 tests/projects/audiopitch/native.py --release 6000.0.71f1 --rate RATE --set topology-diagnostics --work ANOTHER_FRESH_DIRECTORY
```

Publish each pair with `publish.py --mono-live` or `--topology-diagnostics`,
`--release`, `--rate`, `--capture`, and `--repeat`. Both metadata and raw PCM must
match exactly. References are under the corresponding profile directories in
`Reference/unity-6000.0.71f1`.

`context_run.py --profile mono-live` and `--profile topology-diagnostics` replay
the complete profiles and return their actual comparison results. Neither profile
adds a tolerance, sample alignment, frame limit, or residual allowance. Existing
float bounds and the existing one-step device PCM gate apply. The qualified PCM scope and remaining differences are listed below.


## Live history PCM scope

The separate `topology-pcm` profile contains all 57 Echo/Chorus history controls
and all 17 live-zone controls. It covers Mono, Stereo and Quad, source and
listener components, mono clips, one-shot-only playback, component additions and
removals, initially disabled components, identity scripts and script activation,
and listener disable/re-enable histories with one active listener. Two fresh
captures per rate agree exactly on 10,093,276 values. Their complete metadata and
PCM also match those controls in the full 135-control profile. The separate case
manifest links to that existing canonical PCM.

Listener component and script additions reconnect surviving units. Removing a
listener unit preserves its survivors. Source additions and removals reconnect
survivors. Disabling an existing script keeps its connected history while its
callback is bypassed. Listener disable/re-enable starts fresh histories even
when it happens before the next audio update. Component enabled-state changes
retain each bypassed unit's history. These are measurements in this release and
context, selected by `Compat.AudioComponentActivationMeasured`. Other releases
retain their previous unmeasured listener history and script activation paths.
NativeApi 69 carries one-frame reconnection and recreation commands with the
matching frame. Reused membership sets avoid rebuilding allocations on unchanged
frames. Existing LowPass state and Chorus sweep span survive a reconnection;
owner recreation clears both.

The engine passes the 74-control profile at the existing bounds, including every
Master float. Strict reports retain 1557/1609 device differences at
48000/44100 Hz; every difference fits the existing one-step device gate. The
profile is a PCM qualification for these kernels and histories, not an admission
of every component composition. The six filter kinds also reject duplicate
components, retain the original even when disabled, and allow a replacement
after destruction on this release.

Record with `native.py --set topology-pcm` twice per rate, then publish with
`publish.py --topology-pcm --capture FIRST/capture --repeat SECOND/capture`,
using the explicit `--release` and `--rate` arguments above. Both repeats and the
full-profile parent must match byte for byte before publication.
`context_run.py --profile topology-pcm` runs the engine gate. The full
`mono-live` and `topology-diagnostics` commands remain independent diagnostics
and return every actual failure.

## Remaining queue work

- **Q416-R4a, Mono Echo startup.** Isolated Mono Echo and Reverb/Echo composition
  retain startup differences in source and listener placements. Parameter
  publication and construction history need further controls; a fixed startup
  delay does not describe these cases. Single-unit Reverb controls distinguish
  this from a general Mono reverb folding defect.
- **Q416-R4b, composed source precision.** Stereo and Quad LowPass/Chorus and
  Reverb/Echo controls retain sparse Master and device differences. They are
  outside this new PCM qualification; their complete samples and original float
  bounds remain in `mono-live`.
- **Q416-R4c, diagnostic callback publication.** The full duplicate-filter
  diagnostic still differs on twelve log callback counts. The Editor reports
  zero callbacks during each immediate duplicate attempt; the engine reports
  one. Rejection, retained component identity and replacement agree. Console
  output and application log callback timing need a separate runtime change.
- **Q416-R4d, live changes with simultaneous listeners.** The six-case diagnostic
  remains open. Editing either owner's chain has different retained-history
  interactions. Static multiple-listener controls remain the earlier measured
  scope; Unity documents one active listener and gives no global ordering
  guarantee for these invalid simultaneous-listener setups.

The complete 135-control comparison retains 29650/31060 gate failures at
48000/44100 Hz, including 10604/11040 Master floats, 19034/20008 device samples
and twelve callback metadata values. The separate six-control comparison retains
123337/123586 gate failures out of 917566 values per rate. No tolerance, sample
range, oracle value or residual allowance was changed to exclude these failures.
