# Zero dimension physics queries

Unity 6000.0.71f1 controls for `OverlapSphere`, `OverlapCapsule`, `OverlapBox`, their
NonAlloc forms, the three Check methods, and closest/All/NonAlloc shape casts.
The probe uses solid and trigger sphere, box and capsule colliders. Positions
include the center, nearby floats inside and outside the surface, exact contact,
and two further exterior offsets. Dimensions are zero, 1e-6 and 0.1. Capsule
queries include distinct and coincident endpoints; boxes include point, plane
and line dimensions. Counts and booleans are compared exactly.

The [6000.0 OverlapSphere page](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.OverlapSphere.html)
promises colliders touching or inside the sphere. The
[CheckSphere page](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.CheckSphere.html)
describes overlapping colliders. Neither documents a positive minimum query radius.
The [SphereCast limitations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.SphereCast.html)
say that zero radius has undefined output and that an initially overlapping
collider is not detected. The captured zero-radius sweep results are measured
release behavior, not a portable raycast guarantee. At exact contact the closest
cast and the All/NonAlloc counts distinguish initial-overlap handling.

The recorded nonnegative dimensions pass through unchanged. Exact contact counts
as overlap; a point just outside does not acquire a 1e-5 radius. The same inflation
also changed closest cast answers from just outside the target. This corrects a
query geometry approximation, without changing collider construction. No release
difference has been established. Other orientations, negative/nonfinite inputs,
mesh/terrain targets, and precise hit distance/normal output remain unmeasured here.
Existing physics probes and their tolerances are unchanged.

Record two normal GPU Editor captures on the existing display, then publish once:

```sh
python3 tests/projects/zeroqueries/run.py editor --release 6000.0.71f1 --label capture
python3 tests/projects/zeroqueries/run.py publish --release 6000.0.71f1 --label capture
```

The gate runs `run.py check --release $UNBOUND_PROBE_UNITY_VERSION` against the
committed reference using the immutable runtime supplied by the suite. All run
artifacts remain in the validation cache. The original particle boundary
reference and comparator remain unchanged.
