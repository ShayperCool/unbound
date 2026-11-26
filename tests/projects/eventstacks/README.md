# Exceptions thrown by UnityEvent listeners

`Assets/Scripts/EventStacksProbe.cs` writes values.jsonl in `$EVENTSTACKS_OUT`
(tests/value_probe.py) for exceptions that UnityEvent listeners (`Assets/Scripts/EventListener.cs`)
throw: persistent listeners added with `UnityEventTools` (`persistent_void`, `persistent_int` with
a fixed argument, `persistent_dynamic` on a `UnityEvent<int>`, `persistent_deep` throwing in a
method the listener calls, `persistent_then_others` followed by a persistent and a runtime
listener) and runtime ones (`runtime_method`, `runtime_lambda`). For each case:

* `.extract`: `StackTraceUtility.ExtractStringFromException` of the exception caught around
  `Invoke` (the project's folder reads `<project>`);
* `.log_exception`: the message and stack text `Application.logMessageReceived` gets for
  `Debug.LogException` of it;
* `.unhandled`: the same for the exception thrown out of `Update`;
* `.ran_after`: how many of the listeners after the throwing one ran.

`persistent_void_on_int`: a persistent listener without arguments on a `UnityEvent<int>`.
`log.*`: `Debug.Log`'s message and stack text from listeners; `extract.*`:
`StackTraceUtility.ExtractStackTrace` asked by listeners; `log_exception_inside.*`:
`Debug.LogException` of an exception a listener catches; `coroutine.unhandled`: the log of an
exception a coroutine throws after its first step.

`changes.*`: the calls of events whose listeners add or remove listeners while it runs, or invoke
the event again (each row lists the calls in order, `|` between two Invokes); `changes.nested_*`:
a listener that adds, removes, removes all, or turns a persistent listener off, then invokes the
event again.

What it shows: the exception's stack keeps the listener's frame and lists Unity's frames
between it and the caller (`InvokableCall.Invoke`, `InvokableCall`1.Invoke`,
`CachedInvokableCall`1.Invoke`, `UnityEvent.Invoke`), with their places in Unity's source; the
listeners after a throwing one do not run. The stacks of a running call that the Editor writes
natively (`Debug.Log`, `Debug.LogException`'s caller lines) leave the `InvokableCall` frames out
and give `UnityEvent.Invoke` no place; `ExtractStackTrace` lists them. A listener added or removed
during an Invoke changes the next Invoke only; an Invoke of the same event from a listener after
an addition makes the outer one go on over the event's listeners as they are then, after a
removal over the ones it started with.

Measured in the Unity 6000.0.71f1 Editor on Linux in batch mode (two byte-identical captures).

Record and gate:

    python3 tests/projects/eventstacks/run.py record --label a   # twice, then
    python3 tests/projects/eventstacks/run.py publish a b
    python3 tests/projects/eventstacks/run.py gate
