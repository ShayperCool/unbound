# spriteatlasasset

Measures `UnityEditor.U2D.SpriteAtlasAsset` in the Unity `6000.0.71f1` Editor (Sprite Atlas V2
mode): construction defaults, `Load` and `Save` contracts (including exact `.spriteatlasv2` YAML
output, import/`.meta` timing, and overwriting an existing `.spriteatlasv2`), in-memory mutators
(`isVariant`, `SetIsVariant`, `SetMasterAtlas`, `GetMasterAtlas`, `Add`, `Remove`,
`SetScriptablePacker`), and all nine `[Obsolete]` methods.

```
python3 tests/projects/spriteatlasasset/run.py record --label a
python3 tests/projects/spriteatlasasset/run.py record --label b
python3 tests/projects/spriteatlasasset/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlasasset/run.py gate
```
