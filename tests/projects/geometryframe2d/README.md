# Collider body frames

This probe records local and world shape data, composite paths and cast contacts
for static, body, child and rotated-child frames. It varies small displacements,
collider offsets, reflected/nonuniform scale, large positions and nonplanar
parents. The canonical Unity 6000.0.71f1 reference contains 357 scenarios and
47,376 observations from two byte-identical independent Editor runs.

Run from the repository root. Each label gets a fresh project in the external
cache; the runner wraps each Editor or engine process in `with-heavy-job`.

```sh
work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/geometry-frames"
python3 tests/projects/geometryframe2d/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native1
python3 tests/projects/geometryframe2d/run.py native --unity-version 6000.0.71f1 --work "$work" --label native1
python3 tests/projects/geometryframe2d/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native2
python3 tests/projects/geometryframe2d/run.py native --unity-version 6000.0.71f1 --work "$work" --label native2
```

`record-reference --label native1 --repeat-label native2` publishes an absent
release reference only after checking the release, authored inputs and complete
capture bytes. It refuses to replace an existing reference.

For an engine recording, prepare another label, select matching private managed
and native libraries through `UNBOUND_MANAGED_DIR` and `LD_LIBRARY_PATH`, then run
`engine --label <label> --cli <private-cli>` with the same release and work options.

```sh
python3 tests/projects/geometryframe2d/verify.py --unity-version 6000.0.71f1 \
  --actual "$work/final.json" --baseline "$work/before.json" \
  --report "$work/comparison.json"
```

Verification preserves the full union of keys and the existing query tolerance,
plus every exact difference. With `--baseline`, success requires zero introduced
failing keys; it still reports all retained failures. Without a baseline, any
failure returns a nonzero exit status. The probe includes unresolved behavior;
its reference and passing displacement tests do not establish geometry parity.
Run artifacts and rejected experiments stay in the cache. Other Unity releases
remain unmeasured.

The thirteen composite controls cover an interior hole, a disconnected island,
a repeated/zero-area source path, manual source edits, polygon edge radius and
Delaunay refinement, plus a tilted outline with manual setting changes and cloning. Six more controls cover
large body-local contours beside a valid island. The refinement discriminator produces four shapes with the
option disabled and two with it enabled. The two independent GPU Editor captures
agree byte for byte, SHA-256
`00215a10c77ae32fda1717c9b7c74c7d8a8ccfd71e523f6a335e0768a44fe5a4`.
All 320 original scenarios and the first thirteen added controls retain their
original values. A further 24 tilemap controls use the existing concave sprite
fixture from tests/projects/twod. They vary cell rotation/reflection, extrusion,
body attachment and refinement. Both independent GPU runs agree for all 357
scenarios. Refinement changes the per-polygon vertex counts from 4/5/6 to 4/4/7
for each extruded cell, even though both versions produce three polygons.

The [Unity 6000.0 component reference](https://docs.unity3d.com/6000.0/Documentation/Manual/2d-physics/collider/composite-collider/composite-collider-2d-reference.html)
and [polygon geometry API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CompositeCollider2D.GeometryType.Polygons.html)
describe convex shapes forming solid outlines, with holes excluded from collision.
The [manual generation API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CompositeCollider2D.GenerationType.Manual.html)
requires an explicit GenerateGeometry call after source or setting changes. The
added controls move the hole and change the geometry type and radius so that the
result differs before and after that call. In the measured outline control, a
clone applies pending settings to the copied paths. Switching to synchronous mode
keeps existing geometry until a source change triggers generation.
[useDelaunayMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CompositeCollider2D-useDelaunayMesh.html)
selects an additional refinement step. These pages do not specify polygon order.

For Unity 6000.0.71f1, the generated contours use the body's 2D frame, including
baked transform scale. Coordinates enter the integer contour grid after a
single-precision multiplication and truncation, and leave it through a
single-precision multiplication. Convex partitioning uses odd-even contour filling,
at most eight vertices per piece, optional refinement and near-collinear cleanup.
Contours containing nonfinite points or points beyond a body-local squared
distance of 1e12 are omitted individually; other islands remain collidable.
The controls distinguish valid centers at 800,000 and 999,000 units from a
1,000,000-unit center whose corners exceed that distance, and exercise both
signs beyond the tessellation input range.
The [edge-radius API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CompositeCollider2D-edgeRadius.html)
describes rounded edges; the polygon-mode reference reports zero shape radius
even when edgeRadius is 0.125. These numerical and ordering observations apply to
the measured release. Other releases retain the unmeasured contour-frame,
ear-partition, polygon-radius, immediate manual-setting and outline point-overlap
fallback, with a diagnostic.

Composite shape and path values now pass in all original frames and new controls.
The remaining 138 values are cast points/normals on capsule, circle and box
colliders in 11 displaced-frame cases. Their raw failures and tolerances remain
unchanged for the cast-contact follow-up.

The [Unity 6000.0 Tilemap Collider reference](https://docs.unity3d.com/6000.0/Documentation/Manual/tilemaps/work-with-tilemaps/tilemap-collider-2d-reference.html)
and [useDelaunayMesh API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Tilemaps.TilemapCollider2D-useDelaunayMesh.html)
describe refinement of generated tile geometry. The controls above distinguish
its output. The [processing API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Tilemaps.TilemapCollider2D.ProcessTilemapChanges.html)
processes pending tile changes explicitly; existing tests/projects/twod controls
record pending changes before and after that call and after a physics step.

For the measured release, cell contours are extruded and partitioned together in
the cell's local frame, preserving holes. The cell matrix, cell center and
collider offset are composed with the body matrix before transforming the convex
pieces. This resolves the original extruded-tile shape count and ordering, as
well as all 24 frame controls. Other releases retain the diagnosed transformed
contour partition without refinement. No sprite importer or cache format changes.
