# Stack traces in script messages

Unity 6000.0.71f1 reports the active script caller in both a private Update
and an inherited LateUpdate. A concrete IEnumerator Start and a non-void
LateUpdate check the cases where the dispatcher adapts a return value. Two
independent Editor runs agree on the full UTF-8 traces after removing the
project prefix and on the number of bytes returned and written. The callbacks are small enough to inline into a
managed dispatcher, which would omit their frames. The Update count checks
that the dispatch still happens exactly once before the capture completes.

The Editor calls the concrete-return Start and leaves its enumerator
unscheduled. Unbound advances it once. The probe reports this existing
difference, and the managed test checks that dispatch preserves the returned
instance. Callbacks returning a function pointer use a direct call and may
still lose their entry frame to inlining.

The [Unity 6.0 Scripting API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Debug.ExtractStackTraceNoAlloc.html)
documents the current managed stack, UTF-8 byte count and path cleanup. This
probe runs release scripts with the optimizing JIT. The project prefix is
removed before counting bytes, so the traces compare across capture paths.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/stackmessages/run.py record --label first
python3 tests/projects/stackmessages/run.py record --label second
python3 tests/projects/stackmessages/run.py publish first second
python3 tests/projects/stackmessages/run.py gate
```
