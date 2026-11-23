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
- `human`: the humanoid `HumanA` below its holder, playing muscle curves (its avatar root and two
  bones);
- `direct`: the model with its own Animator (a control).

The avatar root is the first transform, in depth-first order from the Animator's own, below which
the most of the avatar's skeleton paths exist; the clips bind below it.

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
