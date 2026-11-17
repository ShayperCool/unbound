# Device facade

`UnityEngine.Device.Application`, `Screen` and `SystemInfo` are the Device Simulator-aware
versions of the classes of the same name. The [6000.0 Scripting API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Device.Screen.html)
documents that outside the Editor they behave exactly like `UnityEngine.Screen` (and likewise for
the other two), and that in the Editor they mimic a device when the Device Simulator is active. It
does not say what they answer in the Game view without the Simulator; this probe records that.

`Assets/Scripts/DeviceFacadeProbe.cs` runs five frames after the scene loads in Play mode and writes
one JSON line per observation:

- every public static property of the three Device classes against the same-named property of the
  UnityEngine class: `same` or `differs`, never the value itself, so the record does not depend on
  the machine. `SystemInfo.processorFrequency` is a live reading that may change between two reads;
  it is only checked to be positive on both sides. Whether each side has a setter, and which events
  have a counterpart, is recorded too;
- the methods, with fixed arguments (scene names, texture and graphics formats, a render texture
  descriptor, a null display layout list);
- controls where state is written through one class and read through the other, from a value that
  differs from the current one: `targetFrameRate` both ways, `runInBackground`,
  `SetStackTraceLogType`, `Screen.sleepTimeout` (its starting value is recorded too; the Linux Editor
  reads `NeverSleep`, -1, whatever is set, `SystemSetting` and a number of seconds included), and a log handler added through one
  class and removed through the other.

Measured: Unity 6000.0.71f1, Linux GPU Editor (NVIDIA), batchmode with graphics, Play mode, no
Device Simulator. Every property and every recorded method call answers the same as its counterpart,
and the writes listed above act on the counterpart. Not written cross-class, among others: the other
setters (`orientation`, `autorotate*`, `fullScreen`, `fullScreenMode`, `brightness`,
`backgroundLoadingPriority`); not called, among others: `SetResolution`, `MoveMainWindowTo`,
`GetTiledRenderTargetStorageSize`, `HasUserAuthorization`, `RequestUserAuthorization`,
`RequestAdvertisingIdentifierAsync`, `GetBuildTags`, `SetBuildTags`, `OpenURL`, `Quit`, `Unload` and
the obsolete `FormatUsage` overloads. Two independent captures were byte-identical;
`Reference/unity-6000.0.71f1.jsonl` is one of them (346 records). Unmeasured: the Device Simulator
(Unbound does not provide it) and the macOS Editor.

## Running

From the repository root:

```sh
python3 tests/projects/devicefacade/run.py native --label native1   # and native2: GPU Editor
python3 tests/projects/devicefacade/run.py publish native1 native2
UNBOUND_CLI=<build>/unbound python3 tests/projects/devicefacade/run.py check   # the land gate
```

`check` runs the engine headless and compares exactly with the reference; every missing, extra or
differing key is printed. Run output goes to `$XDG_CACHE_HOME/unbound/validation/runs/devicefacade/`.
