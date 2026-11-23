# Humanoid probe

What humanoid avatars give a game on Unity 6000.0.71f1, for two rigs Blender exports
(`make_models.py`): HumanA maps every body bone Unity knows (upper chest, neck, shoulders, toes,
eyes, jaw) and three fingers per hand, rests in an A pose and has rolled bones; HumanB maps only
the required bones, with non-human nodes in between (a root, a spine helper, twist leaves), other
proportions and rolls; HumanT is HumanA with bone translations. tests/projects/animpartial plays HumanA
(model and `.meta`) in its `human.*` cases: a change of HumanA or its import settings needs that
probe's reference recorded again. The Editor configured them as Humanoid with an avatar created from the
model (`ProbeRunner.Create`): it mapped the bones and wrote the human description (bone mapping,
T-pose skeleton, parameters) into the `.meta` files, which are committed. Their clips loop and are
imported without keyframe reduction.

`HumanoidProbe` records:

- the `HumanTrait` tables (names, bone/muscle mapping, default ranges, required bones, parents,
  hierarchy masses);
- each imported avatar (`isValid`, `isHuman`, the human description back) and an instance's
  Animator (`isHuman`, `humanScale`, `avatarRoot`, `isOptimizable`, feet bottom heights,
  `GetBoneTransform` of every `HumanBodyBones`);
- `HumanPoseHandler` on the instance: the rest pose read back; poses written and read back (no
  muscles, every muscle alone at -1 and 1, twelve seeded random poses with body positions and
  rotations, four with muscles beyond the range) with every transform's local rotation and the
  root's and hips' local positions after each write (the rest pose is recorded in full);
- avatars built at run time with `AvatarBuilder` (human with the imported description, generic,
  generic with a root motion node, and a description missing a required bone with its error);
- the clips' humanoid properties; each rig playing its own clip, each rig playing the other's
  (retargeting), and HumanA's clip on a layer with an IK pass whose `OnAnimatorIK` sets hand, foot,
  hint and look-at goals and moves the body, on both rigs: `Animator.Update(0)`, 24 steps of 1/30 s
  and four of 0.1 s, every transform's locals, the human pose, body/root/pivot and what
  `OnAnimatorIK` read (goals, weights, hint, body) at every call;
- blended root motion on HumanA, HumanA's clip (A) with HumanB's (B): a transition from A to B
  (exit time 0.25, 0.3 s), B on an override layer of weight 0.5, B on an additive layer of weight 0.6,
  and a 1D blend tree of A and B at 0.35 (also on HumanB), with `deltaPosition`, `deltaRotation`,
  `velocity`, `angularVelocity` and each layer's state time, transition and clip weights per step.
- layer avatar masks over humanoid clips on HumanA: B on an override layer (0.5) and on an additive
  layer (0.6) through three humanoid masks (no transforms), every body part but Root (`MaskNoRoot`),
  only the upper body (Body, Head, arms, fingers, hand IK: `MaskUpper`) and the upper body with Root
  (`MaskUpperRoot`), each mask's body parts read back; and a 1D blend tree of A and B (0.35) on an
  additive layer of weight 0.6 (`additiveTree`), recorded like the blends above.
- the feet on override layers (0.5): `MaskUpperFootIK` (the upper body, Root and the foot IK parts,
  legs off) and `MaskNoFootIK` (every part but the foot IK ones).
- HumanA's clip imported again with non-default root settings (`ProbeRunner.Settings`), each on
  its own controller (`HumanSet<name>`), recorded like the blends without the transforms' locals:
  bake into pose of the rotation, Y and XZ (`BakeRot`, `BakeY`, `BakeXZ`), based upon the original
  (`OrigRot`, `OrigXZ`), the center of mass and the feet (`CoM`, `Feet`), offsets (`Offset`), loop
  pose, mirror and cycle offset (`LoopPose`, `Mirror`, `Cycle`), combinations (`BakeAll`,
  `BakeOrig`, `BakeOffset`, `BakeLoopPose`, `MirrorOffset`) and two clips without loop time (`OrigOnce`,
  `BakeOnce`); the clips' properties are recorded with the others. Three humanoid `.anim` clips
  (`ProbeRunner.MuscleClips`) whose body and muscles differ at the start and the stop:
  `LoopMuscles` (loop pose), `LoopMusclesBake` (loop pose, rotation and Y baked) and `MirrorMuscles`
  (mirror), and two more: `OrigTurned` (rotation and XZ based upon the original, the body turned 40
  degrees at the start) and `FeetRight` (Y based upon the feet, foot goal curves with the right foot
  lower). An exception in one of these clips or plays is recorded as `<key>.exception` and the
  probe goes on.
