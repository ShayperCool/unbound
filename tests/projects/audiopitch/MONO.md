# Mono effect routing

The `mono` profile records Unity 6000.0.71f1 at 48000 and 44100 Hz with
1024-frame DSP buffers on an isolated eight-channel device. The 64 controls
include 60 Mono-output cases and four Stereo-output controls with mono clips.
Both independent GPU Editor captures agree exactly on 4,456,925 values per rate.
All prior 58 controls retain their metadata and PCM after six chain controls
are appended. Existing canonical references remain unchanged.

## Contracts and measurements

The [AudioSpeakerMode API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html)
defines Mono as one output channel and allows the device to override a requested
mode. Every capture checks the requested mode and actual render width. A clipless
source records its actual callback width, which is one in Mono output.
The [Chorus Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html)
describes separate wet taps; their allocation to Mono output is a release
measurement, not a documented channel-routing guarantee.

| Mechanism | Controls and observed result |
|---|---|
| Chorus | Mono keeps the first wet tap; tap-two/tap-three isolation produces no wet output. Group, source and listener components agree for mono and stereo clips. Mono clips on Stereo output retain the stereo tap allocation. |
| Reverb | Mono retains both reflection paths of the shared network and folds their output before the next effect. Reverb-to-Chorus, Chorus-to-Reverb, child-to-parent routing, source-to-group and group-to-listener controls distinguish this placement from folding only at device output. |
| Wet slots | Distortion, Chorus, Flange, Normalize, Pitch Shifter and SFX Reverb cover -6/0/+6 dB and live levels. Mono's connection deadband counts one active channel; -64/-63 dB controls distinguish retained zero wet output from an audible wet path. Dry gain still follows its requested complement. A quiet SFX Reverb control covers +30 dB. |
| Linked processing | Normalize sees one active sample. Send advances once per mono sample and retains its accumulated endpoint; Receive and Duck observe the same single-channel stream. Live Send, postfader placement and sends after Chorus/Reverb have separate controls. |
| Components and zones | Source/listener Chorus and Reverb, a static User zone, mono/stereo clips and a procedural source exercise shared routing without tying behavior to clip channel count. Live zone/overlap behavior in Mono is outside this reference. |

`Compat.AudioSurroundEffectsMeasured` selects the measured channel mechanism.
The engine stores mono samples in two equal internal lanes; that storage does
not add a second active channel. No buffers are allocated while processing.
Other releases retain their previous two-lane approximations pending measurement.
Normalize and static wet admission expands only to the two measured Mono setting
pairs; the previously measured plain Pitch Shifter buffer sizes remain admitted.
Other Mono rates/buffers for these newly admitted effects remain rejected.

## Reproduction and results

Record independent `native.py --set mono` sessions per rate and publish one with
`publish.py --mono --repeat <other capture>`. `context_run.py --profile mono`
replays the committed authored inputs. `gate.json` selects the release explicitly.
The engine passes every value using the unchanged float and device tolerances.
Strict reports retain 329 device samples at 48000 Hz and 401 at 44100 Hz that
differ by one 16-bit step. Every master-sink float passes ATOL `2e-5` / RTOL `2e-4`.
The profile preserves all 1,820 prior authored inputs and adds 128 inputs.
Additional effect kinds, topology edits, other releases and settings outside the
listed profile remain unmeasured. Full raw comparisons preserve every difference.
