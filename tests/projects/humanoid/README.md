# Humanoid probe

What humanoid avatars give a game on Unity 6000.0.71f1, for two rigs Blender exports
(`make_models.py`): HumanA maps every body bone Unity knows (upper chest, neck, shoulders, toes,
eyes, jaw) and three fingers per hand, rests in an A pose and has rolled bones; HumanB maps only
the required bones, with non-human nodes in between (a root, a spine helper, twist leaves), other
proportions and rolls. The Editor configured both as Humanoid with an avatar created from the
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
- the Animator's `velocity`, `angularVelocity`, `deltaPosition` and `deltaRotation` after every step;
- HumanA with a second layer playing HumanB's clip: an override layer masked to the arms, an
  additive one (weight 0.7) masked to body and head, an override one (0.8) masked to the left leg
  and the root with a transform mask, and a full-body one (0.5) with and without
  `layersAffectMassCenter`; a transition between the two clips and a 1D blend tree (0.4); a state
  with Foot IK, with and without `stabilizeFeet`; `SetBoneLocalRotation` in `OnAnimatorIK`;
- HumanT, a copy of HumanA whose avatar has translation DoF (its avatar, handler, clip, playback on
  itself and on HumanB);
- `HumanPoseHandler` made from joint paths: a human pose written and the avatar pose read (seven
  floats per joint), the rest locals written as the avatar pose and the human pose read back.

`ProbeRunner.Create` also makes the masks, the controllers and HumanT (`CreateBlends`).

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