- mirrored states on HumanA (`ProbeRunner.MirrorStates`), recorded like the settings plays:
  HumanA's clip on a mirrored state (`stateMirror`), the Mirror, Offset and BakeOnce clips on one
  (`stateMirrorClip`, `stateMirrorOffset`, `stateMirrorOnce`), HumanB's clip (2/3 s) on one
  (`stateMirrorB`), with a state cycle offset of 0.25 (`stateMirrorCycle`), a state whose mirror is
  on but driven by the bool parameter Mirror, set at step 8 and cleared at step 18
  (`stateMirrorParam`), a 1D tree of A and B with B's child mirrored, on a plain and on a mirrored
  state (`treeMirror`, `treeMirrorState`), B mirrored on an additive layer of weight 0.6
  (`additiveMirror`), a transition from A to A mirrored (exit time 0.25, 0.3 s:
  `transitionMirror`), and the IK pass of `ik` on a mirrored state and with the Mirror clip
  (`ikMirror`, `ikClipMirror`).
- the first updates of states (`ProbeRunner.FirstSteps`, `HumanoidProbe.FirstSteps`), recorded like
  the settings plays plus `.detail` (each layer's current and next state hash, normalized time and
  length, the transition's normalized time and duration, the current and next clip infos), the
  `ProbeEvent` animation events (`HumanoidEvents`: marker, time, state hash and time, clip and its
  weight) and what each play logged (`.log`): A to B after exit times of 0.24, 0.26, 0.2, 0 and 1.24
  over 0.3 s and 0.24 without a duration (`exit*`); `CrossFade("B", 0.3)`,
  `CrossFadeInFixedTime("B", 0.25)`, `CrossFade("B", 0.3, 0, 0.5)` and `Play("B")` before step 5
  (`crossFade`, `crossFadeFixed`, `crossFadeOffset`, `playState`); an empty base state under B on
  an override layer of weight 0.5 and 0.25 (`emptyPartial`, `emptyPartialQuarter`), an empty state
  leaving for A after an exit time of 0.5 or when Go is set at step 5 (`emptyExit`, `emptyCond`), A
  leaving for an empty state after an exit time of 0.2 and coming back to A (state Back) after 0.3 of
  it, 0.1 s each (`emptyBack`, also `generic.emptyBack`); HumanA's Events clip (events at
  0.1, 0.45 and 0.8) on a plain, a mirrored and a mirrored state with a cycle offset of 0.25
  (`events`, `eventsMirror`, `eventsMirrorCycle`); the Mirror clip on a mirrored state of an
  additive layer (`additiveMirrorClip`); `Play` at normalized times 20000.3 (A) and 30000.3 (B)
  (`longState`, `longStateB`) and the clips in a Manual PlayableGraph at 70000.3 s (`longClipA`,
  `longClipB`, with the playable's time); and on a plain GameObject with generic clips that move
  and turn it (`GenericMove`, 1 s, `GenericBack`, 0.8 s; root motion applied) exit times of 0.24
  and 0.25, cross-fades and Play before step 5, an empty state leaving after 0.5 and under a layer
  of weight 0.5 (`generic.*`, with the GameObject's position and rotation).

  Measured (6000.0.71f1, two identical captures): a state Play or CrossFade enters keeps the
  requested time in the update it enters in (the cross-fade's transition advances); every entered
  state, generic or humanoid, transitions' destinations at any exit time with or without a
  duration included, moves the root over a whole update ending at its time; an exit time of 0 is
  first reached at the end of the first loop; in a transition from an empty state the destination's
  clip infos weigh 1 and its root motion applies in full; a mirrored state fires its humanoid
  clip's events at its pose's shifted times (half a cycle, plus its cycle offset); the Mirror clip
  on a mirrored additive state logs nothing; an update without a humanoid clip rests the feet pivot
  at the root (`Animator.pivotPosition` is the root, the pivot weight 0.5), so the next clip's
  first update moves the root by its feet pivot's way back to the root (about (-0.34, 31.4, 0) on
  HumanA); a layer over no humanoid clip moves the motion, the body and the feet from the identity by
  its weight and follows the feet by that weight; a transition into an empty state poses and moves
  the root with the motion state in full up to and including the update it completes in; at long
  play times a clip reads its time in seconds (single precision for states) divided by its length,
  rounded up to a single. Unbound matches these. Residuals (docs/COMPATIBILITY_GAPS.md): small-step
  velocities, and the root's x of `longClipA`, 2.8e6 units from the origin (its leg muscles match).

- the controls of `HumanoidLeftovers.cs` (assets from `ProbeRunner.Leftovers`): target matching on
  HumanA's clip with root motion applied (`match.*`: each `AvatarTarget` from 0.2 to 0.6, masks of
  XZ with half the rotation and of Y alone, a match across a loop, a start and a target already
  passed, `InterruptMatchTarget(true/false)`, a transition interrupting a completing and a
  cancelling match, a call in a transition, without root motion, on a clip without loop time, a
  second call (also changing completeMatch), an invalid target, on HumanB (also interrupted), a target
  before the start, two layers, also with B a loop ahead, and on a generic Animator playing
  GenericMove), each step with the GameObject's and the target
  bones' world transforms, whether it is matching, the state time and the root motion;
  `SetBoneLocalRotation` in `OnAnimatorIK` (`HumanoidBoneRotation`: spine, hips, head, upper chest,
  lower arm, upper leg and a finger on HumanA and HumanB, `boneRot`, `boneRotB`; one bone per IK
  layer of `HumanAIK2`, `boneRotLayers`; called outside the IK pass, `boneRotOutside`), with the
  transforms' locals every fourth step; `TDoFMuscles.anim` (translation DoF curves) and its mirrored
  copy `TDoFMirror.anim` on HumanA with an avatar with translation DoF built at run time and with its
  own (`tdof.TDoFMuscles`, `tdof.TDoFMirror`); HumanT's avatar and clips (`tdof.avatarT`,
  `HumanT.clip.*`), its clip and the clip mirrored on HumanA and HumanA's clip on HumanT
  (`tdof.playTOnA`, `tdof.playTMirrorOnA`, `tdof.playAOnT`); clips based upon the feet without foot goal curves and
  with the left foot's only (`feetNoGoals`, `feetLeftGoal`); B under `MaskNoRoot` over an empty base
  state and over `GenericMove` (`noRootOverEmpty`, `noRootOverGeneric`); loop pose on an additive
  layer's clip (`LoopMuscles` and HumanA's `LoopPose` clip over A, `additiveLoopPose`,
  `additiveLoopPoseModel`); and `layersAffectMassCenter` (`massCenter.api`, and on with layers
  without Root, `massCenter.*`).
  HumanT is `make_models.py`'s HumanA with bone translations, imported with translation DoF; the
  other assets `Leftovers` makes are not recorded yet. HumanT playing its own clip and the clip
  mirrored (`tdof.playT`, `tdof.playTMirror`, in `HumanoidBody.cs`) and the translation DoF curves
  its clips have (`tdof.curves`: per clip the number of Animator curves; per translation DoF
  curve its largest key and its last key, times 10000; the middle key of three muscle curves,
  for the clip with the Mirror setting too) are recorded. The Manual's
  [Muscle & Settings](https://docs.unity3d.com/6000.0/Documentation/Manual/MuscleDefinitions.html)
  page says Translate DoF enables translation animations for the humanoid, for the chest,
  upper chest, neck, upper leg and shoulder muscles, and names no minimum. Measured: the take
  moves five bones, each mostly along one or two axes of its parent bone's frame; of the 63
  translation DoF curves the clip has seven, with keys from 1.6e-4 to 4.3e-4; the largest of
  the others would be 9.2e-5 (the minimum lies between: Unbound takes 1e-4), and the bones
  play without the small components.
  HumanT's local units are a hundredth of its world units, so a bone's local position 9e-5 off
  (9 mm in the world) is inside the comparison's absolute tolerance; such an error shows in the
  hips and the body instead, which are placed from every bone. The curve queries show the clip
  with the Mirror setting as imported, after it has played mirrored.

- stream handles of a humanoid's bones in a job (`HumanoidBones.cs`, `bone.*`): a job above
  HumanA's clip with `Animator.BindStreamTransform` handles of the hips, the spine, the left lower
  arm, the head and the left hand, one job per change: none (`bone.read`), a local rotation of
  the lower arm (`bone.rotateLocal`), a world rotation of the head (`bone.rotateWorld`), both
  with the human stream and the bones read in between (`bone.rotateThenHuman`: `.job.humanBetween`,
  `.bones.between`), the hips' local position (`bone.moveHips`), the lower arm's world position
  (`bone.moveArm`), the spine's scale (`bone.scale`), a muscle set before the bones are read
  (`bone.humanThenRead`), and none with a handle of an object added under the left hand, which
  the avatar does not hold (`bone.prop`, recorded with the macOS Editor, two identical
  captures; `.prop`: its local position times 100 and rotation, its world position and
  rotation), and the left hand's goal moved by (10, 20, 0) world units and solved: alone
  (`bone.solve`), after the lower arm was turned and the human stream read (`bone.rotateThenSolve`)
  and solved, read back and solved again (`bone.solveTwice`), the three recorded with the macOS
  Editor (two identical captures). Each step records what the handles read before and after the
  change (`.bones.before`, `.bones.after`: local position times 100, the inverse of the rig's root
  scale; local rotation and scale; world position and rotation), the human stream after it
  (`.job.human`) and the final pose, with the transforms' locals every fourth step.
  Measured: the handles read the bones of the stream's pose; rotations and the hips' position
  written through them reach the final pose; a position or scale below the hips stays in the
  stream; the hips read the root's scale of 100 as their own and the positions below them are
  that much further from the hips than the transforms end up. Once the job has read the human
  stream after a bone change, the bones' local values are as before and their world positions
  are read under the root without its scale (the hips 1.02 units from the root where the
  transform is 101.8). The object under the hand is placed under the hand's bone of the stream's
  pose, this update: its world values are the hand handle's composed with its own local values,
  the hand's scale of 10000 included (its local offset of 0.0022 puts it 22 world units from the
  hand). A solve after the pose was read back works on that re-based skeleton: the goal is out of
  its reach, the arm ends straight and the body displaced toward the root (from z -105.5 to
  -1.07); Unbound does the same, its straight arm's roll split between the upper arm and the
  forearm is the bounded known class `readback-solve-straight-limbs`.

  The `bone.scaleHips`, `bone.scaleHipsThenHuman` and `bone.hipsTrs` controls
  add 604 rows from two identical Linux 6000.0.71f1 Editor runs. A non-uniform
  scale written through the hips handle changes the positions of descendants in
  the stream and survives reading the human pose. Retargeting observes the
  scaled body and keeps the scene hips scale at its authored value.
  The combined SetLocalTRS control also changes the position.
  Five more controls add 1084 rows. A descendant's world-position write uses the
  inverse of the same local transforms as its read. A hips TRS call with an
  unchanged scale leaves a preceding non-uniform spine scale and its descendants
  in the same frame. The default elbow hint uses the human pose's current placement,
  including its previously cached value. A solve before a human-pose read uses
  the placement before the scale edit. Three more controls add 662 rows from
  two identical macOS 6000.0.71f1 Editor runs. The first hint and solve also
  follow the earlier placement after a uniform scale edit. A first hint read
  after a non-uniform scale, a human-pose read and a subsequent arm turn uses
  the rebased skeleton. Separate diagnostic runs still show final-pose
  differences when solving after the human-pose read, with or without the
  arm turn. Unbound reports this case.
  [AnimationHumanStream.GetHintPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationHumanStream.GetHintPosition.html),
  [AnimationHumanStream.SolveIK](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationHumanStream.SolveIK.html).
  [TransformStreamHandle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.TransformStreamHandle.html),
  [Humanoid Avatar Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/AvatarCreationandSetup.html).

  The masked transform controls add 757 rows from two identical macOS
  6000.0.71f1 Editor runs. Humanoid bone handles return true for the
  position and rotation read masks and false for scale. A masked SetLocalTRS
  changes only the scale, and a masked SetGlobalTR leaves both fields as read.
  The head's masks are the same when the AvatarMask excludes it from the
  playable layer. The controls use an unoptimized hierarchy. Handles on
  optimized skeleton nodes and handles whose bone transform was destroyed
  keep the generic masks and masked writes. Those cases and repeated masked
  scale writes in one job remain unmeasured.
  [TransformStreamHandle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.TransformStreamHandle.html)

  The kinematic controls add 364 rows from two matching macOS 6000.0.71f1
  captures. A manual job over one humanoid clip reads its bones in
  ProcessAnimation with a kinematic Rigidbody, interpolation off and the
  default Animator update mode. There is no CharacterController or OnAnimatorMove. The
  rotated-arm control applies the same placement when it writes a bone. The
  Animator Transform has the frame from before this evaluation's movement.
  Separate diagnostic captures still show differences in ProcessRootMotion
  pose reads. See the separate scripted movement controls below.

  Five scripted movement controls add 1200 rows from matching macOS
  6000.0.71f1 captures. On a manual clip-to-job graph, ProcessAnimation
  reads the root after OnAnimatorMove. The controls include a direct
  Transform change, no movement, ApplyBuiltinRootMotion with applyRootMotion
  on and off, and a bone rotation after the script moves the root. The
  measured graph uses one eventless clip, the default Animator update mode
  and an Animator without a controller or physics body. Cases where the
  root phase opens the human pose or the graph runs automatically still
  use the reported fallback.
  [OnAnimatorMove](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAnimatorMove.html)

  The layer-mask controls add 1361 rows from two matching macOS 6000.0.71f1
  Editor runs. A ProcessAnimation job above two clips in an override layer
  mixer uses the layers that keep Root to place the hips. It reads body
  muscles from those layers and finger muscles from the blended stream.
  The controls also cover a body position edit, a world bone rotation that
  rebuilds the pose, and direct body muscle writes to both the placement
  and animated data. The unmasked and single-clip controls keep the regular
  pose rules. ProcessRootMotion and other graph shapes retain their report.
  [SetLayerMaskFromAvatarMask](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/animations/animationlayermixerplayable/setlayermaskfromavatarmask)

  Six no-input job controls add 948 rows from two matching macOS
  6000.0.71f1 Editor runs. In an explicit manual evaluation, a single
  full-weight default-values output of a no-input job applies the default
  humanoid pose even when the job writes nothing. A scene arm edit is
  replaced on the next evaluation. The controls also record a pose read
  in ProcessRootMotion, a bone write, a body and muscle write, and a write
  at Evaluate(0) followed by a return to the default pose. The Animator
  has no controller or movement callback, applyRootMotion is off, and
  its update mode is Normal. Other graph shapes and release families
  keep the reported fallback.
  [AnimationScriptPlayable.Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationScriptPlayable.Create.html),
  [AnimationStream.AsHuman](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationStream.AsHuman.html).

