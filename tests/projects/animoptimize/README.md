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
optimize twice, a missing exposed path, the skin's root bone exposed by name, deoptimize (from optimized and unoptimized hierarchies)
and play on. Humanoid controls (`h.*`): the optimized humanoid deoptimized after playing, the
unoptimized one optimized at run time with its left hand exposed, played, deoptimized and played
on, and a `HumanPoseHandler` on both reading the played pose and setting one with an arm muscle
and the body's height changed. Animation job controls (`job.*`, `jobRead.*`, `jobScene.*`,
`jobNone.*` on `Rig` and `RigExposed`): a job over the take with a stream handle and a scene
handle of the hand (the stream handle written or only read, the scene handle alone, or none),
three evaluations of a manual graph without the controller. On `Rig` also: the job calling
`Resolve` (`jobResolve`), `Animator.ResolveAllStreamHandles` before the first evaluation
(`jobResolveAll`) or between the first two of a job that reads nothing (`jobIdleResolveAll`,
beside `jobIdle`), a custom stream property's handle read or not (`jobProp`, `jobIdle`), a
handle bound before the hierarchy is optimized with the hand exposed (`jobEarly`), the
handle of an exposed top node (`jobTop`), the hierarchy optimized between the first two
evaluations of a job that read the handle (`jobReoptimize`) and the hand destroyed there
(`jobDestroyed`, the job reading the stream handle only while it is unresolved;
`jobDestroyedRead`, the job reading it in every evaluation), then its parent `Spine` destroyed
between the second and the third (`jobParentDestroyed`), or the hand destroyed with its animated
grandparent `Hips` (`jobBranchDestroyed`) or with the whole skeleton under the unanimated top
node `Rig`, raised by 3 before the handles are bound (`jobRootDestroyed`), between the first two;
the same with `Animator.Rebind` after the raise (`jobRootRebound`) and with `Rig` raised before
the Animator is first enabled (`jobRootInactive`); and the handles of the unanimated `Leg`, moved
by 1 along z before they are bound (`jobUnanimated`), also over a mixer whose one input, a clip
holding `Leg` elsewhere, has weight 0.5 and then 0 (`jobUnanimatedMixer`), or 0 from the start
with `Leg` moved to z = 2 by the script before the second evaluation (`jobUnanimatedIdle`). Last of all, a job
reads a property handle of the hand's z position bound with `BindStreamProperty`
(`jobTransformProperty`). Graph controls on a rig without
its controller, three evaluations of a manual graph each with the whole pose: the take as the
output's source on `Rig`, `RigOpt`, `Human` and `HumanOpt` (`graphOnly.*`), and on the two
humanoids under a job that does nothing (`graphJob.*`) or reads a stream handle of the left
hand, a bone of `Human` and the exposed transform of `HumanOpt` (`graphJobHand.*`); on
`HumanOpt` also a handle of the Animator's own transform (`graphJobRoot`) and of an object
added below it (`graphJobAdded`), and three `Animator.Update` calls with the controller kept,
without a handle and with the exposed hand bound (`update`, `updateHand`). Every log message
is recorded in order.

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

