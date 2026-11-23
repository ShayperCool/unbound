# Generic and humanoid clips played by an Animator above its model

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode. The poses do not depend on the OS or
the graphics device. The gate compares every value exactly.

`Assets/Scripts/NestedAvatarProbe.cs` puts each model below a holder object, moves the model's
Animator's avatar to a new Animator on the holder (the model's own is removed), gives it a controller
playing one clip of the model, and records `Animator.avatarRoot` and the animated nodes' local poses
after `Animator.Update(0)` and `Update(0.25)`:

- `nested`: `WalkerNoNode` (a generic avatar without a root node) below its holder;
- `renamed`: the model renamed; `deep`: two levels below the holder;
- `noAvatar`: the holder's Animator without the avatar (a control: nothing animates and the avatar
  root is the holder);
- `rootNode`: `Walker` (root node Hips);
- `decoy`: a branch before the model holding a Hips only; `holderPartial`: the holder itself holds
  Hips/Leg; `holderNames`: the holder holds a Hips (without its Leg) and a Prop, as many top-level
  names as the model with fewer of its paths; `tie`: two copies of the model;
- `human`: the humanoid `HumanA` below its holder with a controller of muscle curves, and
  `humanDirect` the model with its own Animator (a control): updated this way neither moves from
  its bind pose (the avatar root and two bones are recorded);
- `direct`: the model with its own Animator (a control);
- `cached`: after a first update a second copy of the model is added before the first: the next
  update still animates the first (the avatar root is kept), and after `Rebind` the second (found
  again, the first back at its defaults);
- played by playable graphs evaluated by hand (`Evaluate(0)`, `Evaluate(0.25)`): `humanGraph`, the
  humanoid's muscle clip below its holder, takes the pose of `humanGraphDirect`, the clip on the
  model's own Animator; `masked`, the generic clip below its holder through a layer whose mask keeps
  `Hips` and leaves out `Hips/Leg` and `Prop`, moves the Hips only (mask paths start at the avatar
  root);
- `maskedLayer`: a controller made in the probe whose layer's mask keeps the root entry and `Hips` and
  leaves out `Prop`, playing a clip that moves `Hips`, `Prop` and the holder itself (an empty path),
  after `Update(0)` and `Update(0.5)`: Hips and the holder move (the holder's own transform takes the
  mask's root entry), Prop does not.

The avatar root is the first transform, in depth-first order from the Animator's own, below which
the most of the avatar's skeleton paths exist; the clips bind below it, layer masks name paths below
it, and it is kept until `Rebind`.

The models and controllers are the modelroot and humanoid probes' (`run.py` takes them; no copy in
Git), the scene the uitkpanelutils probe's.

Other releases are unmeasured.

## Usage

```bash
python3 tests/projects/nestedavatar/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/nestedavatar/run.py publish run-a run-b
python3 tests/projects/nestedavatar/run.py gate
```

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
