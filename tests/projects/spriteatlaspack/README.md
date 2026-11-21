# spriteatlaspack

Measures `UnityEditor.U2D.SpriteAtlasUtility` (`PackAtlases`, `PackAllAtlases`,
`CleanupAtlasPacking`, and default construction) in the Unity `6000.0.71f1` Editor across four
Sprite Packer modes:

- `v1`: `SpritePackerMode.AlwaysOnAtlas` (4) with `.spriteatlas` assets
- `v1build`: `SpritePackerMode.BuildTimeOnlyAtlas` (3) with `.spriteatlas` assets
- `v2`: `SpritePackerMode.SpriteAtlasV2` (5) with `.spriteatlasv2` assets
- `disabled`: `SpritePackerMode.Disabled` (0) with `.spriteatlas` assets

Beside the API answers each profile records a sprite of a packed atlas asked for again, its texture imported
again, after the atlases were packed (`afterPack.lateLoadedSprite`) and the whole `.spriteatlas` the pack wrote after an in-memory
`Add` (`pack.afterInMemoryAdd.written`).

```
python3 tests/projects/spriteatlaspack/run.py record --label a
python3 tests/projects/spriteatlaspack/run.py record --label b
python3 tests/projects/spriteatlaspack/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlaspack/run.py gate
```
