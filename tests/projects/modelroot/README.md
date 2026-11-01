# Model root motion probe

Root motion of imported generic clips on Unity 6000.0.71f1: a root node (`motionNodeName`), Bake
Into Pose and its related clip settings, the clips' motion statistics and `MatchTarget` on generic
rigs. Humanoid rigs are not covered.

`make_models.py` (Blender, 24 fps, one take of frames 0..48) writes `Walker.fbx` (top-level Hips
walking forward with sway, bob, a 90 degree turn and tilts; child Leg; a Prop) and `Mover.fbx`
(the same motion on its single top-level node, which becomes the model's root, so the clips get
root curves). Copies differ in their `.meta`: Walker (avatar created from the model, root node
Hips), WalkerTilt (the same, but its nodes keep Blender's -90 degree X rotation and scale 100),
WalkerNoNode (avatar, no root node), WalkerNoAvatar, Mover and MoverAvatar. Each has 17 clips:
loop on and off, ranges, cycle offset, loop pose, and each Bake Into Pose setting (rotation, Y,
XZ, all), "based upon original" for each, height from feet, orientation offset and level.

`RootProbe` records each clip's `averageSpeed`, `averageAngularSpeed`, `apparentSpeed`,
`averageDuration`, flags, settings and curve bindings (and the curves of four clips), and plays
every clip with `Animator.Update(0)` and twenty steps of 0.125 s with root motion on (three clips
also off), recording the root's pose, the deltas, velocities, root pose API and the animated
nodes. The `match.*` cases call `MatchTarget` for the root (start 0.3, target 0.8 of the state).

The assets are committed. To make them again, regenerate the models and let a disposable Editor
copy write the `.meta` files and controllers back:

```bash
blender -b --factory-startup --python tests/projects/modelroot/make_models.py
python3 tests/projects/modelroot/run.py create --release 6000.0.71f1 --label <label>
```

Record two independent Editor captures (GPU, private Xvfb), publish after they agree, run
Unbound, compare (every missing, extra and differing value; numbers within 2e-4, a rotation also
matches its negation):

```bash
python3 tests/projects/modelroot/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/modelroot/run.py publish --release 6000.0.71f1 --label <label>
UNBOUND_CLI=<unbound> python3 tests/projects/modelroot/run.py unbound --release 6000.0.71f1 --label <run>
python3 tests/projects/modelroot/compare.py \
  tests/projects/modelroot/Reference/unity-6000.0.71f1.jsonl \
  ~/.cache/unbound/validation/runs/modelroot/<run>/unbound/values.jsonl
```

What the Editor does (measured):

- With an avatar and a root node, every clip gets MotionT/MotionQ curves on the Animator: the
  node's position and rotation relative to the model's root. Without an avatar or a root node
  there are none; root curves (a single top-level node) stay as they are.
- Root motion follows the MotionT/MotionQ curves exactly as it follows root curves: the change
  from the previous evaluation, in the root's frame (the node's own scale does not apply). The root node
  keeps its own curves. `Animator.hasRootMotion` stays false for generic avatars.
- Bake Into Pose (rotation, Y, XZ), "based upon original", height from feet, orientation offset
  and level change nothing at play time, for root nodes and root curves alike. They only zero the
  baked components of `averageSpeed` (Y; X and Z).
- `averageSpeed` is the motion over twenty equal parts of the clip, each in the node's frame at
  its start, over the length; `averageAngularSpeed` the turn about the up axis likewise;
  `averageDuration` the length. Clips without MotionT/MotionQ curves have zero speeds.
- `MatchTarget` (AvatarTarget.Root) works on generic rigs, also without root motion: from the
  first evaluation at or past the start, each step adds the step's share of the correction,
  (min(t, end) − t₀) / (end − t₀), where the correction is the target less where the clip's
  motion alone would bring the root by the end; rotations turn by the normalized lerp towards the
  target. The step past the end completes the match and `isMatchingTarget` turns false.
- `apparentSpeed` is `|averageSpeed| · (1 + (averageAngularSpeed / 2)²)`, the product in double
  precision, as for humanoid clips: the speed's length for clips that do not turn, more for clips
  that turn about the up axis (1.154 times for a 90 degree turn in two seconds).

Unbound matches 3714/3714 records.
