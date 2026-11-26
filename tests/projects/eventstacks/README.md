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

`changes.*`: the calls of events whose listeners add or remove listeners while it runs, or invoke
the event again (each row lists the calls in order, `|` between two Invokes).

What it shows: the exception's stack keeps the listener's frame and lists Unity's frames
between it and the caller (`InvokableCall.Invoke`, `InvokableCall`1.Invoke`,
`CachedInvokableCall`1.Invoke`, `UnityEvent.Invoke`), with their places in Unity's source; the
listeners after a throwing one do not run. A listener added or removed during an Invoke changes
the next Invoke only, except that an Invoke of the same event from a listener makes the outer one
go on over the event's listeners as they are then.

Measured in the Unity 6000.0.71f1 Editor on Linux in batch mode (two byte-identical captures).

Record and gate:

    python3 tests/projects/eventstacks/run.py record --label a   # twice, then
    python3 tests/projects/eventstacks/run.py publish a b
    python3 tests/projects/eventstacks/run.py gate
