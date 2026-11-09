# Animation edge cases probe

Unity 6000.0.71f1. `ProbeRunner.Create` authors the clips and controllers; `AnimEdgesProbe`
records, in Play mode:

* `steps.*`: the Steps clip, whose paths' `m_LocalPosition.x` each have a segment with an infinite
  tangent at one end: `A` the first key's out-tangent +∞, `B` the second key's in-tangent −∞, `C`
  the first key's out-tangent −∞, `D` the second key's in-tangent +∞, `E` a middle key with an
  in-tangent −∞, `L` a linear control. Sampled at 0, 0.25, 0.49, 0.51, 0.99 and 1 s by an Animator
  playing it through a controller (`Animator.Update`), by a manually evaluated
  AnimationClipPlayable, and as the curves themselves (`AnimationCurve.Evaluate`).
* `play.*`: `AnimatorControllerPlayable.Play("B")` on a two-state controller (A holds `A.x` between
  1 and 2, B between 10 and 11) issued before its graph's first evaluation: a manual graph (with and
  without a normalized time, before connecting the output), after a first evaluation, a game-time
  graph played by the player loop (state and whether `A.x` is in B's range, its frame intervals
  vary), and `Animator.Play` before the Animator's first update as a control.

```
python3 tests/projects/animedges/run.py create  --release 6000.0.71f1
python3 tests/projects/animedges/run.py editor  --release 6000.0.71f1 --label <label>
python3 tests/projects/animedges/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/animedges/run.py unbound --release 6000.0.71f1
```

The engine run is compared with `tests/projects/modelroot/compare.py` (every missing, extra and
differing value; numbers within 2e-4).

What the Editor does (measured):

- A segment with a +∞ tangent at either end holds the first key's value; otherwise one with a −∞
  tangent holds the second key's value, from the segment's first key on (`B`, `C` read 3 at 0 s,
  `E` reads 2 from 0 s). The Animator, the clip playable and the curve agree.
- `AnimatorControllerPlayable.Play` issued before the graph's first evaluation takes effect in that
  evaluation, in every variant, as `Animator.Play` before the first update does.
