# Humanoid probe

What humanoid avatars give a game on Unity 6000.0.71f1, for two rigs Blender exports
(`make_models.py`): HumanA maps every body bone Unity knows (upper chest, neck, shoulders, toes,
eyes, jaw) and three fingers per hand, rests in an A pose and has rolled bones; HumanB maps only
the required bones, with non-human nodes in between (a root, a spine helper, twist leaves), other
proportions and rolls; HumanT is HumanA with bone translations. The Editor configured them as Humanoid with an avatar created from the
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
  velocities, and the legs and the root's x of `longClipA`/`longClipB`, 2.8e6 units from the
  origin.

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
  other assets `Leftovers` makes and HumanT playing its own clips are not recorded yet.

- states without a motion and without write defaults (`HumanoidPlayables.cs`, controllers from
  `ProbeRunner.Leftovers`): HumanA's clip leaving for such a state (Hold) after an exit time of 0.3
  at once (with an extra `Update(0)` before step 12, `noWDAlone`), over 0.2 s (`noWDFade`), at
  once under B on a layer without Root (`noRootNoWD`) and at once then back to the clip after 0.5 of
  the hold over 0.2 s (`noWDBack`), recorded like the settings plays with the root motion's deltas but
  not its velocities, and GenericMove doing the same on a plain GameObject (`generic.noWD` with the
  extra update, `generic.noWDBack`). Measured: the state keeps the layer's last root motion, so every
  update, an `Update(0)` included, moves the root again by the layer's last step in the root's
  frame, whatever the delta time, with the state's share of a transition into or out of it.

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

After changing the rigs, regenerate them (`blender -b --factory-startup --python
tests/projects/humanoid/make_models.py`), let the Editor configure them in a copy of the project
(`Unity -batchmode -nographics -projectPath <copy> -executeMethod ProbeRunner.Create`) and copy its
`Assets/Resources` metas and controllers back.
