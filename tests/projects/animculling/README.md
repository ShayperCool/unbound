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

Two independent windowed GPU Native captures agree byte for byte on 15,615 records.
Publishing adds 9,322 keys and preserves all 6,293 prior records byte for byte:

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

The `previous/` records on four Manual clip/mixer controls retain the public
source and clip previous-time values before/after visible, skipped and returning
calls. They stay at the last evaluated interval while CullCompletely skips calls.
On visibility return after skipped updates, mixer-input events repeat that last
interval, while a direct clip output does not. The public times are the same in
both shapes, so a clock-advancement explanation is disproved; Unity's internal
reason for the event-cache difference remains unresolved. The implementation
retains event intervals for intermediate clips for this replay, without another
clock advance or a controller-state update. CullUpdateTransforms does not replay
events on return.

An attached controller's next write-defaults sample restores output-bound fields
and the Animator-root pose, including the root during CullUpdateTransforms. The
Manual output's pose replay restores children and fields while leaving that
controller root pose alone. Generic root movement still applies the last stored
delta; with an attached controller, its reported replay velocity uses the
controller sample interval (0.02 here), instead of the explicit interval (0.04
here). These distinctions are measured, not a proposed controller phase rule.
Other controller intervals and callback-relative visibility changes are unmeasured.

## Direct humanoid clip root motion (Q91)

`humanMotion/` connects one nonmirrored `LoopMuscles` AnimationClipPlayable directly
at output weight one to an Animator with a valid Avatar and no controller. The
assets are the existing humanoid probe's clip and models. HumanA covers both graph
clocks, all three culling modes, root motion disabled, builtin application, and
OnAnimatorMove calling ApplyBuiltinRootMotion with applyRootMotion both off and on.
HumanB's two AlwaysAnimate controls retarget the same clip with a different avatar,
root scale and parent scale/rotation. Foot IK is disabled; playable IK is requested.
Records include the initial unsampled state, root and API root pose, deltas,
velocities, body pose, bone transforms and movement/IK callback counts. Explicit
calls in frames 6, 16 and 33 supply 0, 0.37 and 0.74 seconds, crossing whole loops.
GameTime also advances automatically. The existing visibility schedule covers
visible, offscreen and returning rigs. The visibility mesh is added before
GetBoneTransform initializes the Animator's bindings.

| Question | Unity 6000.0 documented guarantee or limitation | Measured 6000.0.71f1 context |
| --- | --- | --- |
| Direct output | [AnimationPlayableOutput](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationPlayableOutput.html) connects an animation graph to an Animator; [AnimationClipPlayable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationClipPlayable.html) controls an AnimationClip. Neither specifies a separate humanoid output root algorithm. | These cases measure one direct clip, without a controller or intervening node. |
| Root extraction | [Root Motion manual](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html) projects the humanoid Body Transform to a Root Transform, computes its change each frame, and applies it to the GameObject. Bake settings can keep position/rotation in the pose; loop pose is relative to the root. Body-orientation projection can fail for strafing and needs an offset. | The existing nonmirrored looping clip has translation and yaw. The implementation reuses controller-side SampleMotion/ApplyMotion, including the feet pivot and body-relative pose. This does not establish a universal graph composition rule. |
| Retargeting | [Retargeting manual](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html) limits retargeting to humanoid models with configured Avatars. | Both authored avatars are valid; HumanB also checks the existing avatar/root transform conversion. |
| Application and callback | [applyRootMotion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-applyRootMotion.html) has no effect when OnAnimatorMove is implemented; changing it at runtime reinitializes the Animator. [OnAnimatorMove](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAnimatorMove.html) runs after animation evaluation and before OnAnimatorIK. | Flags are set before graph playback and never toggled here. Both callback cases apply the sampled builtin motion. No order between different Animators is asserted. |
| Motion fields | [deltaPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-deltaPosition.html), [deltaRotation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-deltaRotation.html), [velocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-velocity.html) and [angularVelocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-angularVelocity.html) describe the last evaluated frame and say applyRootMotion must be enabled for calculation. | The OnAnimatorMove case with the flag off measures the callback exception separately from that stated API prerequisite. Zero and successive evaluations retain every numeric field. |
| Culling and explicit time | [AnimatorCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.html) disables retargeting, IK and transform writes for CullUpdateTransforms, and animation for CullCompletely. [PlayableGraph.Evaluate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html) advances by the supplied interval. Neither documents visibility replay or humanoid feet-pivot state under replay. | The existing Q90 timing/culling machinery is retained. Root motion and its memory are sampled once per evaluation; visibility replay projects the retained local step into the current root frame without sampling the feet pivot again. |

Mixer/layer/controller-playable composition, an attached controller, fractional or
multiple outputs, loop overrides, mirrored clips, animation jobs/HumanStream and
other releases remain outside this measurement. Unsupported requested humanoid
root-motion contexts retain a diagnostic instead of using generic transform curves.

The Q91 Native repeats in `q91-native-final` are complete and byte-identical on
17,539 records. Publishing appended 1,924 keys and retained every prior record.
Earlier experimental captures initialized the Animator before the visibility mesh
existed and did not exercise the intended culling phases; they are retained outside
Git and are not this reference.

The first engine comparison exposed missing loop-pose correction on playable
muscle curves and replay of old world motion after the root had turned. Direct
humanoid output sampling now uses the existing loop-pose correction. Replay uses
the retained local step in the current root frame without advancing the clip or
feet-pivot memory. A pose-only visibility return also refreshes the humanoid API
root pose when the parent has moved. Before the first evaluation, the humanoid API root pose retains
the root captured when the Animator was enabled, even if game code has since
turned the Transform; the initial records preserve this distinction.

