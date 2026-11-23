# Animator.hasBoundPlayables through an Animator's life

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (the value does not depend on the
OS). The gate compares every value exactly.

`Assets/Scripts/BoundPlayablesProbe.cs` makes Animators in Play mode and records
`hasBoundPlayables`:

- `none`: no controller (false); `controlled`: a controller set on an active object (true at once,
  before any update); `updated`: after `Update(0)`; `disabled`: the Animator disabled (true);
  `inactive`: on an inactive object (false until the object is activated);
- `removed`: the controller taken away (true until the frame ends, then false), given again (true at
  once), then the object deactivated (still true, also a frame later and once reactivated);
  `controlled.taken*`: the same after frames;
- `output`: no controller, a playable graph's animation output on it (true), the graph destroyed
  (true until the frame ends, then false); `outputWithController`: the same with a controller (true).

`*.frames` are read two frames later. The Animator's controller has a playable graph of its own,
made when a valid controller is set on an active hierarchy or when the hierarchy becomes active,
and released at the end of the frame its controller is taken away.

Unmeasured: an invalid controller (an override without a base, or one whose base is taken away
later), Edit mode and pause (the release waits for the next Play mode frame), a controller changed
by reverting or applying a prefab override, and an Animator in a scene file.

The model's controller is the modelroot probe's (`run.py` takes it), the scene the uitkpanelutils
probe's. Other releases are unmeasured.

## Usage

```bash
python3 tests/projects/boundplayables/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/boundplayables/run.py publish run-a run-b
python3 tests/projects/boundplayables/run.py gate
```

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
