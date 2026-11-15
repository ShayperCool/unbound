# Lightmap and HDR cubemap encodings by build target

Unity 6000.0.71f1, built-in pipeline. How the active build target stores baked HDR lighting and
how shaders decode it. `ProbeRunner.Create` authored `Assets/Resources/`: a 16x8 float lightmap
(EXR, Lightmap type, intensities 0 to 45 along x and eight tints), a 6:1 strip of 4x4 float cube
faces (EXR, Cube shape), an 8-bit (PNG) Lightmap-type texture, mipmapped copies of the two
lightmaps, and compressed copies of the float lightmap and cubemap (the importer's default
compression). The uncompressed ones are point sampled and clamped, mipless but for the copies.

`Scripts/LightmapEncodingProbe` writes `lightmapencoding.jsonl`: every
`GraphicsSettings.HasShaderDefine` answer, each texture's format, graphics format, `isDataSRGB`
and mip count, `ReflectionProbe.defaultTextureHDRDecodeValues` and a Custom probe's
`textureHDRDecodeValues`, and 16x8 float readbacks of a quad lightmapped one pixel per texel:
the raw lightmap texel, `unity_Lightmap_HDR` and `DecodeLightmap`; the raw reflection at the 96
cube texel centres, `unity_SpecCube0_HDR` and `DecodeHDR` (scene default reflection and the
probe); the quad drawn with Legacy Shaders/Diffuse; the first three with the 8-bit lightmap; a
material's `_Cube_HDR` and `_Flat_HDR` and its cube decoded with them; level 1 of mipmapped
float and 8-bit lightmaps; a float 2D colour texture's (`Hdr2d.exr`) material decode values;
the cube's decode values through a property block and as a global texture; `unity_Lightmap_HDR`
of a draw using the second of two lightmaps whose first is a plain texture; which of a texture's
and an explicit `_Cube_HDR` value wins on materials, blocks and globals (`precedence/*`); the
built-in `Texture2D` default textures' names, formats and sampled texels (`default/*`).

`profiles.py` lists the eight profiles, each a project copy with a build target, colour space and
the Player Settings lists `m_BuildTargetGroupLightmapEncodingQuality` and
`m_BuildTargetGroupHDRCubemapEncodingQuality` (none: the target's default):
Standalone Linear, Standalone Low Linear, Android Linear and Gamma (defaults), Android Low Linear,
Android Normal Linear and Gamma, Android High Linear. The Editor ran on the NVIDIA driver
(GPU-backed batchmode, `-buildTarget <target>`); both captures of every profile were identical.

Measured (the Editor's defaults are Standalone High/High and Android Low/Low):

- Android defines UNITY_NO_RGBM and UNITY_NO_CUBEMAP_ARRAY; the lightmap define names the
  lightmap encoding. The tier defines are never answered, LPPV follows the tier settings.
- Lightmaps: Low is double LDR on Android (RGB24, the byte is half the gamma-space value) and
  RGBM on Standalone; Normal is RGBM (RGBA32); High keeps half floats. An 8-bit lightmap's stored
  value (byte / 255) is taken as its radiance, filtered into mips as floats and encoded alike;
  an unencoded one filters its mips as stored (not in linear light).
- A texture's `<name>_HDR` is its decode values, on a material, through a property block and as
  a global texture; Android's float 2D colour texture (RGBM) decodes as RGBM there.
- Setting a texture writes its `<name>_HDR`: the later of the texture and an explicit vector
  wins on a material, a block and globals; a block's plain texture replaces a material's encoded
  values; a material's own texture's values win over a global's.
- Default textures: white (255), black and red with alpha 0 (red has 3 mips), grey 127 in every
  channel, `linearGrayTexture` a separate linear `UnityLinearGrey`, the normal (127, 127, 255)
  linear with alpha 255 on Standalone and 127 on Android; in a gamma project all are linear.
- `unity_Lightmap_HDR` is the first lightmap's: a plain first texture leaves (1, 1, 0, 0) for an
  encoded second.
- HDR cubemaps: Low double LDR, Normal RGBM, High half floats, on both targets.
- Decode values: double LDR 2 (2^2.2 for sRGB data in Linear), RGBM 5 and alpha (5^2.2, exponent
  2.2 in Linear); `unity_Lightmap_HDR` follows the first lightmap.
- Compressed: Android double LDR ETC2_RGB (Linear) or ETC_RGB4 (Gamma), RGBM ETC2_RGBA8, High
  ASTC_HDR_6x6; Standalone BC6H, RGBM lightmaps DXT5, double LDR cubemaps DXT1.

```sh
python3 tests/projects/lightmapencoding/record.py create  --release 6000.0.71f1
python3 tests/projects/lightmapencoding/record.py record  --release 6000.0.71f1
python3 tests/projects/lightmapencoding/record.py publish --release 6000.0.71f1
UNBOUND=<unbound> python3 tests/projects/lightmapencoding/run.py check
```

`run.py check` compares every record; capture pixels within a relative 1e-3 (the Editor's
readbacks of 8-bit sRGB data carry about 2^-11 of rounding, one 8-bit step is more than 0.9%).
The engine matches all 752 records, the compressed cubemaps included (Q497: a cubemap's faces
are compressed as 2D textures of the face size, tests/projects/cubecompression).
`residuals.json` lists the open differences (none); an entry that stops occurring fails the
check.
