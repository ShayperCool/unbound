# Sprite atlas extensions in the Editor

The Editor's view of sprite atlases through `UnityEditor.U2D.SpriteAtlasExtensions`, which
packages such as Addressables read: for each sprite atlas of the project (in asset path order,
found with `AssetDatabase.FindAssets("t:SpriteAtlas")`) its packables (type, asset path and name of
each, in order), its master atlas, whether builds include it, and its packing and texture settings,
beside its variant flag and sprite count; also the packing and texture settings of `new SpriteAtlas()`
and the answers for a null atlas. Recorded in Play mode, in batch mode, with the
Unity 6000.0.71f1 Editor.

Four profiles, each with its own reference under `Reference/unity-6000.0.71f1/`:

- `v1`: Sprite Packer mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets in
  `Assets/Atlases/`: `Main` (a folder, a texture and a sprite of a sheet as packables, included in
  builds), `Variant` (a variant of `Main` at scale 0.5, not included) and `Missing` (a packable
  whose texture does not exist beside a sprite).
- `v2`: "Sprite Atlas V2 - Enabled" with two `.spriteatlasv2` assets in `Assets/Atlases/` whose
  importer settings exclude (`Excluded`: a folder and a texture) or include (`Included`: two
  sprites) them.
- `v1settings`: mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets in
  `Assets/SettingsAtlases/`: `Defaults` (omitted `packingSettings` and `textureSettings`), `Custom`
  (non-default packing and texture fields) and `CustomVariant` (a variant of `Custom` with distinct
  packing and texture fields).
- `v2settings`: mode "Sprite Atlas V2 - Enabled" with the corresponding three `.spriteatlasv2`
  assets in `Assets/SettingsAtlases/` and their `SpriteAtlasImporter` `.meta` files.

The sprites come from tests/projects/spriteatlas (files the Editor authored). The atlases are
written from that probe's Editor-authored atlas files with other packables and settings. The
scene and the Play-mode runner are tests/projects/imguiskin's, the packages and project settings
tests/projects/analyticssession's; `run.py` selects the packer mode, removes the other atlas folder
and removes the other kind of atlas.

Measured: packables keep their stored order; a folder answers its DefaultAsset, a texture packable
its Texture2D, a sprite packable its Sprite; a packable whose asset is gone is left out (not
null); a variant has no packables and answers its master; "Include in Build" is the V1 atlas's
`bindAsDefault` and the V2 importer's; `GetPackingSettings` and `GetTextureSettings` return
Editor defaults when omitted or on `new SpriteAtlas()`, serialized values verbatim (including
`maxTextureSize: 0` and `anisoLevel: 0`), a V1 variant's own settings, and a V2 variant's master's
packing settings alongside its own texture settings; a null atlas throws ArgumentNullException for
all five. Not measured: either kind of atlas in the other kind's packer mode.

Values compare exactly.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteatlaseditor/run.py record --label a
python3 tests/projects/spriteatlaseditor/run.py record --label b
python3 tests/projects/spriteatlaseditor/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlaseditor/run.py gate
```
