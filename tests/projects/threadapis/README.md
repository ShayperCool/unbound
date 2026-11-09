# Main-thread API and execution contexts

Unity 6000.0.71f1. The reference contains 1,222 outcomes from 134 representative API calls,
thread markers, animation-stream controls and constructor checks. Two independent GPU Editor
captures are byte-identical.

The API matrix runs in Start, a Thread, Task.Run, an async continuation with ConfigureAwait(false),
a scheduled IJob, IJob.Run, IAnimationJob.ProcessAnimation, explicit Physics.Simulate contact
modification and automatic-simulation contact modification. Each matrix has its own objects
created beforehand. It covers Rigidbody, Collider, Physics queries, Renderer, Material, Animator,
AudioSource, Canvas, RectTransform and MonoBehaviour.Invoke/CancelInvoke/IsInvoking.

Measured distinctions:

- Rejected calls preserve each binding's complete UnityException message. Physics settings,
  Animator.StringToHash, Shader.PropertyToID and Canvas.ForceUpdateCanvases remain allowed.
- Zero-direction Physics.Raycast overloads return false before reaching the checked query.
  Collider.Raycast still checks first, as does a nonzero RaycastNonAlloc with a null buffer.
- Constructors and field initializers reject Time.time in scene-loaded MonoBehaviours and
  ScriptableObject assets, AddComponent and CreateInstance. Their messages identify the class
  and, for AddComponent, the GameObject. Creation callbacks run after this restriction ends.
- Animation callbacks report JobsUtility.IsExecutingJob and reject main-thread-only calls;
  stream and scene transform handles remain usable. The implementation preserves these contexts
  even when evaluating animation on the main thread.
- Both measured contact-modification contexts allow these calls and report no job execution.
  This does not guarantee callback thread assignment for other scenes. The worker-thread matrix
  separately records the rejection behavior when calls run off the main thread.

The exact outcomes are measured, not a universal thread-safety list. Sibling setters share their
bindings' check by inference unless separately captured. Remaining engine API bindings need
further coverage; the compatibility gap lists them. Constructor and animation contexts use the
measured release's Compat profile; other releases keep their previous, unmeasured contexts.

Contracts and limitations: [Unity .NET overview](https://docs.unity3d.com/6000.0/Documentation/Manual/overview-of-dot-net-in-unity.html),
[IAnimationJob](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.IAnimationJob.html),
[ContactModifyEvent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ContactModifyEvent.html)
and [ContactEvent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ContactEvent.html).
ContactModifyEvent permits callbacks from any thread and limits the available API accordingly.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/threadapis/run.py editor --repeat native1
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/threadapis/run.py editor --repeat native2
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/threadapis/run.py publish native1 native2
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/threadapis/run.py publish-raycast native1 native2
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/threadapis/gate.py
```

A separate nine-outcome `raycast.jsonl` control records the exact single-hit layer-mask
overload binding in every context.

The gate makes a fresh project copy, runs only Unbound, and compares every outcome exactly.
The replay compares all 1,222 outcomes without return-value allowances. The separate
`followups.jsonl` matrix adds 598 outcomes and checks additional Renderer, Animator and AudioSource properties
on the main thread, Thread, Task.Run, IJob.Run and a scheduled IJob. It compares the full
exception type and message, including binding aliases, and keeps successful thread-safe
outcomes. Each setter runs even when its getter is forbidden.

Lifecycle controls distinguish a renderer's null/empty slots and owned material copies,
a controllerless Animator's active, disabled, inactive and reactivated states, and an
AudioSource's pending play-on-awake state from actual, delayed, paused and stopped playback.
A second renderer still shares the original white material after the first renderer's
instance becomes red; both fresh clipless and already playing sources exercise Pause.

Contracts checked against Unity 6000.0 documentation:
- [Renderer.material](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-material.html):
  instantiation isolates material mutations; the two-renderer color control changes only one.
- [Animator.isInitialized](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-isInitialized.html):
  reports initialization; inactive Rebind and activation are recorded separately. Controllerless
  keep-state controls measure the release's initialization flag, independently of controller state.
- [AudioSource.isPlaying](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-isPlaying.html):
  actual and delayed playback report playing, Pause reports false for a playing voice. The
  fresh clipless pending state is recorded separately; Pause before its audio update leaves it true.

Record and publish the supplementary reference from two identical GPU Editor captures:
```sh
python3 tests/projects/threadapis/run.py editor --repeat followups1 --followups
python3 tests/projects/threadapis/run.py editor --repeat followups2 --followups
python3 tests/projects/threadapis/run.py publish-followups followups1 followups2
```

Managed MainThreadApiContextTests additionally check scope restoration after constructor failure,
creation callbacks, animation callbacks and handles, and zero allocations in successful checked
property reads. The task's microbenchmark measures 10 million property reads and central checks;
its run output stays in the task cache.
