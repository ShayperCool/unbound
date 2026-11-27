# Microphone probe (`audiomic`)

Measures `UnityEngine.Microphone` in Editor Play mode on Unity 6000.0.71f1 (Linux with PulseAudio/PipeWire):

- **Device enumeration & capabilities (`devices.*`, `caps.*`)**: `Microphone.devices` non-empty, unique names; `GetDeviceCaps` for `null`, `""`, `devices[0]`, and an unknown device name (`0..0` on Linux PulseAudio, indicating any frequency supported).
- **Idle state (`idle.*`)**: `IsRecording` (`false`), `GetPosition` (`0`), and silent `End` before any recording starts.
- **Validation & boundaries (`err.*`)**: unknown device name, `lengthSec <= 0`, `lengthSec == 3600` (logs `"Length of the recording must be less than one hour"` and returns `null`), `lengthSec == 3601` (throws `ArgumentException`), and `frequency <= 0`.
- **Non-looping recording (`rec1.*`)**: mono (`channels = 1`) `"Microphone"` clip metadata (`DecompressOnLoad`, `Loaded`), `null`/`""`/`devices[0]` aliasing for `IsRecording` and `GetPosition`, sample position advancement, `GetData` while recording, automatic stop with position `0` after `lengthSec` elapses, `GetData`/`SetData` readback and `AudioSource` playback after `End`.
- **Looping & double-start (`rec2.*`, `rec3.*`)**: looping recording continues past `lengthSec` with wrapped position; calling `Start` again on the same device replaces the recording; `End(devices[0])` stops a recording started with `""`.
- **Lifecycle stops (`destroy_while_rec.*`, `reset.*`)**: `DestroyImmediate(clip)` and `AudioSettings.Reset` both stop an active recording.

## Unity 6000.0 contract

- Scripting API `Microphone` (`https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Microphone.html`), `Microphone.Start`, `Microphone.End`, `Microphone.IsRecording`, `Microphone.GetPosition`, `Microphone.GetDeviceCaps`, `Microphone.devices`.
- `Compat.MicrophoneMeasured` (`ReleaseFamily.Unity6000_0`) gates the 6000.0-measured `lengthSec == 3600` native error return; other release families throw `ArgumentException` for `lengthSec >= 3600`.
- On macOS (and other non-Linux OSes), audio capture is not yet implemented and warns once while returning an empty device list.

## Usage

```bash
python3 tests/projects/audiomic/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/audiomic/run.py publish run-a run-b
python3 tests/projects/audiomic/run.py gate
```

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical Unity 6000.0.71f1 Linux Editor batch captures (`118` rows plus `complete`).
