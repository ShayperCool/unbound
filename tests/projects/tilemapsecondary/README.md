# Tilemap secondary textures under 2D lights

The 2D renderer (URP 17, the `urp2d` probe's pipeline and settings; this probe's own `Packages`
add the tilemap module) lights a Sprite-Lit sprite with its secondary textures: `_NormalMap` for
lights that use normal maps, `_MaskTex` for the masked blend styles. A tilemap lights each tile
as the tile's SpriteRenderer twin (the same sprite under the cell's transform) is lit.
`Assets/Editor/TilemapSecondaryProbe.cs` records in Play mode the colour a white square gets under
a dim global light and a point light with `NormalMapQuality.Accurate` above it, drawn by a
SpriteRenderer and by a Tilemap whose single tile shows the same sprite: without a normal map, and
with flat, -y-tilted and +x-tilted normal maps (`Assets/Normals`, imported as normal maps); the +x
one on a cell rotated 90 degrees and on a cell mirrored along x (and on a sprite turned and scaled
alike); and with a half-red mask texture
under a light of the masked blend style 3 (Additive with Mask, mask channel R). Each case is left
three frames, which render its camera into a render texture; the colour at five places is recorded
(0-255). Values compare within 1, by which the Editor on different GPUs rounds them apart.

```sh
python3 tests/projects/tilemapsecondary/run.py record --label A     # twice, A and B
python3 tests/projects/tilemapsecondary/run.py publish A B
python3 tests/projects/tilemapsecondary/run.py gate
```
