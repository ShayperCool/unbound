# Sprite atlas extensions in the Editor

The Editor's view of sprite atlases through `UnityEditor.U2D.SpriteAtlasExtensions`, which
packages such as Addressables read: for each sprite atlas of the project (in asset path order,
found with `AssetDatabase.FindAssets("t:SpriteAtlas")`) its packables (type, asset path and name of
each, in order), its master atlas, whether builds include it, its packing and texture settings,
(in the platform profiles) its per-platform `TextureImporterPlatformSettings` across target aliases,
defaults, variants, legacy serialized versions and mutation isolation, and (in the include profiles)
`SetIncludeInBuild` across V1 and V2 atlases, `new SpriteAtlas()`, null, same-value repeats,
toggles, `EditorUtility.IsDirty`/`GetDirtyCount`, `AssetDatabase.SaveAssets`/`SaveAssetIfDirty`,
`AssetDatabase.ImportAsset(ForceUpdate)` and on-disk `.spriteatlas`/`.meta` persistence, beside its
variant flag and sprite count; also the settings of `new SpriteAtlas()` and the answers for a null
atlas. Recorded in Play mode, in batch mode, with the Unity 6000.0.71f1 Editor.

Thirty-three profiles, each with its own reference under `Reference/unity-6000.0.71f1/`:

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
- `v1include`: mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets in
  `Assets/IncludeAtlases/`: `Included` (`bindAsDefault: 1`), `Excluded` (`bindAsDefault: 0`) and
  `Omitted` (absent `bindAsDefault`, empty packables).
- `v2include`: mode "Sprite Atlas V2 - Enabled" with the corresponding three `.spriteatlasv2`
  assets in `Assets/IncludeAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1packing`: mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets in
  `Assets/PackingAtlases/`: `Master` (default `packingSettings`), `Variant` (variant of `Master`
  with distinct `packingSettings`) and `Legacy` (`serializedVersion: 1` `paddingPower: 3` and
  `allowAlphaSplitting: 1`).
- `v2packing`: mode "Sprite Atlas V2 - Enabled" with the corresponding three `.spriteatlasv2`
  assets in `Assets/PackingAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1texture`: mode "Sprite Atlas V1 - Always Enabled" with four `.spriteatlas` assets in
  `Assets/TextureAtlases/`: `Master` (non-default `textureSettings`, `packingSettings` and
  `platformSettings`), `Variant` (variant of `Master` with distinct `textureSettings`), `Legacy`
  (`serializedVersion: 1` `colorSpace: 0` with non-default hidden fields and unrelated
  `packingSettings`/`platformSettings`) and `Omitted` (absent `textureSettings`, custom
  `packingSettings`).
- `v2texture`: mode "Sprite Atlas V2 - Enabled" with the corresponding four `.spriteatlasv2` assets
  in `Assets/TextureAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1platformset`: mode "Sprite Atlas V1 - Always Enabled" with four `.spriteatlas` assets in
  `Assets/PlatformSetAtlases/`: `Master` (non-default `Android` and `iOS` entries alongside custom
  `textureSettings` and `packingSettings`), `Variant` (variant of `Master` with its own `Android`
  entry), `Legacy` (`serializedVersion: 1` `Android`, `Windows Store Apps` and BC7 `PS5` entries)
  and `Omitted` (absent `platformSettings`, custom `textureSettings` and `packingSettings`).
- `v2platformset`: mode "Sprite Atlas V2 - Enabled" with the corresponding four `.spriteatlasv2`
  assets in `Assets/PlatformSetAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1add`: mode "Sprite Atlas V1 - Always Enabled" with four `.spriteatlas` assets in
  `Assets/AddAtlases/`: `Master` (one existing texture packable alongside custom `textureSettings`,
  `packingSettings` and `platformSettings`), `Empty` (empty `packables`), `Variant` (variant of
  `Master` with empty `packables`) and `Omitted` (absent `packables` alongside custom settings).
