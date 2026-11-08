# Animation callback placement

Where the Animator's script callbacks run in the player loop, Unity 6000.0.71f1 Play Mode,
built-in pipeline, Linux GPU Editor (windowed, Scene views closed). `Assets/Scripts/AnimCallbacks.cs`
brackets every leaf system of the player loop with markers that record its name, then runs one case
per eight frames (`Time.captureFramerate` 50, fixed step 0.02 s):

| Case | Content |
| --- | --- |
| `genericController` | a generic Animator (child `Marker`) with `GenericOrder` |
| `humanController` | the `HumanA` model with `HumanOrder` |
| `twoHumans` | two such humanoids |
| `humanLegacy` | a humanoid, a generic Animator and a legacy Animation with events |
| `genericGraph`, `humanGraph` | a GameTime AnimationClipPlayable output (playable IK requested), no controller |
| `humanManual` | a Manual graph evaluated with `Evaluate(0.02)` in Update |
| `humanUpdateCall` | `Animator.Update(0.02)` in Update besides the automatic update |
| `humanFixed` | `AnimatorUpdateMode.Fixed` |
| `humanCulled` | CullUpdateTransforms, off screen |
| `humanControllerGraph` | `HumanOrder` and a GameTime clip output on the same Animator |
| `rootPlain`, `rootMove`, `rootApply` | a generic Animator playing `GenericRoot` (curves on its own position and on `Marker`): no movement script and root motion off; `OnAnimatorMove` and root motion off; builtin root motion |
| `rootGraph` | `GenericRoot` from a GameTime clip output, no movement script, root motion off |
| `humanPlain` | the humanoid with `HumanPlain` (no IK pass, no behaviours) and no movement or IK script |

The first eleven cases' subjects have `OnAnimatorMove` and `OnAnimatorIK`; the last five have
neither. `GenericOrder` and `HumanOrder` (`ProbeRunner.Create`) have a base layer A → B (exit time 0.12, a 0.04 s fixed
transition) and a second layer at weight 0.5 playing C; both layers have an IK pass, and every state
has an `OrderState` behaviour logging OnStateEnter/Update/Exit/Move/IK. The clips carry events at
0.03 s and 0.25 s. Every callback logs its leaf and, for every subject, whether its pose (any
transform below its root, compared exactly) and its root position changed since the previous
LateUpdate; the same state is also sampled right after each animation leaf.

```
python3 tests/projects/animcallbacks/run.py create  --release 6000.0.71f1   # authors clips and controllers
python3 tests/projects/animcallbacks/run.py editor  --release 6000.0.71f1 --label <label>
python3 tests/projects/animcallbacks/run.py publish --release 6000.0.71f1 --label <label>
UNBOUND=<unbound> python3 tests/projects/animcallbacks/run.py unbound --release 6000.0.71f1
```

The engine run compares every record strictly, in order.

## Contract and limits

The [6000.0 execution order manual](https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html)
places the animation update (state machines, OnAnimatorMove, OnAnimatorIK, writes) before LateUpdate
and gives no order between them beyond the list.
[OnAnimatorMove](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAnimatorMove.html)
is invoked "after the state machines and the animations have been evaluated, but before OnAnimatorIK";
[OnAnimatorIK](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAnimatorIK.html)
is "called by the Animator Component immediately before it updates its internal IK system", once per
layer with an IK pass. Neither page names player-loop leaves, orders callbacks across Animators or
says when transforms are written. Everything below is observed in 6000.0.71f1 for these cases, not a
documented guarantee.

## Observed (two byte-identical captures, 1928 records)

- PreLateUpdate.DirectorUpdateAnimationBegin runs every Animator's state machines first, Animator by
  Animator: clip events (of the controller and of its playable outputs), then OnStateExit/Update/Enter.
  Then every Animator's movement: OnAnimatorMove, then OnStateMove per layer. The root has moved; no
  pose is written yet. PreLateUpdate.LegacyAnimationUpdate (legacy events) still sees the old poses.
- PreLateUpdate.DirectorUpdateAnimationEnd: the humanoids' poses are written, then the IK passes run
  layer by layer over all Animators (OnAnimatorIK(layer), then OnStateIK of that layer's states).
  A generic Animator's pose is written in the same leaf, after those IK passes.
- The Animator's own Transform is written in the Begin leaf, with the movement: its position curves
  (with root motion off and no movement script, also from a graph output) and its root motion.
  Only the transforms below it wait for the End leaf. Whether the Animator has a movement or IK
  script, an IK pass or state behaviours does not change this.
- AnimatorUpdateMode.Fixed: the same split over FixedUpdate.DirectorFixedUpdate and
  FixedUpdate.DirectorFixedUpdatePostPhysics. CullUpdateTransforms: the Begin part only.
- `PlayableGraph.Evaluate` and `Animator.Update` run everything at once in the calling script, in the
  same order (events, state callbacks, movement, write, IK).
- Within a layer, a transition's start calls Update(source) then Enter(destination), its end
  Exit(source) then Update(destination); the source still gets OnStateMove in the update that
  completes the transition.

Not covered: several Animators with playable outputs and controllers at once, Animators enabled or
destroyed between the two leaves, the generic write order relative to humanoids without an IK pass,
interrupted transitions, sub-state machines, the root's rotation and scale curves and the Animator's
non-Transform properties (when they are written), and other releases.
