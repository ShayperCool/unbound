# Animator.animatePhysics

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 46 rows. The gate compares every missing, extra or
unequal row exactly.

State rows are exact: two authored Animators (`m_UpdateMode` Fixed with `m_AnimatePhysics` off, and
Normal with it on) read and clone their own values; two Animators in the older format without the
field animate physics when their update mode is Fixed (the former AnimatePhysics mode) and not when
it is Normal; on a new Animator, setting animatePhysics leaves
updateMode alone and setting updateMode leaves animatePhysics alone. The property is not obsolete.

Physics rows are categories, because exact positions and velocities depend on the platform's physics
arithmetic and, in Normal mode, on frame timing. A kinematic platform (4 × 0.5 × 2 box) is animated
along x at 1 unit/s with a 1 m box resting on it; after 100 fixed steps the probe records whether the
platform travelled more than 1 unit, whether its velocity exceeded 0.5 units/s in one of the last ten steps
(in Normal mode a step without an Update before it has no new pose), and whether the box travelled
more than half as far as the platform:

- animatePhysics on, in Normal and in Fixed mode: the body has velocity and carries the box;
- off: it travels by teleports without velocity and the box stays behind;
- a body below an animated root with animatePhysics moves like an animated one;
- a body a script moves while its Animator (with animatePhysics) animates only another object
  teleports, and a disabled Animator moves nothing.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [Animator.animatePhysics](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-animatePhysics.html) | `on.*` against `off.*`: the platform's velocity and the carried box. |
| [Animator.updateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-updateMode.html) | `setOn.updateMode`, `setOff.updateMode`, `fixed.animatePhysics` and the authored rows. |

Not measured: root motion applied to kinematic bodies, non-kinematic animated bodies, Rigidbody2D
under such an Animator, the legacy Animation component's animatePhysics (which stays a Fixed-update
switch here and teleports), script writes from the Animator's own callbacks, and other releases.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/animatephysics/run.py native --label reference-a
python3 tests/projects/animatephysics/run.py native --label reference-b
python3 tests/projects/animatephysics/run.py publish reference-a reference-b
python3 tests/projects/animatephysics/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