- `v2add`: mode "Sprite Atlas V2 - Enabled" with the corresponding four `.spriteatlasv2` assets in
  `Assets/AddAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1remove`: mode "Sprite Atlas V1 - Always Enabled" with four `.spriteatlas` assets in
  `Assets/RemoveAtlases/`: `Master` (folder, duplicate texture, sprite of the same texture, and
  sub-sprite alongside custom settings), `NullAndMissing` (null `{fileID: 0}` ref, missing asset
  ref, sprite, and folder), `Variant` (variant of `Master` with its own three packables) and
  `Omitted` (absent `packables` alongside custom settings).
- `v2remove`: mode "Sprite Atlas V2 - Enabled" with the corresponding four `.spriteatlasv2` assets
  in `Assets/RemoveAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1variantscale`: mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets in
  `Assets/VariantScaleAtlases/`: `Master` (non-variant control at `variantMultiplier: 1`), `Variant`
  (variant of `Master` at `variantMultiplier: 0.5`) and `Omitted` (variant of `Master` with
  `variantMultiplier` omitted).
- `v2variantscale`: mode "Sprite Atlas V2 - Enabled" with the corresponding three `.spriteatlasv2`
  assets in `Assets/VariantScaleAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1isvariant`: mode "Sprite Atlas V1 - Always Enabled" with three `.spriteatlas` assets in
  `Assets/IsVariantAtlases/`: `Master` (non-variant with packable and custom settings), `Variant`
  (variant of `Master` with its own folder packable, `m_Tag: Master` and `variantMultiplier: 0.5`)
  and `Omitted` (absent `m_IsVariant`).
- `v2isvariant`: mode "Sprite Atlas V2 - Enabled" with the corresponding three `.spriteatlasv2`
  assets in `Assets/IsVariantAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v1masteratlas`: mode "Sprite Atlas V1 - Always Enabled" with five `.spriteatlas` assets in
  `Assets/MasterAtlasAtlases/`: `MasterA` (non-variant with sprite packable and custom settings),
  `MasterB` (non-variant with folder packable and distinct settings), `Variant` (variant of
  `MasterA` with `variantMultiplier: 0.5`), `OtherVariant` (variant with null master at
  `variantMultiplier: 0.75`) and `Omitted` (variant with omitted `m_MasterAtlas`).
- `v2masteratlas`: mode "Sprite Atlas V2 - Enabled" with the corresponding five `.spriteatlasv2`
  assets in `Assets/MasterAtlasAtlases/` and their `SpriteAtlasImporter` `.meta` files.
- `v2importerplatform`: mode "Sprite Atlas V2 - Enabled" with six `.spriteatlasv2` assets in
  `Assets/ImporterPlatformAtlases/` (`Defaults`, `Omitted`, `Custom`, `CustomVariant`, `Legacy`,
  `LegacyV3`) and their `SpriteAtlasImporter` `.meta` files, exercising `AssetImporter.GetAtPath`
  and `SpriteAtlasImporter.GetPlatformSettings(string)` alongside non-atlas, missing-path,
  uncreated `new SpriteAtlasImporter()`, fresh-copy identity, mutation isolation, and in-memory
  `SpriteAtlasExtensions.SetPlatformSettings` independence controls.
- `v2importerplatformset`: mode "Sprite Atlas V2 - Enabled" with four `.spriteatlasv2` assets in
  `Assets/ImporterPlatformSetAtlases/` (`Master`, `Variant`, `Legacy`, `Omitted`) and their
  `SpriteAtlasImporter` `.meta` files, exercising `SpriteAtlasImporter.SetPlatformSettings`
  across null `src`, uncreated `new SpriteAtlasImporter()`, alias normalization in place,
  defensive copy isolation, same-value repeat dirtying, supported/default/absent/non-overridden
  rules, master/variant independence, legacy v1 sequence-version upgrade, omitted `platformSettings`,
  `AssetDatabase.SaveAssets`, `AssetDatabase.WriteImportSettingsIfDirty`,
  `SpriteAtlasImporter.SaveAndReimport`, `AssetDatabase.ImportAsset(ForceUpdate)`, and exact
  `.meta`/`.spriteatlasv2` persistence.