- states without a motion and without write defaults (`HumanoidPlayables.cs`, controllers from
  `ProbeRunner.Leftovers`): HumanA's clip leaving for such a state (Hold) after an exit time of 0.3
  at once (with an extra `Update(0)` before step 12, `noWDAlone`), over 0.2 s (`noWDFade`), at
  once under B on a layer without Root (`noRootNoWD`) and at once then back to the clip after 0.5 of
  the hold over 0.2 s (`noWDBack`), recorded like the settings plays with the root motion's deltas but
  not its velocities, and GenericMove doing the same on a plain GameObject (`generic.noWD` with the
  extra update, `generic.noWDBack`). Measured: the state keeps the layer's last root motion, so every
  update, an `Update(0)` included, moves the root again by the layer's last step in the root's
  frame, whatever the delta time, with the state's share of a transition into or out of it.

- Foot IK and layer mixers (`HumanoidPlayables.cs`, HumanFootIK from `ProbeRunner.Leftovers`): HumanA's
  clip on a state with Foot IK (`footIK`); in PlayableGraphs on HumanA (manual updates, root motion
  applied unless said, recorded like the settings plays with the root motion's deltas and
  velocities): HumanA's clip alone (`mixer.clip`, also without applyFootIK, `mixer.clipNoFootIK`),
  HumanB's alone (`mixer.clipB`), a layer mixer with HumanA's clip alone (`mixer.one`), HumanB's clip
  above it on an override layer of weight 0, 1 and 0.5 (`mixer.weight0`, `mixer.weight1`,
  `mixer.noneMotion`; without root motion `mixer.none`; without Foot IK `mixer.noIK`), both in an
  AnimationMixerPlayable at 0.5 (`mixer.plain`; with Foot IK on HumanA's clip only,
  `mixer.plainFootIKA`), B through the masks Upper, NoRoot and NoFootIK (0.5, `mixer.upper`,
  `mixer.noRoot`, `mixer.noFootIK`; NoRoot with layersAffectMassCenter, `mixer.noRootMassCenter`),
  additive (0.6) through Upper and NoRoot (`mixer.additiveUpper`, `mixer.additiveNoRoot`) and the
  FeetNoGoals clip alone (`mixer.feetNoGoals`); and a state with Foot IK leaving for HumanB's clip
  without it over 0.3 s (`footIKTransition`, HumanFootIKTransition). Measured: see
  docs/COMPATIBILITY_GAPS.md (humanoid row, layer mixers and Foot IK).

