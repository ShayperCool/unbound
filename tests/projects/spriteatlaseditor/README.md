# Sprite atlas extensions in the Editor

The Editor's view of sprite atlases through `UnityEditor.U2D.SpriteAtlasExtensions`, which
packages such as Addressables read: for each sprite atlas of the project (in asset path order,
found with `AssetDatabase.FindAssets("t:SpriteAtlas")`) its packables (type, asset path and name of
each, in order), its master atlas and whether builds include it, beside its variant flag and sprite
count; and the three answers for a null atlas. Recorded in Play mode, in batch mode, with the
Unity 6000.0.71f1 Editor.

Two profiles, each with its own reference under `Reference/unity-6000.0.71f1/`:

- `v1`: Sprite Packer mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets:
  `Main` (a folder, a texture and a sprite of a sheet as packables, included in builds),
  `Variant` (a variant of `Main` at scale 0.5, not included) and `Missing` (a packable whose
  texture does not exist beside a sprite).
- `v2`: "Sprite Atlas V2 - Enabled" with two `.spriteatlasv2` assets whose importer settings
  exclude (`Excluded`: a folder and a texture) or include (`Included`: two sprites) them.

The sprites come from tests/projects/spriteatlas (files the Editor authored). The atlases are
written from that probe's Editor-authored atlas files with other packables and settings. The
scene and the Play-mode runner are tests/projects/imguiskin's, the packages and project settings
tests/projects/analyticssession's; `run.py` selects the packer mode and removes the other kind of
atlas.

Measured: packables keep their stored order; a folder answers its DefaultAsset, a texture packable
its Texture2D, a sprite packable its Sprite; a packable whose asset is gone is left out (not
null); a variant has no packables and answers its master; "Include in Build" is the V1 atlas's
`bindAsDefault` and the V2 importer's; a null atlas throws ArgumentNullException for all three.
Not measured: V2 variants, either kind of atlas in the other kind's packer mode.

Values compare exactly.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteatlaseditor/run.py record --label a
python3 tests/projects/spriteatlaseditor/run.py record --label b
python3 tests/projects/spriteatlaseditor/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlaseditor/run.py gate
```
