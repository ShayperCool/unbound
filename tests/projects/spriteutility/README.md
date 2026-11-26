# Editor sprite utilities

What `UnityEditor.Sprites.SpriteUtility.GeneratePolygonOutlineVerticesOfSize` (internal, called
by reflection as the 2D Sprite Editor's polygon tool reaches it), `GetSpriteTexture` and
`GetSpriteUVs` answer. Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two
identical captures): the values depend on neither the OS nor the graphics device; the gate's
tolerance (1e-4) leaves room for a platform's double-precision sine and cosine.

`Assets/Scripts/SpriteUtilityProbe.cs` records in Play mode into values.jsonl in
`$SPRITEUTILITY_OUT` (tests/value_probe.py):

* `outline.<sides>.<w>x<h>`: the corner count, and `.<i>.x` / `.<i>.y` each corner as a number,
  for 0, 1, 2, 3, 4, 5, 6, 8, 12, 33 and 128 sides in boxes of 64 x 64, 100 x 50, 7 x 13, 1 x 1,
  0 x 10, and 4 sides in a box of -8 x 16. A negative side count crashes the Editor (not recorded).
* `texture.<sprite>.<atlas>`, `uvs.<sprite>.<atlas>`, `uv.<sprite>`: of sprites made by script
  (the whole texture, a rectangle of it, a tight mesh), with and without the atlas data, and of
  null.

What it shows: the outline's first corner is at the top of the box, then each corner a step of one
sides-th turn further counter-clockwise, at (-sin, cos) of the angle scaled to the half sizes; the
angle grows by the step in single precision, its sine and cosine taken in double precision. No sides give the box's four corners from its bottom
left. A sprite's texture and coordinates are its own; asking for the atlas data of a sprite that
is not packed throws an ArgumentException ("Could not get atlas data - Sprite is not packed."), a
null sprite an ArgumentNullException. A packed sprite (its source or atlas data) is not measured
(Unbound reports it as not implemented).

Record and gate:

    python3 tests/projects/spriteutility/run.py record --label a   # twice, then
    python3 tests/projects/spriteutility/run.py publish a b
    python3 tests/projects/spriteutility/run.py gate
