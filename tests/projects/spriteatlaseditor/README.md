# Sprite atlas extensions in the Editor

The Editor's view of sprite atlases through `UnityEditor.U2D.SpriteAtlasExtensions`, which
packages such as Addressables read: for each sprite atlas of the project (in asset path order,
found with `AssetDatabase.FindAssets("t:SpriteAtlas")`) its packables (type, asset path and name of
each, in order), its master atlas, whether builds include it, its packing and texture settings,
and (in the platform profiles) its per-platform `TextureImporterPlatformSettings` across target
aliases, defaults, variants, legacy serialized versions and mutation isolation, beside its variant
flag and sprite count; also the settings of `new SpriteAtlas()` and the answers for a null atlas.
Recorded in Play mode, in batch mode, with the Unity 6000.0.71f1 Editor.

Six profiles, each with its own reference under `Reference/unity-6000.0.71f1/`:

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
- `v1platform`: mode "Sprite Atlas V1 - Always Enabled" with five `.spriteatlas` assets in
  `Assets/PlatformAtlases/`: `Defaults` (empty `platformSettings`), `Custom` (non-default
  `DefaultTexturePlatform`, `Android`, `iPhone`, `Standalone`, and v4 `BC6H`/`BC7` entries),
  `CustomVariant` (a variant of `Custom` with distinct `DefaultTexturePlatform` and `Android`
  entries), `Legacy` (`serializedVersion: 1` format and BC7 migrations) and `LegacyV3`
  (`serializedVersion: 3` build-target group names and a desktop-specific entry).
- `v2platform`: mode "Sprite Atlas V2 - Enabled" with the corresponding five `.spriteatlasv2`
  assets in `Assets/PlatformAtlases/` and their `SpriteAtlasImporter` `.meta` files.

The sprites come from tests/projects/spriteatlas (files the Editor authored). The atlases are
written from that probe's Editor-authored atlas files with other packables and settings. The
scene and the Play-mode runner are tests/projects/imguiskin's, the packages and project settings
tests/projects/analyticssession's; `run.py` selects the packer mode, removes the other atlas folders
and removes the other kind of atlas.

Measured: packables keep their stored order; a folder answers its DefaultAsset, a texture packable
its Texture2D, a sprite packable its Sprite; a packable whose asset is gone is left out (not
null); a variant has no packables and answers its master; "Include in Build" is the V1 atlas's
`bindAsDefault` and the V2 importer's; `GetPackingSettings` and `GetTextureSettings` return
Editor defaults when omitted or on `new SpriteAtlas()`, serialized values verbatim (including
`maxTextureSize: 0` and `anisoLevel: 0`), a V1 variant's own settings, and a V2 variant's master's
packing settings alongside its own texture settings; `GetPlatformSettings` normalizes build target
names and aliases (desktop targets and `"Standalone"` to `"Standalone"`, `"iPhone"` to `"iOS"`,
`"Web"` to `"WebGL"`, `"WSAPlayer"` / `"Windows Store Apps"` / `"WSA"` to `"WindowsStoreApps"`,
`"GameCoreXboxSeries"` to `"GameCoreScarlett"`, `"LinuxHeadlessSimulation"` to `"CloudRendering"`,
while unknown names and `"DefaultTexturePlatform"` stay case-sensitive verbatim and `null` / `""`
become `""`), returns a fresh copy of a matching entry (with `compressionQuality: 100` for BC6H/BC7
when `forceMaximumCompressionQuality_BC6H_BC7` is set or `serializedVersion <= 2`, and legacy v1/v3
migrations applied), returns fresh Editor defaults named after the normalized target when no entry
matches (never inheriting stored `DefaultTexturePlatform`), keeps both V1 and V2 variants on their
own platform settings, and returns defaults on `new SpriteAtlas()`; a null atlas throws
ArgumentNullException for all six. Not measured: either kind of atlas in the other kind's packer
mode.

Values compare exactly.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteatlaseditor/run.py record --label a
python3 tests/projects/spriteatlaseditor/run.py record --label b
python3 tests/projects/spriteatlaseditor/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlaseditor/run.py gate
```
