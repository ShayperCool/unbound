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

Five hand-authored atlases (`Assets/Atlases`, sprites in `Assets/Art`): `Packed` (uncompressed)
with A (16 x 16, white, `_NormalMap` and `_MaskTex`), B (8 x 8, `_NormalMap`) and C (12 x 12,
none); `Compressed` (compressed) with D (as A); `Swapped` with F, whose `_MaskTex` is linear and
whose `_NormalMap` is an ordinary sRGB texture; `Order` (uncompressed) with G (16 x 16,
`_NormalMap`) and H (8 x 8, `_NormalMap` and `_MaskTex`), where the smaller sprite's secondary-hash
key precedes the larger sprite's; and `SameSet` (uncompressed) with I (8 x 8, `_NormalMap`) and J
(16 x 16, `_NormalMap`), sharing the same secondary property set so the low 32-bit GUID hash
orders I before J despite I's smaller area. `Assets/Editor/SpriteAtlasSecondaryProbe.cs`
records in Play mode each sprite's texture (the page name without its import hash), size,
format, colour space, texture rect and secondary textures (name, texture, size, format, colour
space, readability, and for uncompressed pages the texels at the texture rect's centre and
corners), then the colours A, B, C, D and F get, drawn by a SpriteRenderer, under a dim global light
and a point light that uses normal maps, and A under a light of the masked blend style 3 (0-255;
values compare within 1, by which the Editor on different GPUs rounds them apart).

Sprites pack in ascending secondary-texture hash order (0 for sprites without secondary textures
first, so C packs first; H precedes G despite smaller area; A precedes B), then by area (largest
first), then by texture. Not measured (Unbound warns): textures of
both colour spaces under one property in one page, per-property secondary settings, mipmapped
secondary pages (filtered as plain texels), alpha dilation (which leaves the border fill's alpha,
a normal map's x, empty), and variant scaling of secondary pages (variants clear secondary page
bindings and leave sprites with their own); not measured: the order `GetSprites` lists the sprites
of different sets in (Unbound keeps its texture order).

```sh
python3 tests/projects/spriteatlassecondary/run.py record --label A     # twice, A and B
python3 tests/projects/spriteatlassecondary/run.py publish A B
python3 tests/projects/spriteatlassecondary/run.py gate
```
