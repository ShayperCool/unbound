# Six-input Stereo voices and output devices

Unity 6000.0.71f1 on Linux, 48000/44100 Hz and 1024-frame periods. The measured voice scope is non-streamed six-channel clips on 2D sources with Stereo or Prologic output. Source-processing controls use a private eight-channel PulseAudio device; separate encoding controls use private 2-, 4-, 6- and 8-channel devices.

## Contracts and controls

| Basis | Distinguishing control |
|---|---|
| [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) receives interleaved samples and runs in Inspector order on the audio thread. Unity API calls are restricted there. | Identity taps before and after a nonzero Distortion filter record the complete callback stream. Mono and stereo clips provide controls outside the six-input case. The callback copies into a preallocated buffer; file writes happen on the main thread after offline rendering. |
| [AudioClip channels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip-channels.html), [SetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.SetData.html) and [GetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.GetData.html) expose interleaved clip data. These clips are generated and non-streamed. | All six independent noise columns are read back before playback and retained alongside every Master, device and source-callback sample. |
| [panStereo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-panStereo.html) applies to mono/stereo sounds. [Volume](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-volume.html) controls source level. | Six-input left/right pan records actual behavior without extending the documented input scope. Live pan and volume changes occur after eight excited buffers, so a retained center contribution and a volume change before nonlinear processing alter the result. |
| [driverCapabilities](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings-driverCapabilities.html) describes the current driver; [Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) selects the closest supported configuration. [Prologic](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.Prologic.html) describes matrix encoding. | Independent processes capture Stereo and Prologic, including all six isolated impulses and dense noise, on four device widths. Two output channels alone are not treated as proof of fallback. |

## Voice processing

Thirty-six controls cover mono/stereo/six-channel clips with a tap before or after source Distortion, six-channel group Distortion, static and live pan/volume, pitch and one-shot-only playback. Source and group Distortion use the same 0.7 level. Two untapped source-Distortion controls check processing without the recorder component. Their complete Master and device PCM is byte-identical to the tapped source-Distortion controls in both modes and rates. Each mode starts in a separate process.

Independent Editor repeats agree exactly on all **11,665,952 values per rate**. Every callback has two channels, 32 callbacks and 32768 frames, including the six-channel clips. The earlier thirty controls retain identical metadata and PCM after four live controls are added; all thirty-four remain unchanged after the untapped controls are appended. All 56 Editor sessions across the original, extended and device captures use the GPU and have no Editor assertions.

Decoded interpolation retains all six inputs. A per-voice matrix then reduces them to Stereo before source components and scripts. With centered pan, the coefficients are the six basis responses in [ENCODING.md](ENCODING.md). Pan changes front/rear side gains; center and LFE keep their contributions in both outputs. All coefficients share the existing source-volume and 64-sample transition rules. A fold followed by ordinary stereo balance would incorrectly pan the center/LFE terms.

`Compat` selects `AudioOutputRules.six_channel_stereo_voices`. The implementation reuses the voice's matrix and ramp, with shared decoded interpolation for ordinary and six-input voices. Main and one-shot voices select the matrix from their own clip. No audio-thread allocation or lock is added. Rust controls also cover 32000 Hz, 97-frame periods, live pan, incomplete trailing input, empty clips, interpolation endpoints and zero render allocations.

All **11,665,952 engine values per rate pass strictly**, including every source callback, Master and device sample. The `.source_float` stream uses ATOL `2e-5` / RTOL `2e-4` even in a one-step device comparison. No allowance or frame limit is added. The original thirty-control engine diagnostic retains its 3,536,836 failures per rate outside Git; those before results are not rewritten.

## Output devices

Each device width has two independent projects per rate and two mode sessions per project. The four encoding controls retain all six input streams, six isolated channel impulses and dense noise.

| Private device channels | Capability getter | Native repeat values per rate | Stereo/Prologic paired sample values per rate | Engine result |
|---:|---|---:|---:|---|
| 2 | Stereo | 1,310,770 exact | 655,360 exact | All values pass strictly |
| 4 | Quad | 1,310,770 exact | 655,360 exact | All values pass strictly |
| 6 | 5.1 | 1,310,770 exact | 655,360 exact | All values pass strictly |
| 8 | 7.1 | 1,310,770 exact | 655,360 exact | All values pass strictly |

The six impulse coefficients remain the ordinary Stereo response in both modes on all four device widths. Fresh eight-channel captures also match the earlier canonical reference exactly. New references are under `Reference/unity-6000.0.71f1/encoding-devices/device-C/rate-RATE`; the earlier eight-channel reference stays byte-identical under `encoding/rate-RATE`. Earlier engine encoding diagnostics retain their 262,014 gate failures per rate and device width; the new implementation passes all values strictly.

These are different layouts on the same backend, not measurements of multiple hardware drivers. Physical ALSA playback endpoints exist, but the current account cannot open them. The machine's default sink is unchanged; `PULSE_SERVER` selects a private server and cleanup unloads its sink by numeric module index.

## Remaining scope

**Q416-R5a** covers six-input spatial sources, other output layouts, streamed six-channel clips, other input widths and arbitrary graph histories. Those contexts retain the previous unqualified first-two-input path; this change does not certify them. Other releases retain that path too, explicitly unmeasured. Physical-device PCM, other backends, setting pairs and releases remain unmeasured. No encoded-output reference is available for those contexts.

## Reproduction

Record each voice repeat with `native.py --release 6000.0.71f1 --rate RATE --set voice-input --work FRESH_CACHE_DIRECTORY`. Publish only after exact agreement with `publish.py --voice-input --release 6000.0.71f1 --rate RATE --capture A/capture --repeat B/capture`. Replay with `context_run.py --release 6000.0.71f1 --profile voice-input --unbound MATCHING_CLI`.

For device layouts use `native.py --release 6000.0.71f1 --rate RATE --set encoding --device-channels C --work FRESH_CACHE_DIRECTORY` twice. Publish widths 2, 4 and 6 with `publish.py --encoding --device-channels C` and the same release/rate/capture/repeat arguments. Replay each width with `context_run.py --profile encoding --device-channels C`. The existing Prologic gate validates all four recorded availability relationships before its original mono/stereo-input replay.

All eighteen audiopitch recipes remain in the gate. Existing canonical references, authored inputs and comparator bounds stay unchanged.
