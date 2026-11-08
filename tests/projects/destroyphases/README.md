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
