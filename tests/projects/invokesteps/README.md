# Invoke clocks and short repeats

Unity 6000.0.71f1, windowed GPU Editor. Two independent captures publish byte-identical values.
The fixed step is 0.03125 s and the frame step is 0.125 s. The probe records the exact fixed
step of a 0.09 s Invoke, 7 ms repeating calls at every delayed point, a 70 ms cadence control,
self-cancellation and a control with no fixed steps. Values compare exactly.

The first deadline uses the earlier of the fixed and frame clocks at scheduling. Each fixed-step
check reads that step's clock. Dynamic checks occur after Update and after LateUpdate. One late
repeat runs once per check when it is the only due call. Pending calls are visited in deadline
order; an interleaved repeat can be visited again if its next deadline follows the next pending
call. The original cadence remains, rather than restarting the interval from delivery time.
A one-shot is no longer IsInvoking inside its callback; a repeating call can cancel itself.

The initial fixed/frame offset varies when Play mode starts. A single half-fixed-step capture
frame separates clocks that start too close together, before the measured window. The canonical repeat windows begin
after the scheduling frame. The complete initial sequences remain in each capture's raw.txt.
The slower control counts all calls without asserting a phase at a clock boundary.

A second file, `edges.jsonl`, records cancellation and non-finite deadlines from the same run.
Cancelling a neighbor before delivery leaves the single-repeat cadence unchanged. Cancelling it
inside the repeating callback can expose the repeat already inserted behind it, allowing another
call at that point. Two NaN-delay repeats remain pending and deliver no callbacks. The single
probe gate compares both files without changing the original reference values.

[Invoke](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Invoke.html)
documents the delay and leaves other execution order dependent on timing.
[InvokeRepeating](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.InvokeRepeating.html)
documents scaled intervals. The clocks, traversal, callback state and delayed points above are
measured behavior, not a documented contract for other releases. Other releases retain the
previous scheduling clock and delayed points until measured.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/invokesteps/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/invokesteps/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/invokesteps/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/invokesteps/run.py gate
```