- A humanoid deoptimizes the same way (default pose, the model file's order) and plays on.
- A `HumanPoseHandler` on an optimized humanoid reads the stance pose placed at the root, not
  the animated one, and `SetHumanPose` still moves the exposed hand.
- A `TransformStreamHandle` reports `IsResolved` false in its first evaluation until the job
  accesses it (the Scripting API's
  [TransformStreamHandle.Resolve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.TransformStreamHandle.Resolve.html)
  says handles are lazily resolved as they are accessed). A job's `Resolve` resolves it; one
  the job never reads stays unresolved, and `Animator.ResolveAllStreamHandles` before or
  between the evaluations of the manual graph does not change that (its
  [page](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorJobExtensions.ResolveAllStreamHandles.html)
  says "you can manually resolve all handles from the main thread"; `IsResolved` does not
  show it in this capture). A custom stream property's handle is resolved at its first read
  in the same way.
- [TransformStreamHandle.IsResolved](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.TransformStreamHandle.IsResolved.html)
  says a handle "can become unresolved if the animator bindings have changed or if the
  transform had been destroyed". Optimizing the hierarchy after the job read the handle
  leaves it unresolved until the next access (`jobReoptimize`), and its scene handle then
  reads the exposed hand. After the hand is destroyed the stream handle stays valid and
  resolved and the scene handle is invalid (`jobDestroyed`), and a job that goes on reading
  the stream handle gets the stream's values of its path: the local ones of the take and the
  world ones under the hand's parent (`jobDestroyedRead`). Its world values stay what the take
  gives the path when the parent, an animated ancestor or the top node is destroyed too
  (`jobParentDestroyed`, `jobBranchDestroyed`, `jobRootDestroyed`: the same records as
  `jobDestroyedRead`).
- A skeleton node that nothing animates holds the avatar's default pose in the stream, not its
  scene values, and the stream leaves the scene transform alone: the moved `Leg` reads its
  default local and world values while the transform keeps its move (`jobUnanimated`), and the
  stream's world values below the raised `Rig` leave the raise out while the scene handle and
  the transform include it, whether `Rig` was raised before the handles were bound, before
  `Animator.Rebind` or before the Animator was first enabled (`jobRootDestroyed`,
  `jobRootRebound`, `jobRootInactive`).
- Where a mixer input animates the moved `Leg` at weight 0.5 the stream blends with the value
  bound from the scene; at weight 0 the job reads the default pose again and the output writes
  the bound value to the transform (`jobUnanimatedMixer`), in every evaluation from the first
  one, undoing the script's move (`jobUnanimatedIdle`).
- A property handle of a Transform property is valid but never resolves: before the first
  evaluation's record the Editor logs the assertion `Transform bound type not supported,
  binding: 'Rig/Hips/Spine/Hand.m_LocalPosition.z'` and the warning `Could not resolve
  'Rig/Hips/Spine/Hand' for property m_LocalPosition.z`, once, and every `GetFloat` throws `The
  PropertyStreamHandle cannot be resolved.` (`jobTransformProperty`).
- The controls `jobParentDestroyed` to `jobTransformProperty` and `jobUnanimatedIdle` were recorded with the macOS Editor (two
  identical captures); every other record of that capture matches the Linux reference within the
  comparator's tolerance, so the reference keeps its Linux records and adds these.
- A manual graph plays the take on a rig without its controller, optimized or not, generic
  or humanoid, alone or under a job (`graphOnly.*`, `graphJob.*`). On the optimized humanoid
  with its exposed hand bound by `Animator.BindStreamTransform`, or an object added below the
  Animator bound, nothing plays: before each evaluation's record the Editor logs twice the
  error `Transform 'Hips' for human bone 'Hips' not found` and the warning `Object is set to
  Optimize Game Object but does not have an Avatar, Animation will not play.`, the job does
  not run and the hand stays (`graphJobHand.HumanOpt`, `graphJobAdded.HumanOpt`). With the
  controller kept and the hand bound each `Animator.Update` logs the pair three times and the
  state does not play (`updateHand.HumanOpt`). A handle of the Animator's own transform
  leaves it playing (`graphJobRoot.HumanOpt`), as none does (`update.HumanOpt`).
- An exposed transform bound with `Animator.BindStreamTransform` no longer follows its skeleton
  node: the stream holds the pose it was bound at, the job reads that and its writes land on
  the transform. With only a scene handle, or none, the exposed transform follows the node.
- A handle whose path is a skeleton node's animates the node: one bound before the hierarchy
  was optimized goes on reading the node's stream values and its world position, and its
  write turns the node, which the exposed hand follows; the handle of an exposed top node
  (its path is the node's) turns the skeleton below it, and the exposed transforms follow.

Not recorded (an exploratory capture; docs/COMPATIBILITY_GAPS.md says how the engine differs):
the graph control on the optimized generic rig with its exposed hand bound, whose record
bakes the skin. The Editor logs `The input bones do not match the skeleton of the Avatar`
there and the skin's vertices differ between two captures; the job controls on that rig,
which do not bake the skin, log nothing.

Unmeasured, Unbound's choices: a destroyed ancestor that is neither animated nor a node of the
avatar's skeleton is read with the values it last held; a layer or an output of weight 0 over a
channel nothing set reads as set; a channel the output keeps bound reads as set for a job through
`AnimationStreamSource.PreviousInputs`.