- `v2importerpacking`: mode "Sprite Atlas V2 - Enabled" with four `.spriteatlasv2` assets in
  `Assets/ImporterPackingAtlases/` (`Master`, `Variant`, `Legacy`, `Omitted`) and their
  `SpriteAtlasImporter` `.meta` files, exercising `SpriteAtlasImporter.packingSettings` (getter and
  setter) across uncreated `new SpriteAtlasImporter()`, V2 master/variant own-`.meta` vs imported
  master-inheritance behavior, immediate readback and loaded `SpriteAtlas` independence, same-value
  repeat dirtying, negative and zeroed `default` values, legacy `serializedVersion: 1`
  `paddingPower` and hidden `allowAlphaSplitting` preservation, omitted `packingSettings`/
  `platformSettings`, `SaveAssets`, `WriteImportSettingsIfDirty`, `SaveAndReimport` (including
  dependent V2 variant reimport when a master reimports), and `ImportAsset(ForceUpdate)`.
- `v2importertexture`: mode "Sprite Atlas V2 - Enabled" with four `.spriteatlasv2` assets in
  `Assets/ImporterTextureAtlases/` (`Master`, `Variant`, `Legacy`, `Omitted`) and their
  `SpriteAtlasImporter` `.meta` files, exercising `SpriteAtlasImporter.textureSettings` (getter and
  setter) across uncreated `new SpriteAtlasImporter()`, V2 master/variant own-`.meta` independence
  in both importer and loaded `SpriteAtlas`, immediate readback, hidden-field preservation on copy
  mutation, negative `anisoLevel`, out-of-range `filterMode` and zeroed `default` writes, legacy
  `serializedVersion: 1` `colorSpace` to `sRGB` upgrade, omitted `textureSettings`/
  `platformSettings`, `SaveAssets`, `WriteImportSettingsIfDirty`, `SaveAndReimport`, and
  `ImportAsset(ForceUpdate)`.
- `v2importervariantscale`: mode "Sprite Atlas V2 - Enabled" with three `.spriteatlasv2` assets in
  `Assets/ImporterVariantScaleAtlases/` (`Master`, `Variant`, `Omitted`) and their
  `SpriteAtlasImporter` `.meta` files, exercising `SpriteAtlasImporter.variantScale` (getter and
  setter) across uncreated `new SpriteAtlasImporter()`, non-variant/variant/omitted defaults,
  same-value and `-0f` no-op dirtying, unclamped out-of-range, `±Infinity` and `NaN` (`NaN != NaN`
  dirtying and unchanged-file `WriteImportSettingsIfDirty` returning `false`), in-memory
  `SpriteAtlasExtensions.SetVariantScale` independence, `SaveAssets`, `WriteImportSettingsIfDirty`,
  `SaveAndReimport`, and `ImportAsset(ForceUpdate)`.
