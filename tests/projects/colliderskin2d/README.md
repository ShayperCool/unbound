# Collider skin lifecycle controls

`Reference/unity-<version>.json` is the single canonical Native oracle. The current
release has 46 cases and 644 observations, originally confirmed by two independent
Editor processes. It tests geometry authored at contact offset0.01 followed by a
global change and a geometry, pose, activation, material or clone transition.

The cast response supports retaining the earlier box skin for unchanged geometry,
sync, material and density assignments, and refreshing it for the measured changed
geometry/registration transitions. This is an inference from public casts and
bounds, not a read of Unity's internal skin. Other collider types and additional
transitions remain unmeasured. The current engine matches this suite.

```bash
python3 tests/projects/colliderskin2d/run.py prepare --label native1
python3 tests/projects/colliderskin2d/run.py native --label native1
python3 tests/projects/colliderskin2d/run.py prepare --label native2
python3 tests/projects/colliderskin2d/run.py native --label native2
python3 tests/projects/colliderskin2d/run.py record-reference --label native1 --repeat-label native2
python3 tests/projects/colliderskin2d/verify.py --actual /path/to/cache/engine.json
```

UNBOUND_PROBE_UNITY_VERSION selects the release; UNBOUND_PROBE_UNITY can select
its Editor. SKIN_WORK defaults to the XDG unbound validation cache. Every label
uses a separate private Unity project and append-only capture/log. Recording the
reference requires agreeing repeats. Default verify checks reference structure
only. Actual engine comparisons retain all missing/extra/failing keys and write
the original comparison to the cache. No generated output belongs in Git.
