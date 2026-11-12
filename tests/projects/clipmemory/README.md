# Created clip memory accounting

The 81-value reference comes from two independent Unity 6000.0.71f1 Editor
captures on the existing NVIDIA display. Five size/channel/rate shapes cross
requested streaming with reader-callback presence. Samples, channels, frequency
and raw memory bytes are compared exactly. The odd-rate extensions preserve all
49 and then all 65 earlier rows byte-for-byte.

```bash
python3 tests/projects/clipmemory/run.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/clipmemory/run.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/path/to/immutable/unbound python3 tests/projects/clipmemory/run.py check --release 6000.0.71f1
```

[AudioClip.Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.Create.html)
describes the stream option and callback-driven reads.
[Profiler.GetRuntimeMemorySizeLong](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler.GetRuntimeMemorySizeLong.html)
reports the object's memory bytes. The allocation rule is measured here:
requested streams account for two 400 ms buffers, whether a callback is present
or absent. Each buffer truncates its own frame count before doubling. Rates
32002 and 32003 distinguish this from truncating 800 ms once or rounding to a
four-frame boundary. Non-streamed clips account for their sample data instead.

A short clip initializes audio before the matrix. Tiny-clip and first-use
accounting remain in the strict appfacts report. This correction retains the
existing object/channel header model and stores the creation option separately
from the callback playback strategy. It changes runtime profiler bookkeeping;
no imported or cached asset representation changes.

Compat selects this mechanism for 6000.0.71f1. Other releases retain the previous
unmeasured memory rule. Imported and cloned clips, downloaded resources, playback
buffer lifetime and the remaining appfacts object/texture totals are separate
scope. The fixture does not play audio.
