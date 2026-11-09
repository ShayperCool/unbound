# Optimize Game Objects probe

Model import "Optimize Game Objects" (`ModelImporter.optimizeGameObjects`, Extra Transforms to
Expose) and `AnimatorUtility.OptimizeTransformHierarchy` / `DeoptimizeTransformHierarchy` on
Unity 6000.0.71f1. `make_models.py` writes an ASCII FBX rig (no Blender needed): a `Rig` null with
the bone chain `Hips/Spine/Hand` and `Hips/Leg`, a rigid `Prop` mesh under `Hand`, a `Body` mesh
skinned to the four bones, and a take that moves the hips, turns the spine and raises the hand.

| Model | Import |
|---|---|
| Rig | generic avatar from the model, not optimized |
| RigOpt | optimized, nothing exposed |
| RigExposed | optimized, `Rig/Hips/Spine/Hand` exposed |
| RigExposedRoot | optimized, `Rig/Hips` (the skin's root bone) exposed |
| TurnedOpt | the rig under a single top-level null turned -90° about X and scaled by 2 (the root), `Rig/Hips/Spine/Hand` exposed |
| Human / HumanOpt | the humanoid probe's `HumanA` as it is there / optimized with its left hand exposed |

Every model imports without keyframe reduction. `AnimOptimizeProbe` records, per model: the
prefab asset's hierarchy (paths, local poses, components); an instance's
`Animator.hasTransformHierarchy`/`isOptimizable`, what `Transform.Find` and
`GetComponentsInChildren` see, the skinned renderer's bones, root bone and mesh; the instance
playing its take through `Animator.Update` (0, 0.5, 0.25 s): every local pose and the skinned
vertices in world space (sorted: the vertex order is the importer's). Four models play again moved
to (3, 4, 5) and turned 30° (with world positions). Runtime controls start from the unoptimized
rig with objects added under bones (an empty chain under `Hand`, a collider under `Leg`), from
optimized models, and from objects without an Animator: optimize with and without exposed paths,
optimize twice, a missing exposed path, deoptimize (from optimized and unoptimized hierarchies)
and play on. Every log message is recorded in order.

The assets are committed. To make them again, regenerate the rig and let a disposable Editor
copy write the `.meta` files and controllers back (the humanoid controls use `tests/projects/humanoid`'s `HumanA.fbx`, which every project copy takes from there):

```bash
python3 tests/projects/animoptimize/make_models.py
python3 tests/projects/animoptimize/run.py create --release 6000.0.71f1
```

Record two independent Editor captures (GPU, private Xvfb), publish after they agree, run
Unbound and compare (modelroot's comparator: every missing, extra and differing value, numbers
within 2e-4, a rotation also matches its negation):

```bash
python3 tests/projects/animoptimize/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/animoptimize/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/animoptimize/run.py unbound --release 6000.0.71f1
```

What the Editor does (measured):

- Optimizing: every skinned renderer loses its bones; one whose root bone is not exposed moves to
  the root bone's world pose and has no root bone. The skeleton's transforms move under the
  Animator keeping their world pose; then only transforms with components besides Transform, the
  exposed ones (found by their last name among the Animator's children) and their ancestors
  remain. A transform outside the avatar's skeleton stays under its parent (which then remains).
  Transforms sharing a name warn (`Transform '<path>' has the same name (<name>) as transform
  <path>. ...`), after a missing exposed path's warning (`Unable to optimize missing transform on
  GameObject '<name>'`). Optimizing an optimized hierarchy or an object without an Animator does
  nothing; the Animator keeps its state.
- Playing: transform curves animate the skeleton. Each of the Animator's children named as a
  skeleton node takes that node's pose relative to the Animator (the root's own pose not
  included: the turned and placed models expose the same locals); a skinned renderer's follows its
  mesh's root bone. The skin deforms as the unoptimized model's. A humanoid's skeleton carries the
  avatar's scale on its hips (the exposed hand of `HumanOpt` sits at the avatar-space pose, as the
  hips of the unoptimized humanoid do).
- Deoptimizing: the skeleton's transforms come back in the avatar's order (the model file's, not
  the sorted hierarchy's), existing ones of the name reused, each at the avatar's default pose;
  the skinned renderers get their bones and root bone back. The Animator plays on from its state.
