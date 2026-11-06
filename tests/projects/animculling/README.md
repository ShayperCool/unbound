# Animation culling probe

Animator.cullingMode (AlwaysAnimate, CullUpdateTransforms, CullCompletely) and
Animation.cullingType (AlwaysAnimate, BasedOnRenderers) against renderer visibility.
`Assets/Scripts/AnimCullingProbe.cs` makes one camera looking at the origin and, in frame 1's
Update, a holder per case with a rig under it: an Animator (controller `Culling`: state A, a 0.5 s
loop moving the empty child `Marker` along x, with an `OnClipEvent` event at 0.1 s, to state B by
exit time 1.2; or `RootMotion`: the root moving up 1 unit/s) or a legacy Animation (`LegacyLoop`),
plus the case's renderers. A schedule moves the holder between the origin and x = 1000 in Update:
A is on screen in frames 1-11 and 32-60, B from frame 20. Each LateUpdate (after the animation
update) records every renderer's `isVisible`, Marker's position, the root's position, the state and
its normalized time (or the legacy state's time), and the event, `OnAnimatorMove` and
`OnAnimatorIK` counts; the root-motion cases also record what `OnAnimatorMove` and the root showed
at the frame's Update (before that frame's animation, after the previous frame's rendering).
Frames are 0.02 s (`Time.captureDeltaTime`).

Cases: `a/` the state machine for each mode with schedules A and B; `rm/` builtin root motion;
`rmm/` root motion through `OnAnimatorMove` (A and B); `m/` two renderers, the second on screen
exactly while the holder is off and disabled from frame 20; `d/` and `i/` the only renderer disabled
or its GameObject inactive in frames 10-29; `n/` no renderer; `s/` a SkinnedMeshRenderer as the only
renderer, `updateWhenOffscreen` off and on; `h/` the humanoid model of `tests/projects/humanoid`
with an IK pass and a cube; `l*/` the legacy Animation counterparts; `late/` Animators and
Animations created in frame 5.

Measured in the Unity 6000.0.71f1 Editor (built-in pipeline, a windowed GPU Editor, its Scene views
closed so that the Game view's camera is the only one; a Scene view camera would count too):

* The culling uses `isVisible` at the animation update: what the previous frame's rendering left,
  after this frame's EarlyUpdate made renderers no camera drew invisible. A renderer disabled or
  deactivated earlier in the frame no longer counts. One visible renderer of several is enough; a
  hierarchy without renderers is always culled. A skinned renderer counts by its own visibility,
  whatever `updateWhenOffscreen`.
* CullUpdateTransforms: time, the state machine, transitions, events, root motion and
  `OnAnimatorMove` go on; the pose is not written (Marker keeps its last value, or its initial one)
  and `OnAnimatorIK` does not run.
* CullCompletely: nothing advances and nothing catches up afterwards. The Animator's first update
  still runs at zero time (`OnAnimatorMove` is called, no IK).
* When a renderer becomes visible (during the camera's culling), a culled Animator writes its pose
  and runs its IK pass once more; after a skipped update (CullCompletely, or that first update) its
  `OnAnimatorMove`/builtin root motion also runs again with the last evaluation's deltas, so the
  root moves by one extra delta.
* Legacy BasedOnRenderers: while culled the state's time, events and pose stay; the first update
  that runs again advances by all the culled updates' time and fires the skipped events. A state's
  first update (sampling its start) is spent even while culled.

The Unity 6000.0 contract is documented in
[AnimatorCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.html),
[Animator.cullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-cullingMode.html)
and the [Animator component Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Animator.html).
CullUpdateTransforms disables retargeting, IK and transform writes; CullCompletely disables
animation while renderers are invisible. The timing, callbacks and visibility replay above are
observations of this release and probe, not guarantees about other contexts.
[AnimationCullingType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationCullingType.html)
and [Animation.cullingType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animation-cullingType.html)
describe legacy culling: states, events and sampling stop while culled. Its catch-up is measured here.
[Renderer.isVisible](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-isVisible.html)
also counts shadow rendering and Editor Scene view cameras. This capture excludes Scene views.

Not part of the probe: Animators created in the first frame of Play mode are evaluated without
advancing in their first update (measured while designing the probe; it is outside culling, so the
rigs are built in frame 1).

Record (two independent windowed GPU Editor captures; publish only byte-identical ones):

    python3 tests/projects/animculling/run.py create --label create   # assets, scene (batchmode)
    python3 tests/projects/animculling/run.py editor --label native
    python3 tests/projects/animculling/run.py publish --label native

Strictly check the engine: `python3 tests/projects/animculling/run.py check`.
The landing gate, `gate.json`, runs `run.py gate`, which prints the same strict comparison
and then validates only the four documented rotation-threshold residuals below.
`compare.py` compares every record (numbers within 1e-4) and lists missing, extra and differing ones.
Run output stays under `$XDG_CACHE_HOME/unbound/validation/runs/animculling/`.

Unmeasured: other releases, particle/line/trail renderers, controller-only non-transform
properties under CullUpdateTransforms, the Physics update mode, and the Scene view camera.

## Playable outputs

The `p/` cases add a GameTime graph to an Animator created in frame 1. They cover
clip and mixer outputs, each culling mode, with and without the `Culling` controller,
with root motion off, builtin root motion, and `OnAnimatorMove` applying builtin
root motion. `PlayablePose` and `PlayableSecond` have root and child position curves,
`CullReceiver.value` curves, and events. `gt` and `ct` record source and clip times.
The `ph/` cases use the humanoid clip, with and without a controller, clip and mixer
outputs with requested playable IK. `pose/` records keep raw rotations and successive
rotation dot products beside the existing pose-change predicates.

Two independent Unity 6000.0.71f1 GPU Editor captures, Game view only, match byte for
byte across all 6293 records. Every original A11 value is unchanged. Observations:

* CullCompletely stops the animation source and clip clocks as well as the attached
  controller. The initial zero-time evaluation still delivers movement. Returning
  visibility writes the last combined pose, replays movement with the last deltas,
  and runs requested humanoid IK. Playback resumes without catch-up.
* CullUpdateTransforms advances graph and controller clocks, events, root motion
  and movement callbacks. Child transforms, humanoid pose and script fields hold.
  Curves on the Animator's own root still write, including with root motion off.
  Visibility return writes the current combined pose and requests IK without another
  clock step, event dispatch or movement callback.
* The generic rigs do not receive IK callbacks. A humanoid clip or mixer requesting
  playable IK runs one pass, including with an IK-enabled attached controller.

The official 6000.0 [AnimationPlayableOutput API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationPlayableOutput.html)
connects a graph to an Animator. [AnimationPlayableUtilities](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.AnimationPlayableUtilities.html)
provides clip and mixer graph helpers; it does not define separate culling semantics.
The [Playables manual](https://docs.unity3d.com/6000.0/Documentation/Manual/Playables.html)
allows animation without a controller. [SetApplyPlayableIK](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationClipPlayable.SetApplyPlayableIK.html)
requests OnAnimatorIK. These pages and the culling contract above do not specify
clock suspension, the root/script write distinction or visibility replay. Those are
observations of this release and these cases. No callback ordering between different
Animators is asserted.

Unmeasured playable contexts include explicit Graph.Evaluate/Animator.Update,
controller playables, multiple outputs sharing nodes, humanoid output root motion,
output weights other than one, and other releases. The engine keeps explicit
Graph.Evaluate unculled as before, an implementation choice. Culled consumers do not
stop a shared node another unculled output needs, an implementation invariant rather
than a measured Unity guarantee.

The four `ph/always/{bare,controller}/{clip,mixer}/35.arm` pose-change predicates are
insignificant numeric residuals. The preceding and current arm rotations differ from
Native by at most 3.27e-7 per quaternion component, while the frame-35 dot is
0.999999046 versus 0.999999, straddling Quaternion equality's float boundary.
All continuous pose records pass the unchanged strict tolerance. The gate verifies
these exact keys and boolean directions, both raw rotations within 5e-7, and the
measured near-boundary dots. Every other value stays strict. `compare.py` still
reports all four failures and exits nonzero; no oracle, assertion or comparator
tolerance was changed to remove them.

The combined movement dispatch preserves the attached controller's state movement
callbacks through its existing dispatcher. The 6000.0
[OnStateMove API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/StateMachineBehaviour.OnStateMove.html)
places these after OnAnimatorMove. Managed regression tests check that deferring the
controller does not drop them, including under CullUpdateTransforms without requested
root motion. The probe does not claim an ordering between different Animators.

The deferred controller base uses the same layer composition as its scene write,
including defaults, held values and root-motion exclusions. A managed regression
checks that adding an output on another child cannot make a half-weight controller
layer accumulate the previous frame's scene value, and that a no-defaults state
continues holding its layer value after a game write.
