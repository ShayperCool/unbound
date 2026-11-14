# Linked pool state

Unity 6000.0.71f1, 161 exact records from two independent GPU Editor captures.
The captures used the NVIDIA driver only. This probe does not test thread safety.

The [LinkedPool API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.LinkedPool_1.html)
describes linked storage and a pool that is not thread safe.
[Get](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.LinkedPool_1.Get.html)
creates a value when storage is empty. A control consumes two retained values before
observing the newly created third value.
[Release](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.LinkedPool_1.Release.html)
rejects duplicate returns when collection checks are enabled. Controls include
separate equal-valued objects, null and a reentrant release callback.
[Clear](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.LinkedPool_1.Clear.html)
removes stored values and calls their destroy callbacks. Nonempty controls distinguish
the destroyed values and confirm the cleared pool creates a fresh one.

The selected capture additionally observes a private writable CountInactive counter,
reverse release order, callback timing and partial state when callbacks throw. Forced
counts include zero, negative values and both integer limits while storage contains
two objects. Get consumes storage and adjusts the existing counter; empty Get leaves
the counter unchanged. Release consults the counter for capacity, and Clear resets
both storage and counter. These are selected-release observations, including the
wrapping integer arithmetic. ObjectPool controls retain its separate reuse order and null-slot behavior, and
record equal-valued distinct objects and reentrant duplicate references. Both pools
share reference-counted membership without invoking user equality.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/linkedpool/run.py native --label first
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/linkedpool/run.py native --label second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/linkedpool/run.py publish first second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/linkedpool/run.py check
```

The gate uses only the committed reference and engine. Capture repeats and all engine
outputs stay outside Git. Managed tests add 257-object reuse and warmed-allocation
controls with collection checks enabled and disabled.
