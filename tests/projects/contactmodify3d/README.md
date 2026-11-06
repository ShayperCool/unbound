# 3D contact modification probe

Measures `Physics.ContactModifyEvent`, `Physics.ContactModifyEventCCD`, `ModifiableContactPair`,
`ModifiableMassProperties` and `Collider.hasModifiableContacts`.
`Assets/Scripts/ContactModifyProbe.cs` builds every case at runtime, steps it with
`Physics.Simulate(0.02)` and writes one JSON line per record:

- `pair/<case>/...`: a dynamic box resting on a static ground, with the flag on the box, the ground,
  both or neither, a trigger ground, a kinematic box on a static ground, a kinematic pusher, a
  compound child sphere, a MeshCollider ground, and the flag changed on a live contact (with and
  without a collision message receiver, and with the body moved). Values: event calls and pairs per
  step, the pairs seen, the third step's pair data (ids, poses, velocities, mass properties, every
  contact's point, normal, separation, target velocity, max impulse, bounciness, frictions, face
  index), the body's pose.
- `sleep/...`: pairs per step until the body sleeps.
- `batch/...`: 300 flagged boxes: calls and pairs in one step.
- `edit/<kind>/...`, `ball/...`: a sliding, falling box (or a dropped ball) with one edit applied to
  every contact. Values: its trace, final pose and velocities, pair counts.
- `mass/<case>/...`: two frictionless spheres colliding with edited mass scales.
- `ccd/<case>/...`: a fast Continuous ball against a thin wall: both events, their data and edits.
- `order/events`: the event against the same step's collision messages.
- `unsubscribed`, `default`: the flag without a subscriber, the default flag.

The canonical Native oracle is `Reference/unity-6000.0.71f1.jsonl` (342 records, Unity 6000.0.71f1
on Linux, GPU Editor). Two independent captures were byte-identical. Only one copy is kept here.

Measured (6000.0.71f1):

- Pairs with a flagged collider on either side reach the event; triggers do not, and neither do
  kinematic/static pairs that the default contact pairs mode suppresses. A sleeping body's pairs stop.
- 300 pairs arrive in 3 calls (128, 128, 44). A swept CCD contact arrives once, through the CCD event
  only, with the normal inverted. Ignoring it lets the body through.
- The default mass properties are 1; a static body's instance ID is 0; face indices are mesh
  triangles for a MeshCollider and 4294967295 otherwise.
- The event runs within `Physics.Simulate`, before the step's `OnCollisionEnter`/`Stay`.
- Changing the flag on a live contact applies from the next step only when no collision message
  receiver is involved; with one, the pair keeps its state until it is re-created.

Not compared: the calling thread and the order of pairs in a buffer (Unity leaves both unspecified;
pairs are recorded sorted), and which collider is first in a pair (unspecified; the mass cases use
frictionless spheres, whose motion does not depend on it).

`compare.py` lists `KNOWN` differences separately; they are open gaps in
`docs/COMPATIBILITY_GAPS.md` and do not fail the comparison.

```bash
python3 tests/projects/contactmodify3d/run.py native --label native1
python3 tests/projects/contactmodify3d/run.py native --label native2
python3 tests/projects/contactmodify3d/run.py publish native1 native2   # refuses to overwrite
python3 tests/projects/contactmodify3d/run.py engine --label final      # UNBOUND_CLI selects the CLI
python3 tests/projects/contactmodify3d/run.py verify                    # or --candidate PATH
python3 tests/projects/contactmodify3d/run.py check                     # engine run + verify (gate)
```

Run output stays under `${XDG_CACHE_HOME:-~/.cache}/unbound/validation/runs/contactmodify3d/`.
`UNBOUND_PROBE_UNITY_VERSION` selects the release.