- IK goals an Animator Controller blends (`HumanoidGoals.cs`, the HumanGoals* controllers from
  `ProbeRunner.GoalBlends`, each with an IK pass on its base layer): on HumanA with root motion
  applied, `HumanoidGoals` records the body and every goal's position, rotation and weights as
  `OnAnimatorIK` reads them (`<key>.step<n>.goals<call>`), beside the pose and root motion of the
  settings plays. A is HumanA's clip, B HumanB's, transitions leave after an exit time of 0.3 over
  0.3 s and trees are 1D of A and B at 0.35: A to B with Foot IK on both (`goals.transition`, also
  on HumanB, `goals.transitionB`) and B to A (`goals.backTransition`), the tree with Foot IK
  (`goals.tree`), B without Foot IK to the tree with it (`goals.treeTransition`), the tree with
  Foot IK to A without it (`goals.treeSource`), A to FeetNoGoals, a clip without goal curves
  (`goals.noGoals`); A with Foot IK under B on an override layer of weight 0.5 (`goals.override`),
  through MaskNoFootIK (`goals.overrideNoFootIK`) and through MaskUpper (`goals.overrideUpper`),
  and with the Foot IK on B's state only (`goals.overrideFootIK`); A with Foot IK under B on an
  additive layer of weight 0.6 (`goals.additive`) and with the Foot IK on B's state only
  (`goals.additiveFootIK`). The plays with a transition record each layer's states and clips in
  detail. Measured: see docs/COMPATIBILITY_GAPS.md (humanoid row, IK goals on Animator
  Controllers).

