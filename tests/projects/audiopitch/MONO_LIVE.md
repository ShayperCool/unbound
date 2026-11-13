# Live Mono and component topology diagnostics

Unity 6000.0.71f1, Linux, 48000/44100 Hz, 1024-frame DSP buffers, and a private
eight-channel device. Two independent GPU Editor captures agree exactly on
18,482,317 metadata and sample values per rate in `mono-live`, and 917,566 per
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
| Component membership and duplicate rejection are release measurements. | Add/remove/re-add a zero-distortion unit, an initially disabled unit, or an identity script after playback starts. Six steady zero-distortion controls are bit-identical to the matching Echo/Chorus controls. Each of the six filter types rejects a second instance, retains the first even when disabled, and permits a replacement after destruction. |

`mono-live` contains 129 controls. All prior 102 controls retain every metadata
value and PCM byte after three owner-pulse and 24 single-unit controls are added.
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
float bounds and the existing one-step device PCM gate apply. Engine admissions
and remaining differences are recorded with the implementation results.
