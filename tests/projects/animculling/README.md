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

The first frame of Play mode is covered separately by `tests/projects/animstartup`.
Its automatic animation interval is zero in measured Unity 6000.0.71f1 entry contexts,
even for a controller already evaluated explicitly; a new Animator created in a later
Update advances immediately. The rigs here still start in frame 1 so these culling
oracles remain unchanged.

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
  The sampled position curves on the Animator's own root still write, including
  with root motion off.
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

Unmeasured playable contexts include Animator.Update with graph outputs, controller
playables, multiple outputs sharing nodes, humanoid output root motion, output
weights other than one, and other releases. Explicit Graph.Evaluate is measured
separately below. Culled consumers do not
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

An animation event can append stream bindings after a controller sampled its pose.
Managed regressions bind an animated child from that callback, with write defaults
on and off, and check the pose, event count and root movement. Pose buffers grow
without resampling, so previously removed root channels and retained layer values
stay removed or retained. Newly appended channels have no contribution in that
sample. This is an implementation invariant, not a new guarantee about callback
binding timing. The 6000.0
[BindStreamTransform API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorJobExtensions.BindStreamTransform.html)
describes creating a handle for a Transform already bound to the Animator.

## Explicit evaluation (Q90)

`explicit/entry/` creates clip outputs in Start, before Play's first automatic
animation interval. `explicit/later/` creates clip and two-clip mixer outputs in
frame 1. Both use Manual and GameTime graphs, all three culling modes, Animators
with and without their own controller, and pose-only, builtin movement and
OnAnimatorMove movement cases. Every call is recorded before and after, separately
from LateUpdate's following automatic evaluation. Calls in frames 0 (entry only),
6 (visible), 16 (invisible) and 33 (visible again) supply 0.07, 0 and 0.04 seconds.
`explicit/replay/` adds two visibility returns without a new evaluation between
them, for Manual clip outputs with and without a controller. `explicit/human/` covers requested IK on a humanoid clip without a controller or
root motion. Raw bone rotations and dot products are retained; the new human
cases do not introduce quaternion equality predicates.

Two independent windowed GPU Native captures agree byte for byte on 15,551 records.
Publishing adds 9,258 keys and preserves all 6,293 prior records byte for byte:

    python3 tests/projects/animculling/run.py editor --release 6000.0.71f1 --label explicit
    python3 tests/projects/animculling/run.py publish --append --release 6000.0.71f1 --label explicit

The `--append` mode validates both captures, refuses missing or changed old keys,
and appends only new keys before the existing completion row. The engine-only
`gate` command and strict comparator cover the entire canonical reference.

| Question | Unity 6000.0 contract | Observed in 6000.0.71f1 | Implementation consequence |
| --- | --- | --- | --- |
| Supplied delta | [PlayableGraph.Evaluate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html) advances connected playables by the supplied seconds; [Manual](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.DirectorUpdateMode.Manual.html) requires explicit calls with the caller's delta. | AlwaysAnimate advances by 0.07 on its first explicit entry call; repeated zero calls deliver movement with zero delta. GameTime explicit calls are additional to the automatic frame. | Retain supplied deltas, including on entry; do not apply automatic zero-entry timing to explicit calls. |
| Offscreen evaluation | [AnimatorCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.html) and the [Animator Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Animator.html) describe disabled updates or transform/IK writes. They do not separately define explicit graph evaluation. | CullCompletely holds source/clip time, pose, events and movement, including the supplied explicit delta. Its first offscreen explicit call evaluates at zero and delivers movement once. CullUpdateTransforms advances clocks/events/movement but holds child pose, script fields and humanoid IK, while root position curves still write. | Apply output culling to explicit calls as well as automatic ones. First culled evaluation uses zero time; subsequent culled evaluations do not consume time. |
| Own controller | [AnimationPlayableOutput](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationPlayableOutput.html) connects a graph to an Animator; the [Playables examples](https://docs.unity3d.com/6000.0/Documentation/Manual/Playables-Examples.html) show clip and mixer outputs. No separate controller phase is promised. | Explicit graph evaluation writes its pose and generic motion and invokes OnAnimatorMove even with a controller. The controller's state time remains unchanged by that call. Its next automatic evaluation writes its own pose and defaults for a Manual output. | Compute explicit output movement independently of the controller's previous automatic deltas. Preserve automatic combined output dispatch. |
| Visibility return | The culling API does not specify replay. | Manual outputs also write their last pose on return, run requested humanoid IK, and after CullCompletely replay the last movement delta. Before any explicit call, first visibility samples the zero-time pose for culling modes. | Keep the last output sample available across manual frames; sample untouched Manual outputs at first visibility without advancing their clocks or firing events. |
| Explicit Animator update | [Animator.Update](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.Update.html) evaluates with a delta and warns that physics and other game-loop systems may not work well with it. | This extension calls graph.Evaluate, not Animator.Update. Existing animstartup controls cover explicit Animator entry deltas. | Do not infer that graph.Evaluate advances an attached controller, or change Animator.Update's separate behavior. |

These are observations of this release, generic positional clips/mixers and the
specified humanoid IK control, not new promises about other releases or arbitrary
callback order. Controller-playable nodes, shared nodes, fractional output weights
and humanoid output root motion remain separate gaps. The initial mismatch is
explicit evaluation's unconditional advancement/write and its explicit attached
controller root-motion exclusion in AnimationOutputs/PlayableEngine.