- GenericMove (root curves, no humanoid curves) on HumanA with root motion applied, alone on a
  controller (`genericOnHuman`, HumanGenericMove) and in a PlayableGraph (`genericOnHuman.graph`).
  Measured: the root moves by the curves times the human scale either way, and the avatar takes the
  pose with every muscle 0 and the body on the root.

- animation jobs on the human stream (`HumanoidPlayables.cs`, `HumanStreamJob`): an
  AnimationScriptPlayable above HumanA's clip in a PlayableGraph, root motion applied, records what it
  reads (`JobRecord`: the human scale, feet heights, body local and in the world, five muscles, the
  stream's and the feet's velocities, every goal's position, rotation, local position and rotation,
  pose position and rotation and weights, every hint) before and after one change per mode: none
  (`stream.read`, also without root motion in `stream.readNoMotion` and above a mixer of HumanA's and
  HumanB's clips in `stream.mixer`), the body (`stream.body`), the local body (`stream.bodyLocal`),
  muscles (`stream.muscles`), hand and foot goals, weights and a knee hint with `SolveIK`
  (`stream.ik`) and without (`stream.ikNoSolve`), the knee hint alone (`stream.hintNoSolve`), the
  weights alone (`stream.weightsNoSolve`), local goals (`stream.localGoals`), look-at
  (`stream.lookAt`), `ResetToStancePose` (`stream.stance`); two chained jobs, muscles then IK
  (`stream.chained`); the IK job with an `OnAnimatorIK` script (`stream.ikPass`); and the
  `MuscleHandle` list (`stream.muscleHandles`). Measured: see docs/COMPATIBILITY_GAPS.md (humanoid
  row, AnimationHumanStream).

- the body where clips blend (`HumanoidBody.cs`, the Body*.anim clips and HumanBody* controllers
  from `ProbeRunner.BodyBlends`, each with an IK pass on its base layer and `HumanoidGoals`
  reading the body and the goals): five `.anim` clips of one second that differ in their body
  curves only, BodyPos (RootT and all four RootQ curves), BodyNeg (the same rotation with every
  RootQ key negated), BodyPartial (RootQ.x and RootQ.y only), BodyW (RootQ.w only, -1) and BodyNone
  (no body curve); each in a 1D tree with HumanA's clip A at 0.35 (`body.posTree`, `body.negTree`,
  `body.partialTree`, `body.wTree`, `body.noneTree`), BodyNeg to A and A to BodyNone in transitions
  (`body.negTransition`, `body.noneTransition`), BodyNeg under A and A under BodyPartial on an
  override layer of weight 0.5 (`body.negOverride`, `body.partialOverride`); Direct blend trees of
  BodyNeg 0.35 and A 0.65 (`body.directNeg`), BodyPos and A at 0.3 and 0.4 (`body.directLow`) and
  at 0.8 and 0.6 (`body.directHigh`, with normalized blend values `body.directNormalized`), both
  with Foot IK (`body.directLowFootIK`, `body.directHighFootIK`), the latter leaving for HumanB's
  clip (`body.directHighTransition`), BodyNeg and A at 0.3 and 0.4 (`body.directLowNeg`), BodyNegGoal
  (BodyPos with a negated left foot goal rotation, which its body places with four negative
  components except in one recorded update) alone at 0.5 (`body.directHalfNegGoal`), HumanB's clip at 0 (`body.directZeroB`) and at 0.5
  (`body.directLowB`) and an empty child tree at 0.3 beside A at 0.7 (`body.directEmptyChild`); BodyRot (RootQ curves
  only) and BodyT (RootT curves only) in 1D trees with A (`body.rotTree`, `body.tTree`); A under an override layer of weight 1 that leaves
  an empty state for HumanB's clip (`body.emptyLayerTransition`); the same clips without Foot IK in AnimationMixerPlayables
  (`bodyGraph.neg`, `.pos`, `.partial`, `.w`, `.none` at 0.35 and 0.65 of A, `.low`, `.high`, `.lowNeg`,
  `.three` of BodyNeg, A and BodyPartial, `.nested` with a mixer of BodyNeg and BodyPos as an
  input) and A under BodyPartial in a layer mixer (`bodyGraph.layerPartial`); controllers of the
  probe as the source of the Animator's only output through an AnimatorControllerPlayable
  (`controllerGraph.a`, `.footIK`, `.goalsTransition`, `.goalsTree`, `.goalsOverride`,
  `.goalsAdditive`, `.bodyNegTree`, `.bodyDirectLow`, `.maskOverrideNoRoot`, `.maskOverrideUpper`,
  `.maskAdditiveNoRoot`), and HumanGoalsTransition with the playable paused from the 6th to the
  13th update (`controllerGraph.paused`). Authored but not
  recorded, because the engine does not match them yet (docs/COMPATIBILITY_GAPS.md says how they
  differ): the controllers HumanBodyGenericTransition (A to GenericMove) and HumanBodyOnlyOverride (A under BodyOnly, body curves and no muscle curve, on an
  override layer of weight 0.5). Measured: see docs/COMPATIBILITY_GAPS.md (humanoid row).

