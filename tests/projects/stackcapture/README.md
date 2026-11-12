# Captured exception traces

Two independent Unity 6000.0.71f1 Editor captures on the existing NVIDIA display
agree on all 15 values. Direct, nested, unthrown-wrapper, empty-message and generic
exceptions distinguish captured frames from the current logging call stack.
The four added logging-policy values retain all 11 earlier rows byte-for-byte.

```bash
python3 tests/projects/stackcapture/run.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/stackcapture/run.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/path/to/immutable/unbound python3 tests/projects/stackcapture/run.py check --release 6000.0.71f1
```

[Debug.LogException](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Debug.LogException.html)
logs the exception and links its source locations. The captured formatting here
is a measured release behavior: innermost message first, named parameter types,
generic definitions, project-relative source locations, wrapper markers, then
the actual public logging entry and script callers. Runtime callback catches
have the captured throwing frame without a current script caller; testrunner
checks that path separately. Shared source mapping also handles a project
reached through directory links or a project view with linked source roots.

[Application.SetStackTraceLogType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application.SetStackTraceLogType.html)
defaults to ScriptOnly. The None controls start with exceptions that have a real
captured trace, change that setting, and observe an empty trace for both thrown
and unthrown exceptions. Their messages remain present.

The compatibility profile selects ScriptOnly and None exception formatting for
6000.0.71f1. Full and other releases retain the previous unmeasured runtime text.
Async state machines, exception-dispatch boundaries, external package paths, missing source metadata,
and exceptions originating in framework or engine APIs remain outside this
capture. The strict appfacts NoAlloc/StackTraceUtility failures remain separate:
this control does not establish raw buffer lengths, recover JIT-inlined frames,
or supply release-build source paths.