- `v2importerinclude`: mode "Sprite Atlas V2 - Enabled" with four `.spriteatlasv2` assets in
  `Assets/ImporterIncludeAtlases/` (`Included`, `Excluded`, `Variant`, `Omitted`) and their
  `SpriteAtlasImporter` `.meta` files, exercising `SpriteAtlasImporter.includeInBuild` (getter and
  setter) across uncreated `new SpriteAtlasImporter()`, included/excluded/variant/omitted defaults,
  same-value no-op dirtying, toggle dirtying, loaded `SpriteAtlas` independence until reimport,
  `SaveAssets`, `WriteImportSettingsIfDirty`, `SaveAndReimport`, and `ImportAsset(ForceUpdate)`.

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
own platform settings, and returns defaults on `new SpriteAtlas()`; `SetIncludeInBuild` updates
`IsIncludeInBuild` immediately on V1, V2 and `new SpriteAtlas()` and marks the atlas dirty only
when the value changes (`new SpriteAtlas()` starts dirty and stays dirty across `SaveAssets`); on
V1 `.spriteatlas`, `SaveAssets` and `SaveAssetIfDirty` write `m_EditorData.bindAsDefault` into the
`.spriteatlas` (leaving `.meta` unchanged) and clear dirtiness, and `ImportAsset(ForceUpdate)`
keeps the loaded instance and its saved or unsaved in-memory state; on V2 `.spriteatlasv2`,
`SaveAssets` and `SaveAssetIfDirty` write neither `.spriteatlasv2` nor `.meta` and leave the
imported `SpriteAtlas` dirty, while `ImportAsset(ForceUpdate)` reloads a fresh clean `SpriteAtlas`
whose `IsIncludeInBuild` reverts to `.meta`; `SetPackingSettings` stores all fields verbatim
(including negative and zero values, preserving hidden `allowAlphaSplitting`) and unconditionally
marks the atlas dirty even when assigning identical values on V1, V2 and `new SpriteAtlas()`,
updates V1 and V2 variants independently of their master in memory, persists `m_EditorData.packingSettings`
(upgraded to `serializedVersion: 2`) on V1 `SaveAssets`/`SaveAssetIfDirty` while leaving V2 files
and `.meta` unchanged and dirty, and on V2 `ImportAsset(ForceUpdate)` reloads a fresh clean atlas (a V2 variant copies its live
master's packing settings); `SetTextureSettings` likewise stores all fields verbatim (including
negative `anisoLevel`, out-of-range `filterMode`, `default` zeroing of `maxTextureSize` and hidden
fields, or preserving `maxTextureSize` and hidden fields when mutating a `GetTextureSettings()`
copy) and unconditionally marks the atlas dirty even on identical values on V1, V2 and
`new SpriteAtlas()`, updates V1 and V2 variants independently of their master in memory, persists
`m_EditorData.textureSettings` (upgraded to `serializedVersion: 2` or inserted after
`serializedVersion` when omitted, preserving unrelated `packingSettings`/`platformSettings` and
`.meta`) on V1 `SaveAssets`/`SaveAssetIfDirty` while leaving V2 files and `.meta` unchanged and
dirty, and on V2 `ImportAsset(ForceUpdate)` reloads a fresh clean atlas whose texture settings
revert to its own `.meta` (including for variants); `SetPlatformSettings` normalizes `src.name` in
place via `TextureImporter.GetTexturePlatformSerializationName` first (so a null `src` throws
`NullReferenceException` even for a null atlas, and a non-null `src` has its name normalized before
a null atlas throws `ArgumentNullException`), stores a defensive copy replacing any matching entry
in place (even when `overridden` is false), appends an absent entry only when `overridden` is true
or `name == "DefaultTexturePlatform"`, unconditionally marks the atlas dirty even on identical
values or uninserted non-overridden entries, keeps V1 and V2 variants independent of their master,
persists `m_EditorData.platformSettings` at `serializedVersion: 4` (inserting after
`textureSettings` when omitted, preserving unrelated `textureSettings`/`packingSettings` and
`.meta`) on V1 `SaveAssets`/`SaveAssetIfDirty`, leaves V2 files and `.meta` unchanged and dirty,
and on V2 `ImportAsset(ForceUpdate)` reloads a fresh clean atlas reverted to its own `.meta`; `Add`
appends valid packable objects (`Texture2D`, `Sprite`, or folder `DefaultAsset`, including runtime
`new Texture2D` and `Sprite.Create` objects) in input order without deduplication (within a call or
across calls), skips null entries (logging `"Attempted to add null object into a Sprite Atlas."`)
and invalid objects such as `GameObject`, `RenderTexture`, `SpriteAtlas`, or non-folder
`DefaultAsset` (logging `"Attempted to add invalid object into a Sprite Atlas. <name> was a <Type>.
Only Folder, Texture and Sprite are valid."`) while still appending valid entries in the same array,
accepts `null` and empty arrays with no warning and unconditionally marks the atlas dirty on every
call across V1, V2, variants (without affecting the master) and `new SpriteAtlas()`, persists
`m_EditorData.packables` on V1 `SaveAssets`/`SaveAssetIfDirty` (writing `{fileID: 0}` for runtime
objects, inserting `packables` after `variantMultiplier` when omitted, and preserving unrelated
settings and `.meta`) while leaving V2 files and `.meta` unchanged and dirty, and on V2
`ImportAsset(ForceUpdate)` reloads a fresh clean atlas reverted to its `.spriteatlasv2`; `Remove`
removes every matching occurrence of each element in `objects` by swapping each removed slot with
the last packable (preserving distinct `Texture2D` vs `Sprite` asset identities on the same PNG,
matching destroyed runtime objects by their object identity rather than `null`, and leaving a
destroyed runtime packable as `null` in `GetPackables()` until removed), logs no warnings for
missing/null/invalid elements, accepts `null` and empty arrays, marks the atlas dirty once per
element plus once after the loop (`dirtyDelta += N + 1`, or `+1` for `null`/empty) across V1, V2,
variants (without modifying or dirtying their master) and `new SpriteAtlas()`, persists updated V1
`m_EditorData.packables` (including `packables: []` when emptied or omitted, preserving unrelated
settings and `.meta`) while leaving V2 `.spriteatlasv2` and `.meta` unchanged and dirty until
`ImportAsset(ForceUpdate)` reloads from disk; `SetVariantScale` clamps `value` to `[0.1, 1]`
(clamping zero, negative, below-minimum and `-Infinity` to `0.1`, above-maximum and `+Infinity` to
`1`, while leaving `NaN` unchanged), logs no warnings, leaves `isVariant` unchanged, unconditionally
marks the atlas dirty even on same-value repeats across V1, V2, variants (without dirtying their
master), non-variants and `new SpriteAtlas()`, persists modified V1 `m_EditorData.variantMultiplier`
(inserting after `secondaryTextureSettings` when omitted, preserving unrelated settings, packables
and `.meta`) while leaving V2 `.spriteatlasv2` and `.meta` unchanged and dirty until
`ImportAsset(ForceUpdate)` reloads from disk; `SetIsVariant` is a no-op when `isVariant` already
matches `value` (leaving dirtiness, master, tag, packables and settings untouched), and on change
updates `isVariant` and marks the atlas dirty once (without dirtying a variant's master); toggling
`true -> false` clears the master atlas reference to null and resets `tag` to the atlas's own name
while preserving packables, `variantMultiplier` and all settings, whereas toggling `false -> true`
leaves master, tag, packables, `variantMultiplier` and settings untouched; on V1 `.spriteatlas`,
`SaveAssets`/`SaveAssetIfDirty` persist modified `m_IsVariant`, `m_MasterAtlas` and `m_Tag`
(appending `m_IsVariant` after `m_Tag` when omitted, preserving unrelated fields and `.meta`) while
leaving V2 `.spriteatlasv2` and `.meta` unchanged and dirty until `ImportAsset(ForceUpdate)` reloads
from disk; `SetMasterAtlas` warns (`"Cannot set a master to this atlas because it was not set as a
variant atlas."`) and leaves non-variant atlases unchanged and clean (even when `value` is null),
returns without warning or dirtying when a variant already references the same master (including
`null` when already `null`), and on change updates the master reference, guards self-assignment
(warning `"Cannot set itself as the master atlas."` and clearing master to `null`) and
variant-as-master / cyclic variants (warning `"Cannot set a variant atlas as a master atlas."` and
clearing master to `null`), copies the valid master's `tag` (leaving `tag` unchanged when cleared to
`null` or by a guard), leaves `packingSettings`/`textureSettings`/`platformSettings` and the old/new
masters' dirtiness untouched, and marks the target atlas dirty once; on V1 `.spriteatlas`,
`SaveAssets`/`SaveAssetIfDirty` persist modified `m_MasterAtlas` (writing `{fileID: 0}` for cleared
or transient masters and inserting `m_MasterAtlas` after `m_EditorData` when omitted) and `m_Tag`
(preserving unrelated fields and `.meta`) while leaving V2 `.spriteatlasv2` and `.meta` unchanged
and dirty until `ImportAsset(ForceUpdate)` reloads from disk; a null atlas throws
ArgumentNullException for all fifteen (with non-null `src` for `SetPlatformSettings`);
`AssetImporter.GetAtPath` returns the cached `SpriteAtlasImporter` instance for a `.spriteatlasv2`
asset (while returning `TextureImporter` for a PNG and `null` for a missing path), an uncreated
`new SpriteAtlasImporter().GetPlatformSettings(...)` throws `NullReferenceException`, and
`SpriteAtlasImporter.GetPlatformSettings(string)` normalizes build-target names/aliases identically
to `SpriteAtlasExtensions.GetPlatformSettings`, applies legacy v1/v2/v3 migrations and the BC6H/BC7
max-quality rule, reads a V2 variant's own `.meta` (never inheriting its master's overrides),
returns fresh defaults named after the normalized target when absent or omitted (never inheriting
stored `DefaultTexturePlatform`), allocates a fresh distinct `TextureImporterPlatformSettings` copy
on every call for both stored and absent targets, and stays unaffected by unsaved in-memory
`SpriteAtlasExtensions.SetPlatformSettings` edits; `SpriteAtlasImporter.SetPlatformSettings`
normalizes `src.name` in place first (throwing `NullReferenceException` for null `src` without
dirtying, and normalizing a non-null `src.name` before throwing `NullReferenceException` on an
uncreated `new SpriteAtlasImporter()`), replaces matching entries in place (preserving order, even
when `overridden` is false), appends a defensive copy when absent only for `overridden: true` or
`DefaultTexturePlatform`, unconditionally marks the importer dirty even on same-value repeats or
uninserted non-overridden entries, keeps master and variant importers independent, is not written
or cleared by `AssetDatabase.SaveAssets`, writes unprefixed `serializedVersion: 4`
`platformSettings` (inserting omitted `textureSettings`/`packingSettings` defaults around it while
leaving `.spriteatlasv2` untouched) and clears importer dirtiness on
`AssetDatabase.WriteImportSettingsIfDirty` (returning `true` when dirty and `false` when clean,
without reimporting the loaded `SpriteAtlas`), and flushes `.meta`, evicts the cached importer and
reimports a fresh clean `SpriteAtlas` on both `SaveAndReimport` and
`AssetDatabase.ImportAsset(ForceUpdate)`; `SpriteAtlasImporter.packingSettings` throws
`NullReferenceException` on both accessors of an uncreated `new SpriteAtlasImporter()`, reads the
importer's own `.meta` `packingSettings` (returning Editor defaults when omitted, converting legacy
v1 `paddingPower` while keeping hidden `allowAlphaSplitting`, and returning a V2 variant's own
`.meta` settings independently of its master), stores assigned values verbatim (including negative
and zeroed `default` values) and unconditionally marks the importer dirty even on same-value
repeats (leaving the loaded `SpriteAtlas` unaffected until reimport and staying unaffected by
unsaved in-memory `SpriteAtlasExtensions.SetPackingSettings` edits), follows the same `SaveAssets`
(no write, stays dirty), `WriteImportSettingsIfDirty` (writes `serializedVersion: 2`
`packingSettings` and any omitted `platformSettings: []`/`textureSettings` defaults while leaving
`.spriteatlasv2` and unrelated `.meta` sections untouched, clears dirty, does not reimport), and
`SaveAndReimport`/`ImportAsset(ForceUpdate)` persistence and reload timing, and reimporting a V2
master atlas also updates its loaded V2 variant `SpriteAtlas` to inherit the master's updated
packing settings while leaving the variant importer's own `.meta` untouched;
`SpriteAtlasImporter.textureSettings` throws `NullReferenceException` on both accessors of an
uncreated `new SpriteAtlasImporter()`, reads the importer's own `.meta` `textureSettings`
(returning Editor defaults when omitted, converting legacy v1 `colorSpace` to `sRGB` while
preserving hidden fields, and keeping V2 variants independent of their master in both importer and
loaded `SpriteAtlas`), stores assigned values verbatim (including negative `anisoLevel`,
out-of-range `filterMode`, and zeroed `default` values) and unconditionally marks the importer
dirty even on same-value repeats without mutating the loaded `SpriteAtlas` until reimport, and
follows the same `SaveAssets`/`WriteImportSettingsIfDirty`/`SaveAndReimport`/
`ImportAsset(ForceUpdate)` persistence and reload timing (writing `serializedVersion: 2`
`textureSettings` and any omitted `platformSettings: []`/`packingSettings` defaults while leaving
`.spriteatlasv2` and unmodified `.meta` sections untouched); `SpriteAtlasImporter.variantScale`
throws `NullReferenceException` on both accessors of an uncreated `new SpriteAtlasImporter()`,
reads `variantMultiplier` from `.meta` (defaulting to `1` when omitted), stores assigned values
verbatim without clamping (including out-of-range values, `±Infinity` and `NaN`), marks the
importer dirty only when `variantScale != value` (so same finite values and `-0f` on `0f` are
no-ops while `NaN` always dirties, and `WriteImportSettingsIfDirty` returns `false` when `.meta`
bytes did not change), leaves `isVariant` and unrelated `.meta` sections untouched, and follows the
same `SaveAssets`/`WriteImportSettingsIfDirty`/`SaveAndReimport`/`ImportAsset(ForceUpdate)`
timing; `SpriteAtlasImporter.includeInBuild` throws `NullReferenceException` on both accessors of
an uncreated `new SpriteAtlasImporter()`, reads `bindAsDefault` from `.meta` (defaulting to `true`
when omitted), marks the importer dirty only when toggling the value, keeps master and variant
importers and loaded `SpriteAtlas` instances independent until reimport, writes `bindAsDefault`
(`1`/`0`, inserting after `variantMultiplier` when omitted), and follows the same
`SaveAssets`/`WriteImportSettingsIfDirty`/`SaveAndReimport`/`ImportAsset(ForceUpdate)` timing.
- `v1wholewrite` / `v2wholewrite`: `Assets/WholeWriteAtlases/` holds `Legacy` and `Untouched`
  atlases whose `.spriteatlas` (V1) or `.meta` (V2) keep legacy `textureSettings` (`colorSpace`),
  legacy and v3 `platformSettings` entries and legacy `packingSettings` (`paddingPower: 0`) and omit
  `secondaryTextureSettings` and `variantMultiplier`; `Legacy` changes only "Include in Build" before
  `SaveAssetIfDirty` (V1) or `WriteImportSettingsIfDirty` (V2), `Untouched` changes nothing, nor
  does `Current` (the same settings in the current format); each records whether the atlas or
  importer was dirty before and the whole file written.
  `LegacyVariant` (a legacy variant of `Current`), `LegacyExcluded` (legacy, not included in
  builds) and `Packed` (packing `Single.png`, its packed sprites not yet stored) with its variant `PackedVariant` are V1 controls
  too, and every V1 file is also recorded as it stands when Play mode starts (`*.atStart`).
  `v1wholewritedisabled` runs the V1 fixtures in mode "Disabled" (0), where entering Play mode
  packs nothing. The V1 profiles also give `Scaled` (a variant whose `variantMultiplier` is 2) a master with
  `SetMasterAtlas` before saving it, and saves `ScaledAlone` (the same) and
  `ScaledPlain` (not a variant) without it. The V2 profile also loads `LazyVariant` (a variant of `LazyMaster`) and changes the master's
  packing settings in memory before anything asks the variant for its settings, changes
  `EarlyMaster`'s before `EarlyVariant` first loads, and writes `UserData` (a legacy `.meta`)
  after changing only its importer's `userData`.

Not measured: either kind of atlas in the other kind's packer mode.

Values compare exactly.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteatlaseditor/run.py record --label a
python3 tests/projects/spriteatlaseditor/run.py record --label b
python3 tests/projects/spriteatlaseditor/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/spriteatlaseditor/run.py gate
```
