# Additive and override layer probe

Generic-rig Animator layers on Unity 6000.0.71f1: 100 cases of eleven fixed `Animator.Update`
steps (0, then ten of 0.125 s), 1100 records of the local position, rotation and scale of an
animated hierarchy, two script floats and the layer weights. Humanoid rigs are not covered.

The cases cover additive reference poses (none, another clip at a time, the clip itself at a time,
references without some curves, a clip start time, non-looping clips), quaternion and Euler
(`localEulerAnglesRaw`) rotation composition, position and scale differences, layer weights from
-2 to 3 and changed between updates, default weights, a base layer without states or additive,
transform masks (no root, sparse) on additive and override layers, a six-layer stack, override
layers above additive ones, a synced additive layer with override motions across a transition,
blend trees in additive layers, and transitions to and from motion and empty states with and
without write defaults in additive and override layers, including empty first states.
The `mix*` cases transition between a state with write defaults and one without, both ways, in
the base, an override and an additive layer, to motion and empty states (empty states also with
the same write defaults on both sides), at layer weights 1 and 0.5; `.poke` cases set channels from
a script during the transition, `.hold` cases stay in the base layer's empty state.

The assets are committed (`ProbeRunner.Create` made them in a copy of the project). Record two
independent Editor captures (GPU, private Xvfb), publish after they agree, run Unbound, compare:

```bash
python3 tests/projects/animatoradditive/run.py editor --release <version> --label <label>
python3 tests/projects/animatoradditive/run.py publish --release <version> --label <label>
UNBOUND_CLI=<unbound> python3 tests/projects/animatoradditive/run.py unbound --release <version> --label <run>
python3 tests/projects/animatoradditive/compare.py \
  tests/projects/animatoradditive/Reference/unity-6000.0.71f1.jsonl \
  ~/.cache/unbound/validation/runs/animatoradditive/<run>/unbound/values.jsonl
```

Captures go to `$XDG_CACHE_HOME/unbound/validation/runs/animatoradditive/<label>`. The comparison
lists every missing, extra and differing value; numbers match within 2e-4, a quaternion also
matches its negation.

Select the Editor release explicitly and use fresh labels. The runner writes the selected
release into its disposable project and preserves existing projects/captures. Only the
release documented above has been measured.
