# A sprite's first import

What the Editor writes into a texture's `.meta` the first time it imports it as a sprite, in a project
whose default behaviour mode is 2D (`EditorSettings.m_DefaultBehaviorMode: 1`). Unity 6000.0.71f1,
recorded with the macOS Editor in batch mode (two identical captures): the values depend on neither
the OS nor the graphics device, apart from the installation's platform modules, whose per-platform
texture entries are left out of each text (`.platforms` says whether those left out were the
installed modules, `.modules` whether each was a copy of the first default entry but for its name).

`Assets/Textures` holds PNGs (4x4 opaque unless named otherwise):

| texture | its `.meta` |
| --- | --- |
| `Control` | a new texture's settings (the layout of `crates/unbound-assets/src/new_meta/texture_importer.yaml`, the default and Standalone entries), a Default texture |
| `EmptyId` | the same, `textureType: 8` and `spriteMode: 1` (a Single sprite), no sprite ID, `nPOTScale: 1` |
| `IdNpot` | as `EmptyId` with the sprite ID the Editor gives a single sprite |
| `EmptyIdPot0` | as `EmptyId` with `nPOTScale: 0` |
| `Bare` | only `textureType: 8` and `spriteMode: 1` |
| `New` | none |
| `NewNpot` | none; 6x5 |
| `NewGaps` | none; 16x8, two opaque blocks apart on a transparent ground |
| `BareWrap`, `BarePpu` | as `Bare` with `textureSettings: wrapU: 1`, with `spritePixelsToUnits: 50` |
| `BareDefault` | only `textureType: 0` |
| `BareFull` | `Control`'s keys as a Single sprite, without `serializedVersion` |
| `BareLegacy`, `BareSize` | as `Bare` with `maxTextureSize: 512`, `compressionQuality: 100` and (`BareLegacy`) `textureFormat: 3` |
| `BareDefaultEntry` | as `Bare` with a default entry of a few keys (`maxTextureSize: 1024`, compressed) |
| `BareSheet`, `BarePlat`, `BareTag` | as `Bare` with a `spriteSheet` of no `serializedVersion` at its defaults, with a `Standalone` entry of no `serializedVersion`, with keys the release lacks (`spritePackingTag`, `wrapMode`) |
| `SaveData`, `SaveNpot`, `SaveClear`, `SaveBundle`, `SaveKeep`, `SaveWrap` | as `Bare` with its sprite ID (`SaveClear` also a default and a `Standalone` entry of a few keys, `SaveWrap` `textureSettings: wrapU: 1`) |
| `KeepWrap`, `KeepWrapV2`, `KeepWrapMode` | as `Bare` with its sprite ID and `textureSettings` of `wrapU: 1`, of `serializedVersion: 2`, `filterMode: 0` and `wrapU: 1`, of `filterMode: 0` and `wrapMode: 1` |
| `KeepDefaultWrap` | only `textureType: 0` and `textureSettings: wrapU: 1` |
| `CurrentWrap`, `CurrentWrapMode` | only `serializedVersion: 13`, `textureType: 0` and `textureSettings` of `filterMode: 0` and `wrapU: 1`, of `wrapMode: 1` |
| `Order*`, `Near*` | none; 16x16 (`NearGrown` 16x8) blocks whose rows touch (side by side, a staircase, a tall block, padded grids of 2x2 and 3x3, tops at the edge), and pixels between big blocks at different gaps, on one row, two and three in a row, and joins that move a rect within its row |
| `Slice*` | none; 16x16 (`SliceNpot` 10x6) of opaque blocks on a transparent ground: an L, blocks touching at a corner, a pixel beside a block, blocks at the edges, faint ones (alpha 1, 50), five 3x3 blocks, 3-wide blocks a pixel apart, a ring, narrow, flat and 5x2 blocks beside a big one, lone 3x3 and 4x4 blocks, two big blocks with pixels between or near them, a chain of overlapping rows, an empty texture |