Record two independent Editor captures (GPU, private Xvfb), publish after they agree, run Unbound,
compare:

```bash
python3 tests/projects/humanoid/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/humanoid/run.py publish --release 6000.0.71f1 --label <label>
UNBOUND_CLI=<unbound> python3 tests/projects/humanoid/run.py unbound --release 6000.0.71f1 --label <run>
python3 tests/projects/humanoid/compare.py tests/projects/humanoid/Reference/unity-6000.0.71f1.jsonl \
  ~/.cache/unbound/validation/runs/humanoid/<run>/unbound/values.jsonl
```

Captures go to `$XDG_CACHE_HOME/unbound/validation/runs/humanoid/<label>`. The comparison lists
every missing, extra and differing value with a per-section summary; numbers match within 2e-4
(absolute or relative), a quaternion also matches its negation.

Known differences (`known.json`, by class with its cause and either the exact `<key> <field>` entries or bounds, see compare.py; only
keys of the human stream controls are listed): each is printed as `known <class>: ...` and counted
in the summary's `known` number instead of its differences, and an entry that matches again is
printed as `NOW MATCHES`. `feet-pivot-velocity`: Animator.velocity at steps 5 and 17 of HumanA's
clip, the residual every HumanA play of the probe already has; `foot-ik-legs`: leg muscles up to
2.4e-3 on single steps after the Animator's own Foot IK on a pose a job changed (consistent with
amplified rounding of a nearly straight leg); `stance-drift`: the final pose after a job's
ResetToStancePose on an Animator root motion has moved from the origin (a static placement there
does the same, measured outside the reference), where the Editor's
stance values step with the distance (body up to 3.3e-3, a straight leg's twist muscle up to
1.5e-2 by update 28). `stream.stanceInPlace` (recorded last, built in `HumanoidBones.cs`) is
the same job without root motion applied, so that the Animator stays at the origin: there every
value matches.
`readback-solve-straight-limbs` is bounded instead of listed (its `bounds` and `sums`, see
compare.py): in `bone.rotateThenSolve` and `bone.solveTwice`, the arm roll muscles 41 and 43 and
the left foot's 26 and 27 within 1e-2 each, the roll muscles' sum within 1e-4 of the Editor's on
every step, and the upper arm's and hand's local rotations within 1e-2 per component; a value
beyond its bound counts as a difference.

