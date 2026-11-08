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

## Rendering and operating-system focus controls

The `--render-loop` companion has 671 trace records from two identical windowed
Unity 6000.0.71f1 GPU Editor captures. It brackets every public loop leaf, appends
an extra custom tail, and later removes FinishFrameRendering. Automatic built-in
camera callbacks still follow the complete public/custom loop, then
WaitForEndOfFrame resumes. Explicit Camera.Render calls from Update and from an
end-of-frame coroutine run their callbacks before returning. A disabled camera
is rendered only by those explicit calls. Disabling/re-enabling a renderer gives
immediate invisibility and visibility during its next culling pass. Destroy from
Update and from EOF retains the delayed scripting delivery measured above.
Adding a camera component inside OnPreCull includes its OnPreCull in that same
pass. Built-in rendering sends no RenderPipelineManager camera/frame events.
Only the camera being rendered logs OnRenderObject, so the trace imposes no
order between different receivers of that message.

The [6000.0 Camera.Render API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera.Render.html)
documents explicit camera rendering and its callbacks; a camera cannot render
itself recursively. The [OnWillRenderObject API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnWillRenderObject.html)
applies per visible camera and excludes disabled scripts. The
[OnBecameVisible API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnBecameVisible.html)
includes Scene view cameras. The
[WaitForEndOfFrame API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForEndOfFrame.html)
places resumes after cameras/GUI, warns that changing to Scene view suspends them,
and excludes batch mode. The exact position outside the public loop is this
release's Play Mode observation, not a guarantee for other releases or pipelines.
The original destruction capture now checks renderer phases strictly.

The `--focus-loop` companion drives xdotool and xmessage on a private X11 display
for both a windowed GPU Editor and the real Unbound host window. After a ready
marker, each reports focus(false), focus(true), and no pause notification. The
four trace records are identical across independent Editor runs and the host.
Startup notifications are kept separately in each run's `.startup` sidecar;
`messages` continues to check startup notifications. The host is forced onto X11
for this control, because an inherited Wayland session would bypass its private
display. Wayland and mobile background pause behavior are not certified here.

Use the normal record/publish/unbound commands above with `--render-loop` or
`--focus-loop` and a fresh label. The engine-only gate runs all three captures.

The `--completion-loop` companion adds 44 strict records for
[Awaitable.EndOfFrameAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Awaitable.EndOfFrameAsync.html).
The 6000.0 API promises end-of-frame completion. In two identical windowed
6000.0.71f1 captures, completion follows rendering and EOF coroutines. With
OnDemandRendering interval two, both kinds of EOF wait span skipped screen
frames. The scheduler uses the same release-specific frame completion mechanism
as rendering and reuses its completion snapshot. The existing rendering/focus
and destruction references are unchanged.