Managed regressions also guard two cases outside this Native capture: a paused
clip samples a stationary motion interval, following the 6000.0
[Pause API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableExtensions.Pause.html)
guarantee that evaluation does not advance its local time; and two separately
evaluated Manual graphs targeting one Animator retain the unsupported multiple-output
diagnostic. The first is an implementation inference from frozen time, not an
additional Native observation; the second guards the declared support boundary.


## Two-input humanoid mixer root motion (Q104)

`humanMixerMotion/` extends the direct-clip controls with one AnimationMixerPlayable,
one full-weight default-values AnimationPlayableOutput and one HumanA Animator without
a controller. It reuses `LoopMuscles` and the humanoid probe's authored `OrigTurned`
clip, both nonmirrored, with translation and yaw. Constant input weights are 0/1,
1/0 and 0.25/0.75. The endpoint controls use AlwaysAnimate with both clocks. The
interior blend also covers all three culling modes and root motion off, builtin
application, and OnAnimatorMove calling ApplyBuiltinRootMotion with either flag.
It preserves Q91's renderer-before-bone initialization and visibility schedule.
New records add both input clocks, weights and feet pivot to Q91's root, body,
bone, motion and callback observations. Explicit calls retain the 0, 0.37, 0.74
intervals, including loop crossings. No old reference key or tolerance is changed.

Unity contract checked for **6000.0**, measured release **6000.0.71f1**:

| Question | Documented guarantee or limitation | Measurement boundary |
| --- | --- | --- |
| Composition | [AnimationMixerPlayable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationMixerPlayable.html) controls an animation mixer; [Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationMixerPlayable.Create.html) specifies its input count. [AnimationPlayableOutput](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationPlayableOutput.html) connects a graph to an Animator. These pages do not specify a numeric humanoid blending formula or evaluation order between Animators. | Two clip inputs, fixed nonnegative weights summing to one, one output at weight one. No weight normalization is inferred for other graph settings. |
| Root and pose | [Root Motion](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html) describes projection of the Body Transform and applying its frame delta to the GameObject. Bake settings retain components in the pose; body-orientation projection can fail for strafing and requires an offset. Loop pose is relative to the root. [Retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html) requires humanoid models with configured Avatars. | Reuse the existing humanoid sampling, blending, feet pivot and retargeting. Numerical composition is an observation of this release/context, not a universal guarantee. |
| Motion API | [deltaPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-deltaPosition.html), [deltaRotation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-deltaRotation.html), [velocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-velocity.html), [angularVelocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-angularVelocity.html) describe the last evaluated frame, with applyRootMotion enabled as a prerequisite. | Both flag values with a callback are observed separately; every successive and zero-delta call is retained. |
| Movement delivery | [applyRootMotion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-applyRootMotion.html) has no effect when OnAnimatorMove exists; runtime flag changes reinitialize the Animator. [OnAnimatorMove](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAnimatorMove.html) follows animation evaluation and precedes OnAnimatorIK. | Flags stay constant. Counts and callback delta are captured, with no cross-Animator order assertion. |
| Clocks and visibility | [Evaluate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html) advances by the supplied interval. [AnimatorCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.html) and [Animator component](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Animator.html) describe disabling retargeting, IK and transform writes, or animation completely, offscreen. | First sample scheduling, retained motion and pose replay on visibility return are measured details, not documented global phase rules. |

Layers, controller playables, HumanStream, mirrors/additive graphs, attached
controllers, shared or fractional outputs, non-unit input weight sums and live
weight changes remain outside this certification.

The two independent Native captures in `q104-native` agree byte-for-byte on
19,611 records. Publication appended 2,072 keys and preserved all 17,539 prior
records. Before implementation, 1,972 new records differed, plus the four prior
rotation-predicate residuals. Endpoint controls matched after using the existing
humanoid motion pass. The interior pose exposed scalar blending of unnormalized
RootQ curves, which produced an extra body rotation after the normalized humanoid
sample was applied. The supported mixer now writes its blended humanoid RootX
back to those body bindings before retargeting. Generic and controller blending
retain their existing paths.

The interior blend also distinguishes the retained clip step from its feet-pivot
correction. After CullCompletely, visibility replay matches the retained blended
clip step projected in the current root frame, before that correction. Reusing
the corrected step introduced a persistent root offset, up to 0.0114 in a component
in the Manual control. Sampling and feet-pivot memory do not advance on replay.
This is a measured composition detail; direct clip replay remains unchanged.
[pivotPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-pivotPosition.html)
and [pivotWeight](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-pivotWeight.html)
describe the stable point between the feet and its left/right weight. They do not
specify an offscreen replay algorithm. In this mixer capture the public pivot stays
zero when motion is not requested, including on return; internal feet memory is
still maintained for pose/motion sampling.

Final strict comparison: Q104 2,072/2,072; Q91 1,924/1,924; Q90 9,322/9,322.
The full reference has 19,607/19,611 strict matches, no missing/extra keys, and only
the same four documented rotation-predicate residuals accepted by `verify_gate.py`.
Regressions: startup 2,223/2,223 and skinned bounds 2,063/2,063. The humanoid probe
retains its prior 203 differences, 428,164/428,367 values matching, with every raw
difference retained outside Git. The focused managed motion tests pass 35/35.
The existing `gate.json` command remains engine-only with an explicit release:

    python3 tests/projects/animculling/run.py gate --release "$UNBOUND_PROBE_UNITY_VERSION"