`apiCurves.*` (recorded after it, built in `HumanoidLeftovers.cs`): a clip made at run time whose
humanoid curves (a spine, a left arm and a left thumb muscle, the body's height and turn) are set
through `AnimationClip.SetCurve` (`setCurve`), `AnimationUtility.SetEditorCurve` (`editorCurve`)
and `SetCurve` with the clip's Mirror setting on (`mirror`), played without foot IK in a manual
graph on HumanA, and the mirrored clip with foot IK (`mirrorFootIK`). The Editor takes them as the clip's humanoid curves: `humanMotion` is true, the
bindings are the Animator's, the muscles and the body play, and the mirrored clip moves the right
arm and thumb. These records come from two identical captures of the macOS Editor; the rest of
those captures differ from the Linux reference in 287 values beyond the comparator's tolerance
(root motion velocities, IK, the stance drift, one pivot), so only these records were added.

Thirteen additional controls append 115 rows from a matching pair of macOS 6000.0.71f1 Editor
captures. The sampled goal rotation fills any missing components from the identity before
mirroring. When the entire rotation is missing, its W moves to Y for feet and to Z for hands.
Partial foot and hand rotations follow the same mapping. The controls include a fully authored
foot rotation, a foot position with an omitted rotation, and Foot IK through a clip, a controller
playable, a mirrored state and an attached controller. The original `mirrorFootIK` control now
matches within the comparator's existing tolerance and is checked with the other controls.
Five movement controls record both foot velocities in jobs over clips with moving body curves,
and MatchTarget on the right foot of an attached controller. They compare plain and mirrored
clips and a mirrored partial foot rotation. The MatchTarget check uses a rotation-only weight
mask. These job controls check the sampled foot velocities.
Families without measured defaults still report the fallback.

Six playable root-motion controls append 66 rows from a matching pair of macOS 6000.0.71f1
Editor captures. A mirrored clip moves and turns the Animator when it is the source of a single
full-weight manual output or the input to one animation job. The controls record root motion,
final poses, an animated yaw, Foot IK, and the stream pose above a moving clip. The measured
Animator has no controller, movement script or physics body, the Normal update mode, and
applyRootMotion enabled. The graph uses explicit evaluation and the DefaultValues stream source.
Other mirrored playable shapes and release families retain the report.
[Animator.applyRootMotion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-applyRootMotion.html),
[PlayableGraph.Evaluate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableGraph.Evaluate.html).
[Animation tab](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AnimationClip.html),
[AnimationHumanStream.GetGoalLocalPosition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationHumanStream.GetGoalLocalPosition.html).

Ten `additiveEmptyBody.*` controls append 950 rows from matching macOS 6000.0.71f1
Editor captures. An additive clip over an empty base state with write defaults adds its weighted
body difference from the identity. The Animator's root motion remains zero. The controller
playable as a single full-weight explicit manual output and the controller attached to an
Animator give the same placement, with root motion on or off. The controls vary the additive
layer weight, mask Root off, and compare a humanoid base clip. Two controls fade from a moving
base clip to the empty state. The feet stop moving the root when the fade finishes. Controller
playables below mixers or jobs use the reported fallback. Separate diagnostic captures keep
the differences for an active additive layer over a generic base and for an immediate Play cut.
They also record the nonfinite body Unity produced when an explicit additive reference lacked body curves.
[Animation layers](https://docs.unity3d.com/6000.0/Documentation/Manual/AnimationLayers.html).

Five `genericController.*` controls append 475 rows from matching macOS 6000.0.71f1
Editor captures. A generic base clip drives the root of a humanoid through an explicit
manual controller playable with a single full-weight DefaultValues output. The controls
compare a controller with only the generic clip, one with an inactive humanoid state, and
one with a zero-weight additive humanoid layer. They cover root motion enabled and disabled,
with the root and body pose, velocities, and delta motion recorded at each step. A separate
full capture keeps the open positive-weight additive case (728 differing values).
[Root motion](https://docs.unity3d.com/6000.0/Documentation/Manual/RootMotion.html),
[AnimatorControllerPlayable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorControllerPlayable.html).

After changing the rigs, regenerate them (`blender -b --factory-startup --python
tests/projects/humanoid/make_models.py`), let the Editor configure them in a copy of the project
(`Unity -batchmode -nographics -projectPath <copy> -executeMethod ProbeRunner.Create`) and copy its
`Assets/Resources` metas and controllers back.
