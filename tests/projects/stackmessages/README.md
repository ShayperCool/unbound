# Stack traces in script messages

Unity 6000.0.71f1 reports the active script caller in both a private Update
and an inherited LateUpdate. Two independent Editor runs agree on the full
UTF-8 traces after removing the project prefix and on the number of bytes
returned and written. Both callbacks are small enough to inline into a
managed dispatcher, which would omit their frames. The Update count checks
that the dispatch still happens exactly once before the capture completes.

The [Unity 6.0 Scripting API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Debug.ExtractStackTraceNoAlloc.html)
documents the current managed stack, UTF-8 byte count and path cleanup. This
probe measures the script entry frames. It does not require the absolute
project path or Unity's build-source path to be the same on every machine.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/stackmessages/run.py record --label first
python3 tests/projects/stackmessages/run.py record --label second
python3 tests/projects/stackmessages/run.py publish first second
python3 tests/projects/stackmessages/run.py gate
```
