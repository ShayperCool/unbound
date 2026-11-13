# Sprite atlases packed for Play mode

With the Sprite Packer mode "Sprite Atlas V1 - Always Enabled" or "Sprite Atlas V2 - Enabled"
the Unity Editor packs sprite atlases before it enters Play mode, and the atlases' sprites draw
and report from the packed pages there. Unity 6000.0.71f1 authors two projects (sets):

- `v1`: Sprite Atlas V1 assets (`.spriteatlas`), mode 4. Seven folders of nine generated shapes
  (Tight and Full Rect meshes; `Inset` has an off-centre pivot, 64 pixels per unit and a
  transparent margin, so its Tight texture rect is smaller than its rect), one atlas each:
  `Tight` (padding 2, rotation and tight packing), `Paged` (padding 4, 128 x 128 pages: two
  pages), `FullRect` (Full Rect sprites in a tight atlas), `Sources` (block-compressed source
  textures), `Dilated` (padding 8, alpha dilation, mipmaps, a compressed page), `Blocks`
  (block offset 2) and `Late` (not included in builds: late binding). Also `Sheets` (two sprite
  sheets of equal 32 x 32 cells), `Folder` (a folder packable, point filtering), `Picked`
  (sprites as packables, one 9-sliced) and `PagedHalf` (a variant of `Paged` at scale 0.5);
  `Unpicked` and `Loose` (a trimmed Tight sprite) are in no atlas.
- `v2`: Sprite Atlas V2 assets (`.spriteatlasv2` with SpriteAtlasImporter settings), mode 5:
  `Tight`, `Paged` and `Late`.

In Play mode the probe records every sprite (texture, texture rect, offsets, packed, packing
mode and rotation, rect, pivot, bounds, vertices, UVs, triangles, `spriteAtlasTextureScale`,
`Sprites.DataUtility` outer and inner UVs and padding), each atlas (tag, variant, sprite count,
`GetSprites` clones in order, `GetSprite`, `CanBindTo`), every page texture (size, format,
mipmaps, sampler, readability and all texels), the late-binding sprites before and after
`SpriteAtlasManager.atlasRequested` registers their atlas (and the events with their frames),
and a 160 x 96 render of six sprite renderers.

The comparator (`compare.py`) compares every value exactly (the render within 2 per channel).
Page textures pair up by name; their names end with a hash of the Editor's import state,
which Unbound does not reproduce (it uses its own), so names equal up to that hash are listed
residuals. The gate assembles the committed `Authored/` sets and runs only the engine. Outputs
stay under `$XDG_CACHE_HOME/unbound/validation/runs/spriteatlas/runs/`.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteatlas/run.py author --set v1 --label author
python3 tests/projects/spriteatlas/run.py native --set v1 --label native-a
python3 tests/projects/spriteatlas/run.py native --set v1 --label native-b
python3 tests/projects/spriteatlas/run.py publish --set v1 native-a native-b
UNBOUND=/path/to/this/checkouts/unbound python3 tests/projects/spriteatlas/run.py check
```

(The same with `--set v2`.) A sprite larger than its atlas's page cancels the packing of every
atlas in the Editor ("Packing Error: Sprite Atlas (X) contain sprite(s) larger than its max
texture size. It needs at least N."): that case needs a project of its own and is covered by
unit tests, not by this probe.
