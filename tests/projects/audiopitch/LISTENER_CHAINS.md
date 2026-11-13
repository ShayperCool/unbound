# Listener components and scripts

Unity 6000.0.71f1 on Linux, 48000/44100 Hz, 1024-frame DSP periods and an isolated
eight-channel device. The independent `listener-chain` profile has 36 controls
in Stereo and Quad. The separate `listener-diagnostics` profile has ten controls
with a disabled, switched or simultaneous second listener.

| Basis | Distinguishing control |
| --- | --- |
| The 6000.0 [audio filter manual](https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/filters/class-audio-effect) describes component order and enabled state. [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) places scripts in Inspector order and supplies interleaved samples. | An affine script applies a gain and offset before or after each of Low Pass, High Pass, Distortion, Echo, Chorus and Reverb. Every paired order changes the captured output, including startup for filters that retain DC. |
| Script and component positions form one processing chain. | Dry controls isolate the script, two adjacent scripts distinguish script order, and an Echo between two scripts distinguishes interleaved placement. A temporary layout observer is removed after preflight so it cannot add an unrequested script stage. |
| [bypassListenerEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassListenerEffects.html) applies to unrouted sources and does not bypass listener effects on mixer-routed sources. | Routed and unrouted controls compare bypass off/on with both a script and an Echo. Every capture retains full Master and device PCM. |
| The [Audio Listener manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioListener.html) requires one listener for proper operation. It specifies no global order for several active listeners. | The diagnostic uses different affine scripts on two listeners, including controls with Distortion on one and Echo on the other. Disabled and switched controls keep one active listener; simultaneous controls record the unsupported scene configuration without establishing an ordering guarantee. |

Two independent GPU Editor captures agree exactly on all 6,816,040 metadata and
sample values per rate in `listener-chain`. The ten-control diagnostic agrees
exactly on all 1,966,172 values per rate. Earlier 36-control and six-control
captures retain identical metadata and raw samples when the extra diagnostic
controls are appended. Existing profiles, authored asset bytes, references and
comparator bounds remain unchanged.

Each active listener contributes its scripts and components to the final mix.
The simultaneous diagnostic runs both listeners' scripts, even though only one
listener pose is selected. This observation does not select a universal order
between GameObjects. The engine keeps its existing registration order without
adding a global sort; normal content should follow Unity's one-listener contract.
Listener filters sharing a GameObject with an AudioSource remain unsupported.
Live membership histories, other output settings and other releases require
separate measurements.

Record two fresh projects for each profile and rate with:

```bash
python3 tests/projects/audiopitch/native.py --release 6000.0.71f1 \
  --rate RATE --set listener-chain --work FRESH_CACHE_DIRECTORY
```

Use `--set listener-diagnostics` for the separate diagnostic. Compare independent
captures with `compare.py --profile PROFILE --exact`, then publish with
`publish.py --listener-chain --repeat REPEAT` or `--listener-diagnostics`.
Both publishers require complete, identical metadata and raw samples. Replay
with `context_run.py --release 6000.0.71f1 --profile PROFILE`. The diagnostic
returns its actual full comparison result; it does not admit a public guarantee
for simultaneous listeners. Reports retain every failure and no frame limit,
alignment or additional tolerance is introduced.

The existing `Compat.AudioListenerChainMeasured` selection enables component
positions among scripts and active-listener DSP membership for this release.
NativeApi 67 carries an immutable script-plan identifier and script ranges on
listener callbacks. Publication keeps the old plan available until the matching
frame is installed; callbacks never combine a new array with old component
positions. Unchanged listener-script publication reuses its plan without
allocating. Components keep their existing shared kernels and per-unit histories.
Offline scripts retain complete DSP buffers; device callbacks retain their
existing sizes. Processing adds no audio-thread allocation or lock. Other
releases keep their previous component/script rejection and hearing-listener-only
script collection until separately measured.

The corrected engine passes all 6,816,040 values per rate in `listener-chain` at
the original gate. Strict reports retain 174 device-only differences at 48000 Hz
and 53 at 44100 Hz, each within the existing one-step PCM allowance. Every Master
float passes ATOL `2e-5` / RTOL `2e-4`. The complete ten-control diagnostic passes
all 1,966,172 values per rate even under the strict float bounds; it remains a
measurement of these scenes, not a contract for simultaneous listeners.

The prior engine retains 2,701,678 and 2,702,267 gate failures in the 36-control
profile at 48000 and 44100 Hz. Its earlier six-control listener diagnostic
retains 196,548 gate failures per rate. Those six controls precede the four
additional component-chain diagnostics; no baseline result is claimed for the
latter four. All raw before/current captures and complete reports stay outside
Git. The earlier profiles and tolerances are unchanged.
