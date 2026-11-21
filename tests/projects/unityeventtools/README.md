# unityeventtools

What `UnityEditor.Events.UnityEventTools` does to an event's persistent listeners in Play mode
(Unity 6000.0.71f1): each case edits a fresh `EventHost`'s event (`Assets/Scripts/EventHost.cs`)
and records what the edit throws and logs, every stored persistent call (target, target type,
method, mode, state and argument, read from the serialized fields), and what invoking the event
then calls on `EventReceiver` and logs.

* `add.*`: `AddPersistentListener` for an event without arguments, with one and with two, a static
  method, a lambda, a lambda with a captured variable, null, a destroyed target; `AddVoid…`,
  `AddInt…`, `AddFloat…`, `AddBool…`, `AddString…` (also null) and `AddObject…` (typed and null)
  listeners; two listeners; a typed object listener with a null argument, null delegates in the
  void and object modes, and lambdas in the void and int modes.
* `register.*`: registering over an existing call in the same and another mode, and at an index
  out of range.
* `remove.*`, `unregister*`: removing by index (also out of range), by delegate (every match, with
  an argument, and none), and unregistering.
* `state.afterAdd`: a listener switched off after it was added.

A lambda's method name is the compiler's choice, so exception texts record only that it is one.

```sh
python3 tests/projects/unityeventtools/run.py record --label a
python3 tests/projects/unityeventtools/run.py record --label b
python3 tests/projects/unityeventtools/run.py publish a b
python3 tests/projects/unityeventtools/run.py gate
```
