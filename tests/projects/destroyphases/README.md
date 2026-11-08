# Destroy phases

Unity 6000.0.71f1 Play Mode, built-in pipeline, Linux GPU Editor. The fixture runs in
private project copies assembled by `run.py`. It reuses the empty scene/settings
from `lifecycle` and the avatar/controller from `humanoid`; it authors only its own
legacy animation clip. Run artifacts stay in the validation cache.

The capture logs request/return state, OnDisable/OnDestroy, later callbacks,
particle ticks, audio playback, null observations and player-loop leaf brackets.
The hierarchy controls include multiple components, siblings, grandchildren and
one deeper generation. Script execution order selects this fixture's driver;
the raw component/sibling order is an observation, not a promised API order.
`--default-loop` leaves the public player loop unchanged as a control for markers.

Record two independent windowed captures, then publish their identical record:

```
python3 tests/projects/destroyphases/run.py editor --release 6000.0.71f1 --label capture --repeat native1
python3 tests/projects/destroyphases/run.py editor --release 6000.0.71f1 --label capture --repeat native2
python3 tests/projects/destroyphases/run.py publish --release 6000.0.71f1 --label capture
```

The engine-only gate reads the committed reference and prints every raw delta:

```
UNBOUND=/path/to/unbound python3 tests/projects/destroyphases/run.py unbound --release 6000.0.71f1
```

## Contract and limits

The [6000.0 Destroy API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Destroy.html)
places destruction after Update and before rendering; positive delays use scaled
time. Components are removed individually, while GameObjects include their
components and descendants. The measured leaf positions refine this description
for this Editor release, not for arbitrary releases or custom loops.

The [execution-order manual](https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html)
limits ordering guarantees across objects, including parents/children and instances
of one script. Rendering descriptions apply to the built-in pipeline.
[DestroyImmediate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.DestroyImmediate.html)
is intended for Edit mode and refuses immediate destruction from physics,
animation-event, rendering and OnValidate callbacks. This fixture measures runtime
GameObject/component refusals and a runtime asset control.

[UnloadSceneAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html)
has no guaranteed completion time and does not unload assets. Earlier captures
completed the simple unload at relative frame 2 or 4. All raw captures remain in
the validation cache; a canonical frame is an observation, not a completion-time
contract. Startup pause/focus controls cover initial false/true notifications;
OS background pause and focus changes are outside those controls.

## Observations used by the implementation

- Zero-delay requests disable Behaviours on the target and its direct children
  before returning. Deeper descendants stay enabled until delivery. Audio playback
  stops; particle simulation and script callbacks are distinct questions.
- Objects remain non-null during OnDisable and OnDestroy. The null observation
  changes after teardown. Component destruction keeps the GameObject alive.
- Delivery consumes the requests present at entry to each delayed Fixed, Update,
  or PostLateUpdate scripting point. Requests from Starts, coroutine resumes or
  OnDestroy inside that point wait for the next point.
- Positive delays leave Behaviours enabled until the scaled deadline is reached.
  A zero delay still completes with timeScale zero.
- Immediate destruction and scene unloading deactivate children before parents,
  then visit parents before children for OnDestroy in this fixture. This is not a
  guarantee about sibling/component order.
- Runtime assets can be destroyed; persistent assets and prefab assets refuse
  Destroy immediately and stay alive.

The original `particletriggerapi` capture was independently repeated during this
work and still matched all 5299 records. Its `*/gone` windows have no particle
callback. Smaller coroutine controls here still receive OnParticleTrigger after
the request, including disabled scripts. Their public loop, default-loop, batch,
script-order, nested-coroutine and early-read controls do not explain that
context difference. The previous particle guard remains while this difference is
investigated; its suppression of simulation in those smaller windows is a
functional residual, not an insignificant numerical difference.

The comparator prints the entire ordered raw delta. Its comparison view names
only these residuals, with bounds checked by `test_compare.py`:

- Renderer callbacks currently originate at FinishFrameRendering; the Editor
  calls them after its public loop. IK currently originates at animation-begin
  instead of animation-end. Frame, state and callback count remain checked.
- In the delayed-fraction control, time resumes after a paused frame. The particle
  renderer becomes visible on its first simulation step in the Editor and one
  frame earlier in Unbound. Only the two captured first-frame visibility-count
  patterns are admitted; later callbacks remain checked.
- The retained particle guard suppresses one pending simulation step and its
  trigger callback in the smaller coroutine windows. The comparison accepts only
  that single tick and only callbacks inside that pending window.
- Destroyed ParticleSystem.Play now fails immediately. The existing exception
  diagnostic omits the type's namespace; that exact diagnostic difference remains.
- Async unload callbacks, null state and completion must agree in order, state and
  preloading phase. Elapsed frames can vary. Only previously observed repeatable
  callbacks can occur while waiting; callbacks after completion fail.

These are reported differences, including functional ones. They are not added to
the table of insignificant numerical residuals.
