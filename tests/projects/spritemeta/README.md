# A sprite's first import

What the Editor writes into a texture's `.meta` the first time it imports it as a sprite, in a project
whose default behaviour mode is 2D (`EditorSettings.m_DefaultBehaviorMode: 1`). Unity 6000.0.71f1,
recorded with the macOS Editor in batch mode (two identical captures): the values depend on neither
the OS nor the graphics device, apart from the installation's platform modules, whose per-platform
texture entries are left out of each text (`.platforms` says whether those left out were the
installed modules).

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
| `Slice*` | none; 16x16 (`SliceNpot` 10x6) of opaque blocks on a transparent ground: an L, blocks touching at a corner, a pixel beside a block, blocks at the edges, faint ones (alpha 1, 50), five 3x3 blocks, 3-wide blocks a pixel apart, a ring, narrow, flat and 5x2 blocks beside a big one, lone 3x3 and 4x4 blocks, two big blocks with pixels between or near them, a chain of overlapping rows, an empty texture |

`Assets/Scripts/SpriteMetaProbe.cs` records in Play mode into values.jsonl in `$SPRITEMETA_OUT`
(tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:

* `open/<texture>`: every `.meta` as the project opened with it; `end/<texture>`: the same at the end.
* `importer/<texture>`: the importer's type, sprite mode, NPOT scaling, wrap modes, mipmaps, alpha
  transparency and pixels per unit.
* `sprites/<texture>`: the main asset's type and each sprite's name, rect, pivot, pixels per unit and
  sprite ID (`SpriteEditorExtension.GetSpriteID`).
* `session/<texture>`: the `.meta` of a copy of `New`, `NewNpot` and `NewGaps` written into
  `Assets/Session` in Play mode and imported by `ImportAsset`, and of `SliceFive` imported by
  `Refresh`; their `importer/` and `sprites/` rows too.

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
(the rect around both; of equal gaps the later in scan order), and a texture without any larger
region, or without any opaque pixel, is one sprite of its whole size; each rect is a pixel larger on
every side, clamped. The sprites are numbered in the order a scan from the top row down, left to
right, meets their regions, or, when small regions were joined, in reading order (on shared rows
left first, else higher first). A new texture ImportAsset or Refresh imports during the session gets
the same `.meta` (`session/*`). A sprite's `.meta` without `serializedVersion` is written whole in the
release's layout, its top-level keys kept, its `textureSettings` keys read as defaults (`BareWrap`,
`BarePpu`, `BareFull`); a Default texture's is left as it is (`BareDefault`). A Default texture is
left as it is.

Not covered: `SaveAndReimport`, `Multiple` sprites without IDs, formats other than PNG, a bake's
images, a 3D project's new texture (that is
the Default texture's `.meta`, tests/projects/projectwrites).

Record and gate:

    python3 tests/projects/spritemeta/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/spritemeta/run.py publish A B
    python3 tests/projects/spritemeta/run.py gate

`residuals.json` names the open differences: the `.meta` files of few keys (an older layout Unbound
keeps).