`Assets/Scripts/SpriteMetaProbe.cs` records in Play mode into values.jsonl in `$SPRITEMETA_OUT`
(tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:

* `open/<texture>`: every `.meta` as the project opened with it; `end/<texture>`: the same at the end.
* `importer/<texture>`: the importer's type, sprite mode, NPOT scaling, wrap modes, mipmaps, alpha
  transparency and pixels per unit; `texture/<texture>`: the imported Texture2D's wrap modes, filter mode, anisotropy,
  mip bias, mip count and format; `platform/<texture>`: its default and `Standalone` platform
  settings, `maxTextureSize` and `compressionQuality`.
* `sprites/<texture>`: the main asset's type and each sprite's name, rect, pivot, pixels per unit and
  sprite ID (`SpriteEditorExtension.GetSpriteID`).
* `session/<texture>`: the `.meta` of a copy of `New`, `NewNpot` and `NewGaps` written into
  `Assets/Session` in Play mode and imported by `ImportAsset`, and of `SliceFive` imported by
  `Refresh`; their `importer/` and `sprites/` rows too.
* `save/<texture>/meta`: the `.meta` of each `Save*` texture after a script's change of its importer
  and `SaveAndReimport` (`userData`, `npotScale` `ToLarger`, `ClearPlatformTextureSettings("Standalone")`,
  `assetBundleName` `Mixed-Bundle`, none, `userData`), with its `importer/saved_*` and `platform/saved_*` rows.

What it shows: a Single sprite whose `.meta` has no sprite ID gets one at its first import, and its
`.meta` is written: the ID, `nPOTScale: 0`, the installed modules' platform entries, the file's
layout otherwise kept (`EmptyId`, `EmptyIdPot0`); the ID is a GUID of the xxHash64 of
`Type:Sprite->0` (a sprite of no name: the same for every texture) and the word 0x80. A sprite with an
ID keeps its `.meta` as it is, its NPOT scaling read as none (`IdNpot`). A `.meta` of few keys is
written whole in the release's layout (`Bare`). In the 2D mode a new texture imports as `Multiple`
sprites, clamped, without mipmaps or NPOT scaling, its alpha transparency, sliced automatically: an
opaque texture is one sprite `<name>_0` of the whole texture (pivot written as 0,0 with the centre
alignment), whose file id is the xxHash64 of `Type:Sprite-><name>_00` and whose sprite ID holds that
id (`New`, `NewNpot`); one with transparent pixels is sliced (`NewGaps`, `Slice*`): the 8-connected
regions of non-zero alpha; a region narrower or lower than 4 pixels joins the nearest larger one
(the rect around both; the small ones from the last a scan from the bottom row up meets back, each
by the squared gap to the regions as earlier joins grew them, of equal gaps the one that scan meets
first), and a texture without any larger region, or without any
opaque pixel, is one sprite of its whole size; each rect is a pixel larger on every side, clamped.
The sprites are numbered in rows: the rect that scan meets last of those left and every rect whose
rows touch its rows, left to right (of equal left the one of the higher top first), then the next
row of the rest. A new texture ImportAsset or Refresh imports during the session gets
the same `.meta` (`session/*`). A sprite's `.meta` without `serializedVersion` is written whole in the
release's layout, its top-level keys kept, `applyGammaDecoding` and `cookieLightType` 1, its
`textureSettings` keys read as defaults, after its platform entries a default one and the installed
platforms it lacks as copies of the first default entry (the file's, else the added one), the added
default entry uncompressed, of the section's `maxTextureSize` and `compressionQuality` (`BareWrap`,
`BarePpu`, `BareFull`, `BareSize`, `BareDefaultEntry`), the keys the release lacks left out (`BareTag`);
the importer reads such a section so before any write (`importer/Save*`: wraps Repeat), and it imports so
(`texture/Save*`, `KeepWrap`: Repeat; without a default entry uncompressed, RGB24); a `textureFormat`
of 3 also makes `alphaUsage` 0 (`BareLegacy`), and an entry of no `serializedVersion` reads other
defaults (`BarePlat`: compressed, `forceMaximumCompressionQuality_BC6H_BC7` 1). A script's save of such
a section that has its sprite ID writes it the same way with the change (a cleared entry left out, a
missing `nPOTScale` as 0, the bundle name in lower case); a save of no change writes nothing
(`save/*`). A Default texture's is left as it is (`BareDefault`), and read as a SingleChannel
texture with the same uncompressed default entry (imported Alpha8; `KeepDefaultWrap` too). Texture settings without their own
`serializedVersion` are of version 1 in a section of any version and type: their `wrapMode` is every
axis's, their `wrapU`, `wrapV` and `wrapW` are not read, their `filterMode` is (`KeepWrap`,
`KeepWrapMode`, `KeepDefaultWrap`, `CurrentWrap`, `CurrentWrapMode`); those of version 2 are read axis by
axis (`KeepWrapV2`); a wrap no settings hold is Repeat (`Bare`, `KeepWrapV2`'s `wrapV`).

Not covered: other changes of an oldest section's platform entries (one set that the file lacks),
`Multiple` sprites without IDs, formats other than PNG, a bake's
images, a 3D project's new texture (that is
the Default texture's `.meta`, tests/projects/projectwrites).

Record and gate:

    python3 tests/projects/spritemeta/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/spritemeta/run.py publish A B
    python3 tests/projects/spritemeta/run.py gate

`residuals.json` names the open differences: the importer's reading of an oldest-layout `.meta`'s
platform entries before any write, a Default texture's reading, and the conversion of `BareLegacy` and `BarePlat` (not implemented: Unbound keeps
their layout and says so).
