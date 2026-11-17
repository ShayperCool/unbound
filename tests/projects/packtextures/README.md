# Texture2D.PackTextures of compressed and other textures

`Assets/Scripts/PackTexturesProbe.cs` makes textures at run time from fixed pseudo-random blocks
(or texels) with `LoadRawTextureData`, so no encoder takes part, and packs 26 sets of them with
`Texture2D.PackTextures`: DXT1, DXT5, BC7, ETC2 RGB, ETC2 RGBA8, ASTC 4×4 and the 4444 formats,
alone and mixed with each other and with RGBA32, with and without mipmaps (on all or some),
with and without padding, small textures at odd places, a texture over the maximum size, a
texture given twice. For each set it records the atlas's format, size, mip count, the inputs'
formats and mip counts, the rects, each level's bytes as an FNV-1a hash, and, for a compressed
atlas, whether each input's own level lies in it block for block (`own`), as empty blocks
(`zero`) or otherwise (`other`), and whether every block outside the inputs is zero.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, Linux, NVIDIA driver):

* The first texture's format, when it is one of those above, is kept by textures of the same
  format; DXT1 with DXT5 make DXT5, ETC2 RGB with ETC2 RGBA8 make ETC2 RGBA8; anything else, or a
  texture over the maximum size, makes RGBA32.
* A compressed format stays when all textures have mipmaps and there is no padding, or when
  none has mipmaps (padding or not); mipmaps on some only, or padding with mipmaps, make RGBA32.
* A compressed atlas places textures on whole blocks (with 2 texels of padding a 16 × 16
  texture goes at 36, not 34) and has as many levels as the smallest texture side has down to
  one block; each level holds each texture's own blocks at its place, DXT1 and ETC2 RGB blocks
  behind eight bytes of 0xFF (an opaque alpha block), and zero blocks elsewhere.
* ARGB4444 and RGBA4444 textures make an RGBA4444 atlas on this device (it does not sample the
  ARGB4444 layout), also with mipmaps on some only and with padding (the Scripting API page
  says RGBA32 for mixed mipmaps); its first level is the textures' texels.
* A texture whose place is not on a block in a lower level has no levels from there on
  (`dxt5offblock`: the 8 × 12 texture at (64, 20) shows its neighbour's blocks at level 1).
* An RGBA32 atlas is the textures' first levels with mip levels rebuilt from the first.

Known residuals (`residuals.json`): the RGBA4444 atlas's lower levels, and the texels of
textures shrunk to the maximum size. Not measured: ETC textures (the Editor reports runtime
ETC_RGB4 textures as ETC2_RGB), ETC2 RGBA1, other ASTC block sizes, crunched textures,
imported (unreadable) textures, the layout's shrinking of compressed textures at the maximum.

    python3 tests/projects/packtextures/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/packtextures/run.py record --label B
    python3 tests/projects/packtextures/run.py publish A B
    UNBOUND_CLI=<immutable runtime>/unbound python3 tests/projects/packtextures/run.py gate
