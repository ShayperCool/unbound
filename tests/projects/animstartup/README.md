# Animator startup timing

Unity 6000.0.71f1 Linux Editor, built-in pipeline, windowed GPU Play mode, Game view
only. A one-second clip moves `Marker.x` from 2 to 4 and the Animator root's y from
0 to 1. Events at 0, 0.01 and 0.04 seconds distinguish sampling from advancement.
Snapshots retain normalized time, full state hash, pose, root and delta position,
event counts, and OnAnimatorMove counts. The receiver applies builtin root motion;
`builtin` controls omit the receiver. Every case is recorded before animation in
Update and after animation in LateUpdate for nine Updates. The entry profile has
1,831 records; a delayed profile with no Animator until Update 3 has 168. Two
independent Native captures of each profile agree byte for byte.

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
| First registration after entry | The delayed profile creates its first Animator and first graph in Update 3; both advance immediately, including after explicit zero or positive evaluation. Entry is tied to Play initialization, not the first registered Animator or graph. |
| Fixed and UnscaledTime | Fixed updates retain their fixed deltas, including two fixed steps before the entry Update under this capture clock. UnscaledTime also samples without advancing at entry. This is not a global zero delta for every subsystem. |
| Initial automatic Play(offset) | A newly created controller first samples the requested normalized offset, without adding that automatic update's delta to state time. Its root movement covers the interval ending at the offset. The controls measure this on generic and humanoid controllers. Established-player seeks and explicit positive Update after Play are not measured by this probe and retain the prior implementation. |
| Disable/re-enable | Component enabled=false suspends the existing controller, regardless of keepAnimatorStateOnDisable. Hierarchy deactivation clears the state unless kept. A cleared inactive controller reports no current state, then reinitializes on activation. Neither re-enabling context adds an automatic startup delay. |
| Playable start events | The first automatic GameTime clip sample includes its time-zero event, once, both with a zero entry interval and with a positive later interval. Explicit Graph.Evaluate remains outside this measurement. |
| BeforeSceneLoad integration | Unity keeps rigs constructed by this callback. Unbound previously created them in an outgoing bootstrap scene and destroyed them on the first Single load. The callback now runs after scene objects are loaded, before their Awake, as the runtime initialization API documents. |


Unmeasured: other releases, standalone player startup, custom PlayerLoop replacements,
scene loads after entry, and creation inside animation callbacks. Explicit playable
Graph.Evaluate, controller playables, shared graph nodes, fractional output weights
and humanoid output root motion remain separate compatibility work.

## Humanoid controls and retained follow-up

The `human` profile uses the same HumanA model and IK-enabled controller as
animculling. Controller creation in Start, first Update, second Update and later
Update has plain and Play-offset controls. A controller-free GameTime clip output
requests IK and samples pose with root motion off and no OnAnimatorMove handler.
It records 193 values including graph/state time, hips and arm quaternions, root
position/delta and movement/IK counts. Its reference also requires two independent
byte-identical Native captures.

`humanMotion` is a separate, retained root-motion control with OnAnimatorMove
applying builtin movement to the humanoid output. It has 193 Native records from
two agreeing captures. The existing unsupported humanoid output root-motion path
leaves 60 raw records different in that profile. Run it explicitly with
`run.py check --profiles humanMotion --release 6000.0.71f1`; every differing root,
delta and callback count stays visible. Its poses, IK and clocks match. This is the
separate A12 follow-up named in Q76, not part of the startup gate. No comparator
ignores fields or tolerances. The default gate strictly checks `entry`, `delayed`,
`human` and `initialization`.

## Initialization review controls

The separate `initialization` profile records scene-script lookup before and after
scene initialization, plus the combination of component disabling, GameObject
deactivation/reactivation and later component enabling with state retention off
and on. The scene lookup records `[loaded, active, activeSelf, activeInHierarchy]`.
In the two Native captures it is `[1,0,true,false]` in BeforeSceneLoad and
`[1,1,true,true]` in AfterSceneLoad. Runtime-created objects still activate normally
inside BeforeSceneLoad. Scene-authored roots remain pending activation until the
callback returns; their authored activeSelf flags do not change.

A disabled controller clears on hierarchy deactivation when retention is off,
even though it receives no second component-disable notification. Reactivation
makes its initial state queryable while the component is still disabled; enabling
later advances that fresh state. With retention on, its previous time survives.
These are 31 additional Native records. The earlier 2,192 startup records, the
193 humanoid-motion follow-up records, all culling references and every comparator
tolerance remain unchanged.

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
controller, scenes, model, meta files and settings are committed, with
`tests/authored_project.py` recording and assembling the normalized inputs; the gate does not launch
the Editor. No prior oracle or comparator tolerance is changed by this probe.
