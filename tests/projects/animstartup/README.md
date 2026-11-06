# Animator startup timing

Unity 6000.0.71f1 Linux Editor, built-in pipeline, windowed GPU Play mode, Game view
only. A one-second clip moves `Marker.x` from 2 to 4 and the Animator root's y from
0 to 1. Events at 0, 0.01 and 0.04 seconds distinguish sampling from advancement.
Snapshots retain normalized time, full state hash, pose, root and delta position,
event counts, and OnAnimatorMove counts. The receiver applies builtin root motion;
`builtin` controls omit the receiver. Every case is recorded before animation in
Update and after animation in LateUpdate for nine Updates.

Scene-authored enabled and disabled Animators accompany runtime creation in
BeforeSceneLoad, Awake, OnEnable, AfterSceneLoad, Start, first Update, second Update
and later Update. Each creation group includes plain, explicit Update(0), explicit
Update(0.03), Play at normalized time 0.25, Rebind, UnscaledTime, Fixed, builtin
movement, and clip playable outputs with and without an attached controller.
Disabled component enabling and hierarchy activation controls run in Awake,
OnEnable, Start and the three Update contexts. Later disable/re-enable controls
separate component enabled from GameObject activation and keepAnimatorStateOnDisable.

`unscaled` cases set speed to `0.02 / Time.unscaledDeltaTime` before each automatic
update. They measure whether the unscaled automatic update advances at entry while
keeping animation time deterministic across independently paced Editors. No order
between different Animators or different script instances is asserted.

## Contracts and cause table

These are the official **6000.0** docs, read for this change, including limitations.
The capture measures patch **6000.0.71f1**; neither the docs nor a repeatable capture
establish startup behavior in other releases or player builds.

| Source | Applicable guarantee or limitation |
| --- | --- |
| [Execution order, internal animation update and limitations](https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html) | The default loop places animation between Update and LateUpdate. Animation evaluation includes events, state callbacks, OnAnimatorMove and IK. Ordering of the same event on different objects is generally unspecified; configured script execution order remains supported. The high-level diagram omits internal steps, and PlayerLoop can replace the default loop. |
| [RuntimeInitializeOnLoadMethod](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RuntimeInitializeOnLoadMethodAttribute.html) | BeforeSceneLoad precedes scene Awake/OnEnable; AfterSceneLoad follows them. The same invocations occur on Editor Play entry. Order within a load type is unspecified. |
| [Awake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Awake.html), [OnEnable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnEnable.html), [Start](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Start.html) | Awake runs once, OnEnable requires an enabled component on an active object, Start precedes its first Update and follows scene Awake. Runtime instantiation does not provide the scene-wide initialization ordering. |
| [Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html) | Clones retain active state; Awake/OnEnable run only for objects active in the hierarchy at instantiation. |
| [Animator.Update](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.Update.html) | Evaluates with the supplied delta; manual evaluation may not work well with physics or other game-loop systems. No initial automatic-delta guarantee. |
| [Rebind](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.Rebind.html) | Rebinds animated properties and mesh data after hierarchy changes. No automatic time-step guarantee. |
| [Play](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.Play.html) | Selects a state and optional normalized-time offset. No guarantee that it immediately writes the pose. |
| [updateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-updateMode.html), [AnimatorUpdateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorUpdateMode.html) | Normal uses the frame loop, Fixed the fixed loop, UnscaledTime is independent of timeScale. |
| [keepAnimatorStateOnDisable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-keepAnimatorStateOnDisable.html) | Controls controller state retention when the GameObject becomes inactive, default false; does not affect AnimatorControllerPlayable. |

| Question | Measured observation / inference |
| --- | --- |
| Does every new Animator need a zero-time first tick? | No. Scene/entry-context automatic evaluation uses zero animation time. Creation in the second or later Update advances immediately. |
| Does any evaluation consume a per-player initialization delay? | No. Explicit positive Update advances immediately, including before the first loop. The following entry-frame automatic evaluation adds zero time even for this already evaluated player. Update(0) samples the pose, fires the start event once and delivers movement. |
| Is the entry automatic evaluation skipped? | No. It samples the initial pose, fires start events and calls OnAnimatorMove with zero displacement. Play offsets and explicit positive evaluation are retained. |
| Cause | The automatic animation time source has an entry evaluation interval of zero, independently of the controller player's first evaluation. This is an implementation inference from the discriminating controls, not a claim about Unity internals. |

Unmeasured: other releases, standalone player startup, custom PlayerLoop replacements,
scene loads after entry, and creation inside animation callbacks. Explicit playable
Graph.Evaluate, controller playables, shared graph nodes, fractional output weights
and humanoid output root motion remain separate compatibility work.

## Reproduce

All runs use fresh private projects and keep output under
`$XDG_CACHE_HOME/unbound/validation/runs/animstartup/`. Native publication requires
two independent successful GPU Editor captures with byte-identical records. The
comparator retains all missing, extra and failing values, with numeric tolerance
`1e-4`, the same as animculling.

```sh
python3 tests/projects/animstartup/run.py create --release 6000.0.71f1 --label create
python3 tests/projects/animstartup/run.py editor --release 6000.0.71f1 --label native
python3 tests/projects/animstartup/run.py publish --release 6000.0.71f1 --label native
python3 tests/projects/animstartup/run.py check --release 6000.0.71f1
```

`gate.json` declares the engine-only check with an explicit release. Authored clips,
controller, scene, meta files and settings are committed; the gate does not launch
the Editor. No prior oracle or comparator tolerance is changed by this probe.
