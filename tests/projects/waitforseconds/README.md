# Timed waits: WaitForSeconds, WaitForSecondsAsync, Invoke

Records, in Play mode of the Unity 6000.0.71f1 Editor (windowed on the private GPU display, so
`WaitForEndOfFrame` resumes), when timed waits come due: how many frames after the frame that
started them, and in which part of the frame (`fixedUpdate` when the last script callback before
the resume was a FixedUpdate, `update` after Update).

- `WaitForSeconds` yielded by a coroutine started in FixedUpdate (the frame's first fixed step),
  Update and LateUpdate, by one resumed at the Update coroutine point (`coroutine`) and by one
  resumed at the end of the frame (`endOfFrame`); two zero waits in a row;
- `Awaitable.WaitForSecondsAsync` awaited from Update;
- `MonoBehaviour.Invoke` from Update and from the first fixed step, delays 0 and 0.3 s.

Delays: -1, 0, 0.1, 0.3 and 0.9 s. Game time advances by `Time.captureDeltaTime` = 0.125 s (exact in
binary), so the frame counts do not depend on the machine. No delay ends on a frame boundary: there
the clocks' rounding decides, and the Editor resumed a 1 s wait after 8 frames in one capture and 9
in the next. One control runs on real time: a 0.2 s wait started in a frame that sleeps 250 ms
(`hitch`), to tell a start at the frame's time from one at the end of the frame.

Contract: the Scripting API
[WaitForSeconds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForSeconds.html)
says the coroutine continues "on the first frame after 't' seconds has passed" and that waiting starts
"at the end of the current frame";
[Awaitable.WaitForSecondsAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Awaitable.WaitForSecondsAsync.html)
and [MonoBehaviour.Invoke](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Invoke.html)
give no frame placement. Measured here: both timed waits count from the starting frame's time (also
from a fixed step) and never end in that frame, even at 0 or -1 s; the long frame does not move the
start (`hitch` false/false). Invoke counts from Time.time as read at the call (the fixed clock in a
fixed step) and comes due after a fixed step or after Update, whichever first reaches its time.

```sh
python3 tests/projects/waitforseconds/run.py record --label native-a
python3 tests/projects/waitforseconds/run.py record --label native-b
python3 tests/projects/waitforseconds/run.py publish native-a native-b
python3 tests/projects/waitforseconds/run.py gate
```

Values compare exactly (tolerance 0). Unmeasured: timeScale changes during a wait, InvokeRepeating
rates shorter than a frame, custom player loops without the delayed-call points.
