# Sprite atlas secondary textures

With the Sprite Packer mode "Sprite Atlas V2 - Enabled" (this probe's
`ProjectSettings/EditorSettings.asset`; the rest of the project is the `urp2d` probe's URP 2D
pipeline and settings) the Editor packs a sprite atlas's sprites for Play mode together with their
secondary textures: each page gets one secondary page per shader property its sprites bind
(`<page name>?_NormalMap`), laid out as the page, of the page's size and the atlas's compression
(8-bit RGBA uncompressed, DXT5 compressed), readable as the atlas is, in the colour space of the
textures it holds (a linear mask makes a linear page, an sRGB texture under `_NormalMap` an sRGB
one), holding each secondary texture as it was imported (a normal map's (1, y, y, x) texels); a
sprite's secondary textures become those pages. A sprite without secondary textures is packed
before those with them.

Three hand-authored atlases (`Assets/Atlases`, sprites in `Assets/Art`): `Packed` (uncompressed)
with A (16 x 16, white, `_NormalMap` and `_MaskTex`), B (8 x 8, `_NormalMap`) and C (12 x 12,
none); `Compressed` (compressed) with D (as A); `Swapped` with F, whose `_MaskTex` is linear and
whose `_NormalMap` is an ordinary sRGB texture. `Assets/Editor/SpriteAtlasSecondaryProbe.cs`
records in Play mode each sprite's texture (the page name without its import hash), size,
format, colour space, texture rect and secondary textures (name, texture, size, format, colour
space, readability, and for uncompressed pages the texels at the texture rect's centre and
corners), then the colours A, B and C get, drawn by a SpriteRenderer, under a dim global light
and a point light that uses normal maps, and A under a light of the masked blend style 3 (0-255;
values compare within 1, by which the Editor on different GPUs rounds them apart).

The Editor's order between sprites of different sets of secondary textures is not a function of
the sets (in scratch captures, swapping the sets between sprites reordered them inconsistently);
Unbound orders such sets by a hash of their properties and textures, so A and B trade places in
the page: `residuals.json` lists their texture rects. Not measured: textures of both colour
spaces under one property in one page, per-property secondary settings, mipmapped secondary pages (filtered as plain texels), alpha dilation (which
leaves the border fill's alpha, a normal map's x, empty), and the order `GetSprites` lists the
sprites of different sets in.

```sh
python3 tests/projects/spriteatlassecondary/run.py record --label A     # twice, A and B
python3 tests/projects/spriteatlassecondary/run.py publish A B
python3 tests/projects/spriteatlassecondary/run.py gate
```
