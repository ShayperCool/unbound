# 2D effectors probe

Unity 6000.0.71f1, Linux. All five Effector2D components are tested through defaults, API
limits, singleton restrictions, serialized prefabs, cloning, masks, lifecycle and motion.

```bash
EFFECTORS_PROBE_OUT=/tmp/effectors-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/effectors" -executeMethod ProbeRunner.Record \
  -logFile /tmp/effectors-editor.log
EFFECTORS_PROBE_OUT=/tmp/effectors-unbound unbound run \
  -p tests/projects/effectors --headless 30000 --no-watch
python3 tests/projects/effectors/compare.py tests/projects/effectors/Reference /tmp/effectors-unbound
python3 -m unittest discover -s tests/projects/effectors -p 'test_*.py'
```

ProbeRunner.Create recreates the empty scene and prefabs through the Editor.

Platform/Surface cases include one-way passage/grouping, surface/side arcs, friction/bounce,
reflected transforms, compound contacts, trigger colliders, masks overriding the layer matrix,
and conveyor force/contact-force settings. Material tests check assignment snapshots,
mutation/reassignment and inactive-body fallback. Surface variation uses a private seed-zero
xorshift128 generator, drawing once per solved contact even with zero variation. Enable changes
preserve its state; clones restart it. Global Random is unchanged. The probe records 64 samples,
lifecycle arrays and positive/negative variation on rotated, reflected and curved surfaces.

Volume cases record queued force/torque as well as pose, velocity, inertia, mass center and
callbacks. Area/Point forces and damping run after position integration. Compound bodies receive
one force: Area uses the combined bounds for Collider selection; Point selects an overlapping
collider's bounds. Point falloff covers constant, inverse-linear and inverse-square modes.
Private variation is tested across disable/enable and zero-variation intervals.

Buoyancy covers partial/full circles, boxes, triangles with overridden mass centers, edges,
horizontal/rotated/degenerate capsules, scaled fluid levels and negative Y scale. Partial
capsules clip their oriented rectangle; fully submerged capsules use their true area. Degenerate
collider capsules retain a minimum 0.0049 endpoint separation. Fluid levels use signed Y scale.
Buoyancy follows gravityScale; flow and linear drag join that force before its magnitude is
compared with linearSleepTolerance. Angular drag torque is tested separately against
angularSleepTolerance converted to radians. Low-force cases vary mass, drag, immersion and both
sleep tolerances to distinguish those cutoffs from velocity cutoffs. Trigger shapes are excluded
from body mass and buoyancy area.

Native contact allocation can change initially overlapping platform groups or which collider a
compound Point contact selects. Creating unrelated objects or adding preceding getter checks
changed those results in repeated Editor runs. Complete measured alternatives are retained in
Reference/initial-overlap-variants.json. Normal permutations are accepted only when all other
values match. Positions, forces, contact counts and callbacks retain the original 2e-4 tolerance.
Eight comparator tests enforce these restrictions.

The 2559-record reference has 2531 direct matches and 28 measured native contact-order
records, with zero unexplained differences. Broad physics retains 8 existing differences;
Joint2D remains 1382/1401 plus 19 known rounding records, and ConstantForce remains 608/608.
All 8 comparator tests pass. API signatures match 2258/2258 hand-written types.
