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

`Assets/Scripts/SpriteMetaProbe.cs` records in Play mode into values.jsonl in `$SPRITEMETA_OUT`
(tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:

* `open/<texture>`: every `.meta` as the project opened with it; `end/<texture>`: the same at the end.
* `importer/<texture>`: the importer's type, sprite mode, NPOT scaling, wrap modes, mipmaps, alpha
  transparency and pixels per unit.
* `sprites/<texture>`: the main asset's type and each sprite's name, rect, pivot, pixels per unit and
  sprite ID (`SpriteEditorExtension.GetSpriteID`).

What it shows: a Single sprite whose `.meta` has no sprite ID gets one at its first import, and its
`.meta` is written: the ID, `nPOTScale: 0`, the installed modules' platform entries, the file's
layout otherwise kept (`EmptyId`, `EmptyIdPot0`); the ID is a GUID of the xxHash64 of
`Type:Sprite->0` (a sprite of no name: the same for every texture) and the word 0x80. A sprite with an
ID keeps its `.meta` as it is, its NPOT scaling read as none (`IdNpot`). A `.meta` of few keys is
written whole in the release's layout (`Bare`). In the 2D mode a new texture imports as `Multiple`
sprites, clamped, without mipmaps or NPOT scaling, its alpha transparency, sliced automatically: an
opaque texture is one sprite `<name>_0` of the whole texture (pivot written as 0,0 with the centre
alignment), whose file id is the xxHash64 of `Type:Sprite-><name>_00` and whose sprite ID holds that
id (`New`, `NewNpot`); one with transparent pixels gives a sprite per opaque region, each rect a
pixel larger, clamped (`NewGaps`). A Default texture is left as it is.

Not covered: a sprite imported later in a session (`ImportAsset`, `SaveAndReimport`), `Multiple`
sprites without IDs, other sizes and shapes of opaque regions, a 3D project's new texture (that is
the Default texture's `.meta`, tests/projects/projectwrites).

Record and gate:

    python3 tests/projects/spritemeta/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/spritemeta/run.py publish A B
    python3 tests/projects/spritemeta/run.py gate

`residuals.json` names the open differences: the automatic slicing of a texture with transparent
pixels, and a `.meta` of few keys (an older layout Unbound keeps).
