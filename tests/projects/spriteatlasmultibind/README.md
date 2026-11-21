# spriteatlasmultibind

Measures Unity `6000.0.71f1` Editor runtime multi-atlas binding when one source `Sprite` appears in
multiple atlases across both V1 (`SpritePackerMode.AlwaysOnAtlas`, mode 4) and V2
(`SpritePackerMode.SpriteAtlasV2`, mode 5):

1. Two built-in (`Include in Build`) masters whose ASCII-GUID order opposes their `UnityGUID`
   word order (asset path order agrees with the word order, here and in case 4).
2. One built-in master vs one excluded (`Include in Build` off) master with a lower GUID.
3. One built-in master vs its built-in variant (with a lower GUID).
4. One excluded master with two built-in variants (`0.5` and `0.25` scales) whose ASCII-GUID order
   opposes their `UnityGUID` word order.
5. Late binding (`SpriteAtlasManager.atlasRequested`) for a sprite in two excluded masters,
   observing tag request order and sequential registration (`LateFirst` then `LateSecond`).
6. Late binding for a sprite in an excluded master + excluded variant, registering the variant.
7. Late binding for a sprite in two excluded masters where only the second tag is registered.

```
python3 tests/projects/spriteatlasmultibind/run.py record --label a
python3 tests/projects/spriteatlasmultibind/run.py record --label b
python3 tests/projects/spriteatlasmultibind/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlasmultibind/run.py gate
```
