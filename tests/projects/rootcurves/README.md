# Root motion from body curves on generic Animators

Unity 6000.0.71f1, built-in pipeline, Linux GPU Editor (windowed). Generic clips authored in the
Editor with root motion carry the body's curves on the Animator itself (`RootT.x/y/z`,
`RootQ.x/y/z/w`, empty path) instead of a root node's `MotionT`/`MotionQ`. This probe plays such
clips on Animators without an Avatar (and one with a generic Avatar without a root node) and
records, every frame (`Time.captureFramerate` 50, fixed step 0.02 s), the root's pose, a child's
animated position and `deltaPosition`, `deltaRotation`, `velocity`, `angularVelocity`, in
LateUpdate and from `OnAnimatorMove`.

The body moves 2 m forward per second with a sideways sway and a bob around 1 m, and turns from
40 to 130 degrees of yaw with a 5 degree pitch sway. `ProbeRunner.Create` authors the clips (30 fps,
looping unless named) and one controller per clip:

| Case | Content |
| --- | --- |
| `builtin.Game` | loop pose, root position Y based upon original (the settings of real generated clips), builtin root motion |
| `builtin.<setting>` | `Plain` (loop pose only), `KeepOrientation`, `KeepY`, `KeepXZ` (based upon original), `BakeRotation`, `BakeY`, `BakeXZ` (bake into pose), `NoLoopBlend`, `Once` (no loop) |
| `off`, `script`, `scriptOff` | root motion off; `OnAnimatorMove` moving the root by the deltas, with root motion on and off |
| `physics` | AnimatorUpdateMode.Fixed, a Rigidbody driven by `linearVelocity = deltaPosition / deltaTime` |
| `blend`, `transition`, `override` | a 1D blend tree (Walk 0, Game 1, at 0.5); Walk to Game at exit time 0.5 over 0.25 s; Walk replaced by Game through an AnimatorOverrideController |
| `motionCurves` | the same curves on `MotionT`/`MotionQ` |
| `partial` | only `RootT.x` and `RootT.z` |
| `withRootTransform` | Game's curves plus a curve on the root Transform's own position |
| `avatar`, `scaled` | a generic Avatar without a root node (AvatarBuilder); the root scaled by 2 |

```
python3 tests/projects/rootcurves/run.py create  --release 6000.0.71f1
python3 tests/projects/rootcurves/run.py editor  --release 6000.0.71f1 --label <label>
python3 tests/projects/rootcurves/run.py publish --release 6000.0.71f1 --label <label>
UNBOUND=<unbound> python3 tests/projects/rootcurves/run.py unbound --release 6000.0.71f1
```

The engine run is compared with `tests/projects/modelroot/compare.py` (every missing, extra and
differing value; numbers within 2e-4).

## Contract and observations

The [Root Motion manual](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html)
describes the body transform and root transform with Bake Into Pose and Based Upon for humanoid and
generic clips, the root transform of a generic clip coming from its Root node. It does not describe
body curves on an Animator without an Avatar. [Animator.hasRootMotion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-hasRootMotion.html)
"Returns true if the current rig has root motion"; [Animator.deltaPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-deltaPosition.html)
and [OnAnimatorMove](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAnimatorMove.html)
give the last evaluated frame's motion and the callback that takes it. Everything below is observed
in two byte-identical captures (2119 records), not a documented guarantee:

- Body curves move the root of an Animator without an Avatar (or with a generic one without a root
  node) as a humanoid body moves its root: the step is in the frame of the body's yaw (in the clip's
  own frame with Based Upon Original orientation), only the turn about the up axis is kept, loops
  carry the motion across cycles, Bake Into Pose keeps the start's part, the root's scale applies,
  and there is no human scale or feet pivot. Based Upon Original Y/XZ change no step.
- With root motion off and no `OnAnimatorMove` nothing moves; with `OnAnimatorMove` the deltas are
  reported whether root motion is on or off. `hasRootMotion` stays false.
- With body curves, the root Transform's own position curve does not move the root while root
  motion applies. `MotionT`/`MotionQ` curves keep their pitch in `deltaRotation`.
- Loop pose bends the child's generic curve so that it loops (its value stays 0).

Not covered: playable graph outputs playing body curves (Unbound warns and reports no motion),
clips with both body and `MotionT`/`MotionQ` curves, additive layers, masks and other releases.
