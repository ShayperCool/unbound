# spriteatlaspack

Measures `UnityEditor.U2D.SpriteAtlasUtility` (`PackAtlases`, `PackAllAtlases`,
`CleanupAtlasPacking`, and default construction) in the Unity `6000.0.71f1` Editor across four
Sprite Packer modes:

- `v1`: `SpritePackerMode.AlwaysOnAtlas` (4) with `.spriteatlas` assets
- `v1build`: `SpritePackerMode.BuildTimeOnlyAtlas` (3) with `.spriteatlas` assets
- `v2`: `SpritePackerMode.SpriteAtlasV2` (5) with `.spriteatlasv2` assets
- `disabled`: `SpritePackerMode.Disabled` (0) with `.spriteatlas` assets

```
python3 tests/projects/spriteatlaspack/run.py record --label a
python3 tests/projects/spriteatlaspack/run.py record --label b
python3 tests/projects/spriteatlaspack/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlaspack/run.py gate
```
