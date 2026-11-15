# Collider2D shape bounds and hash

Unity 6000.0.71f1 controls for `Collider2D.GetShapeBounds` and `Collider2D.GetShapeHash`
(`Sources/ProbeEntry.cs`, run in Play mode in a copy of `tests/projects/physics` with the
tilemap module). Each scenario records, for the four `useRadii`/`useWorldSpace` combinations,
every per-shape bounds and the combined bounds, the shape count and the hash (as hex text).

Scenarios: box, circle and capsule colliders on bodies and without one; a two-path polygon; a
two-point edge and an edge chain with an adjacent point; a composite in polygon and outline
mode (and one of its sources); disabled and inactive colliders with an output list that already
holds bounds; a list with more bounds than shapes; a null list; contact offset changes with and
without the shapes being created again, and edge radius edits after them; moves of a body, a
child collider and a collider without a body before and after `Physics2D.SyncTransforms`; hash
relations (the same box at another depth, on a moved body, as a polygon; circles and capsules of
equal and different geometry; resizing and restoring); a tilemap built, extended and reduced
tile by tile, and a fresh tilemap of the same cells; a tilemap edited in one and in several
passes, with a tile replaced by an identical sprite tile, by a different Tile asset with the same
sprite and collider type, removed and set again in one pass, moved with its Grid and re-enabled.

```bash
python3 tests/projects/shapes2d/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/shapes2d/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/shapes2d/run.py unbound --release 6000.0.71f1
```

`editor` runs two independent batch Editors on the GPU and compares them; `publish` writes
`Reference/unity-6000.0.71f1.json` when they are byte-identical (21 scenarios, 2697 values).
`unbound` compares with query2dparity's comparator (numbers within 1e-3 relative, everything
else exact). `RESIDUALS` in `run.py` names the known differences: each still prints, and one
that passes again fails the run as stale.

## Measured (6000.0.71f1)

* Bounds: each shape's bounds are the union of its children's, in the attached body's space or,
  with `useWorldSpace`, in world space (the scene body's space is the world). Circles and
  capsules always include their radius. With `useRadii`, polygons and edges add the radius
  beyond the current `Physics2D.defaultContactOffset`, or their whole radius when it is no larger
  than the offset. The z center is the collider transform's position z, the z extent 0. The
  list is cleared and filled; a collider without shapes leaves it as it was and returns its
  transform position with no extent. A null list is accepted. Pending transform changes are
  not synchronized first (only the z center follows the transform at once).
* Hash: each shape's hash is the 32-bit xxHash of its data seeded with the previous shape's
  hash, the first with `0x8f37154b` (also the value without shapes). A polygon hashes its radius
  (contact offset plus edge radius), then its vertices in body space in hull order; an edge
  chain its radius, then its points. Circle and capsule records include a value that differs
  between Editor processes: the two captures disagree on them, so these scenarios record
  relations only (equal geometry, equal hash; changed and restored geometry). Moving a body
  keeps the hash; moving a collider without a body changes it.
* A box's edge radius set to a new value takes the current contact offset, as a new size does;
  the same value keeps the earlier one.
* Tilemaps: processed tile changes apply in change order. A change that leaves the cell's
  sprite, collider type and transform as they were does nothing. Otherwise the cell's shapes are
  removed, each hashed into the running value (the cell's last shape first), and its new shapes
  are appended and hashed. A full rebuild (moving the Grid, re-enabling the collider) takes the
  cells in z, y, x order and hashes from the seed.

## Residuals

The polygon on a rotated body and both composites match their bounds within tolerance, but some
vertices differ from Unity's in the last bit (for example 8.94e-08 for 0), as in
tests/projects/geometryframe2d; their hashes therefore differ. Measured on Linux with the NVIDIA
driver; other releases are unmeasured.

Contracts: [Collider2D.GetShapeBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider2D.GetShapeBounds.html)
(per-shape bounds, combined bounds, radius and world-space options) and
[Collider2D.GetShapeHash](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider2D.GetShapeHash.html)
(a value for the created geometry; equal geometry gives equal hashes, a change can be detected).
The page's equality guarantee is controlled by the box/polygon/moved-body and circle relations;
the tilemap controls show that Unity's value also depends on the edit history.
