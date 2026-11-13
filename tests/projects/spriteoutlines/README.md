# Sprite outlines traced at runtime

Unity 6000.0.71f1 authors twelve sprite textures: an ellipse, a triangle and a ring (a hole),
each readable (`R`), unreadable (`N`), without a generated physics shape (`NoPhys`) and Full
Rect without one (`NoPhysFull`); a Polygon-mode triangle (`PolyShape`) and a sheet of two sprites
with off-centre pivots at 64 pixels per unit, one at fractional coordinates (`SheetGrid`). In Play
mode the probe records:

- `Sprite.Create` with `SpriteMeshType.Tight` on the readable and unreadable textures, with and
  without `generateFallbackPhysicsShape`, extrude 0, 1 and 4; an off-centre sub-rect at 64 pixels
  per unit, a rect under 32 pixels on a side and a Full Rect control: vertices, UVs, triangles,
  texture rect and physics shapes.
- A PolygonCollider2D added to each imported sprite's object (its paths), and a TilemapCollider2D
  with a sprite tile of each convex shape (its shape types and vertices).
- `UnityEditor.Sprites.SpriteUtility.GenerateOutline` (internal, called through reflection) on the
  readable textures with four detail, alpha tolerance and hole-detection combinations.
- Textures written by the probe, traced by `Sprite.Create` (Tight, fallback physics shape) and
  `GenerateOutline` over the whole texture and an off-centre fractional sub-rect: a new readable
  texture after `Apply`, after `SetPixels32` without `Apply`, after `Apply(false, true)` made it
  non-readable, and an imported readable texture rewritten by `SetPixels32` before and after
  `Apply`. Each traces the texels the CPU holds at that moment.
- `Sprite.OverridePhysicsShape` on a Full Rect sprite with an off-centre pivot: two paths (one
  with points outside the rect), a path of two points and a list with a one-point path (each
  throws `ArgumentException` and keeps the shape), a list with a null path and a null list
  (`ArgumentNullException`), an empty list (clears it); the physics shape and the paths of a
  PolygonCollider2D added after each. `GenerateOutline` of no texture (`ArgumentNullException`).
  A null argument's exception is recorded by type and parameter name only (its message is the
  runtime's wording).
- `UnityEditor.Sprites.SpriteUtility.GenerateOutlineFromSprite` (internal, through reflection) on
  every imported sprite, the second sheet sprite, an off-centre runtime sprite, a Full Rect one and
  each sprite made from a script-written texture, with the importers' arguments (detail 0.25,
  tolerance 200, holes) and four other detail, tolerance and hole combinations; no sprite
  (`ArgumentNullException`).
- Box, Circle, Capsule, Polygon and Edge colliders each added to a new object: sprites without
  and with each flip, Sliced and Tiled drawing (also flipped, also with an off-centre pivot), a
  rotated and scaled object, set local bounds, a disabled renderer, a sprite renderer without a
  sprite, no renderer, a rotated and scaled mesh renderer, a sprite without traceable texels and
  a renderer added after the colliders: their sizes, offsets, radii, paths and points, with the
  renderer's local and world bounds.
- `PolygonCollider2D.CreatePrimitive` with 5, 3, 6 and 40 sides, scales and offsets; what the Box
  and Capsule size and Circle radius setters keep of 0, negative, tiny and huge values.

References from Unity 6000.0.71f1 are recorded with the macOS Editor (arm64, Metal): two
independent captures are identical, and they equal the earlier Linux Editor reference in every
value it had (5,332 values). The values are sprite geometry and collider shapes, which the
platform does not decide.

The comparator compares every value exactly (floats within 1e-5 relative). The gate assembles
the committed `Authored/` set and runs only the engine; outputs stay under
`$XDG_CACHE_HOME/unbound/validation/runs/spriteoutlines/runs/`.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteoutlines/run.py author --label author
python3 tests/projects/spriteoutlines/run.py native --label native-a
python3 tests/projects/spriteoutlines/run.py native --label native-b
python3 tests/projects/spriteoutlines/run.py publish native-a native-b
UNBOUND=/path/to/this/checkouts/unbound python3 tests/projects/spriteoutlines/run.py check
```
