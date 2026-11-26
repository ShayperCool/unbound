# DDS, KTX and ASTC source files

`make_sources.py` writes deterministic source files into `Assets/Sources` (committed without
`.meta` files, so the Editor writes them): DDS of DXT1, DXT5, BC5, BC6H, BC7, BC7 sRGB (DX10
headers for the BC5-BC7 ones), uncompressed RGBA8 and a DXT1 cubemap, with and without mips, one
NPOT; KTX 1 of ETC2 RGB, ETC2 RGBA and ASTC 4x4; `.astc` files of 4x4, 6x6 and 8x8 blocks (one NPOT).
`Assets/Scripts/IhvSourcesProbe.cs` records in Play mode into values.jsonl in `$IHVSOURCES_OUT`
(tests/value_probe.py), for each: the `.meta` the Editor wrote (`meta/`), its importer's type and
settings (`importer/`), the main asset (`asset/`: type, size, dimension, formats, mips,
readability, sampling settings), its sub-assets, and the raw texture data's length and SHA-256
(`raw/`); then, after making each importer readable and importing it again, the same
(`readable_*`).

What it shows (Unity 6000.0.71f1, two identical macOS batch captures): these files import through
`IHVImageFormatImporter` (its `.meta` section: `textureSettings` of version 2, `isReadable`,
`sRGBTexture`, streaming and mipmap-limit settings) into a `Texture2D`, or a `Cubemap` for a DDS
cube, of the file's own block format and mip chain, the Editor's raw data available even when the
importer is not readable. A DDS file's BC1, BC3, BC4 and BC5 blocks and texels are turned upside
down to the Editor's bottom-up rows (a level of one block's height left as it is, a shorter one's
rows reversed within the block); BC7, KTX and ASTC data is kept as in the file; 32-bit BGRA texels
become ARGB32.

`residuals.json` names the open differences: a BC6H DDS's raw data (the Editor's is a fill) and the
uncompressed DDS's ARGB32 storage.

Record and gate:

    python3 tests/projects/ihvsources/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/ihvsources/run.py publish A B
    python3 tests/projects/ihvsources/run.py gate
