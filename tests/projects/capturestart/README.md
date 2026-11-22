# Capture rate and deltas at Play mode's start

Unity 6000.0.71f1, Linux Editor in batch mode on the GPU display, built-in render pipeline. One
run per point at which the probe sets `Time.captureDeltaTime` to 1/30 s (`CAPTURE_START_AT`); each
run records the clocks where the rate is set and in the first six Updates (time, deltaTime,
fixedTime, the FixedUpdates since the previous Update). Two independent sets of runs agree byte
for byte; the canonical reference holds one file per point, 11 points.

Points: `subsystem` and `before` (`RuntimeInitializeOnLoadMethod`, SubsystemRegistration and
BeforeSceneLoad), `bootAwake` (Awake of an object made in BeforeSceneLoad), `sceneAwake` and
`sceneEnable` (an object of the scene), `after` (AfterSceneLoad), `sceneStart` and `bootStart`
(Start), `update0` (the first Update), and two that set no rate: `fixedBefore` (a script sets
`Time.fixedDeltaTime` 0.05 and `Time.timeScale` 0.5 in BeforeSceneLoad) and `projectFixed` (the
run's copy of the project has that timestep and scale in its Time settings).

Measured:

- A rate set at any point before the first frame's Start (the load callbacks, Awake, OnEnable)
  steps the game clock from the first frame on: its Update reads time and deltaTime 1/30. A rate
  set in Start or in the first Update leaves that frame at time 0 and steps from the second.
- Before the first frame and in it, `Time.deltaTime` is 0.0199999921 (and `unscaledDeltaTime`
  before the first Update; `smoothDeltaTime` 0), whatever the fixed timestep and the time scale
  are, set by script or in the project's Time settings. The first frame runs one FixedUpdate at
  fixed time 0.
- Under the rate, a frame runs the FixedUpdates its 1/30 s holds (2, 2, 2, 1, ...).

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [Time.captureDeltaTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-captureDeltaTime.html): with a non-zero value the game time advances by it each frame, whatever the real time. It does not say from which frame after the property is set. | `frame.*` of the points set before and after the first frame's Start differ by one step. |
| [Time.deltaTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-deltaTime.html): the interval from the last frame to the current one. It does not say what it reads before or in the first frame. | `at.*` and `frame.0` of `fixedBefore` and `projectFixed`, whose timestep and scale would give 0.05 or 0.025. |

Not recorded: `unscaledDeltaTime` from the first Update on and every frame of the points that
set no rate after the first (they follow real time); a rate set after the second frame (the
frame's own time then precedes it); windowed Play mode (the Game view's first frame, see
`tests/projects/eofstartup`). Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/capturestart/run.py native --label reference-a
python3 tests/projects/capturestart/run.py native --label reference-b
python3 tests/projects/capturestart/run.py publish reference-a reference-b
python3 tests/projects/capturestart/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
