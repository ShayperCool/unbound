# Script-created clip startup memory

Six Unity 6000.0.71f1 profiles cover a one-frame mono PCM clip, stereo callback PCM,
stereo streaming, audio reset before creation and with the first clip alive, and disabled audio.
There are 28 values from two agreeing macOS batch-mode Editor runs per profile. The Linux GPU
Editor reference in `appfacts` independently records 1,028 bytes on first creation and after
writing sample data. The reset and disabled-audio profiles are measured on macOS. The fixture
borrows a scene and configuration from `formatmapping`.

The first script-created clip reports 88 additional bytes across the three creation modes.
Reading the audio configuration and loading an imported clip leave that first-creation state
available. Writing PCM samples leaves the charge on the first clip. A clip created inside its
PCM reader callback has the ordinary size. Destroying the first clip leaves later
clips at the ordinary total. A successful reset consumes first-creation state before its
configuration callback creates a clip. If the first clip already exists, the retained difference
between it and an identical second clip remains 88 bytes after reset. The probe measures this
difference independently of the remaining per-object total after sample storage is released.
Disabled audio gives a zero difference. Its two absolute size discrepancies remain in the
strict report.

The Scripting API describes the native-memory result, clip creation and the callback from a
successful reset. The ownership and byte count are observations of this release.
[Profiler.GetRuntimeMemorySizeLong](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler.GetRuntimeMemorySizeLong.html)
[AudioClip.Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.Create.html)
[AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html)

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/clipstartupmemory/run.py record --profile PCM --label first
python3 tests/projects/clipstartupmemory/run.py record --profile PCM --label second
python3 tests/projects/clipstartupmemory/run.py publish --profile PCM first second
python3 tests/projects/clipstartupmemory/run.py gate
```

Repeat the recording commands for Callback, Stream, Reset, ResetOwner and Disabled. Publishing
checks the selected profile, the recorded release and two independent Editor runs. The gate
compares every profile exactly. Imported clip sizes, absolute totals of clips after reset and
other Unity families retain their previous behavior.
