# Sprite outlines traced at runtime

Unity 6000.0.71f1 authors twelve sprite textures: an ellipse, a triangle and a ring (a hole),
each readable (`R`), unreadable (`N`), without a generated physics shape (`NoPhys`) and Full
Rect without one (`NoPhysFull`). In Play mode the probe records:

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
  throws `ArgumentException` and keeps the shape), an empty list (clears it) and null
  (`ArgumentNullException`, its type and parameter only); the physics shape and the paths of a
  PolygonCollider2D added after each.

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
