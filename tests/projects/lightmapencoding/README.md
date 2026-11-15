# Lightmap and HDR cubemap encodings by build target

Unity 6000.0.71f1, built-in pipeline. How the active build target stores baked HDR lighting and
how shaders decode it. `ProbeRunner.Create` authored `Assets/Resources/`: a 16x8 float lightmap
(EXR, Lightmap type, intensities 0 to 45 along x and eight tints), a 6:1 strip of 4x4 float cube
faces (EXR, Cube shape), an 8-bit (PNG) Lightmap-type texture, and compressed copies of the float
lightmap and cubemap (the importer's default compression). The four uncompressed ones are point
sampled, clamped and mipless.

`Scripts/LightmapEncodingProbe` writes `lightmapencoding.jsonl`: every
`GraphicsSettings.HasShaderDefine` answer, each texture's format, graphics format, `isDataSRGB`
and mip count, `ReflectionProbe.defaultTextureHDRDecodeValues` and a Custom probe's
`textureHDRDecodeValues`, and 16x8 float readbacks of a quad lightmapped one pixel per texel:
the raw lightmap texel, `unity_Lightmap_HDR` and `DecodeLightmap`; the raw reflection at the 96
cube texel centres, `unity_SpecCube0_HDR` and `DecodeHDR` (scene default reflection and the
probe); the quad drawn with Legacy Shaders/Diffuse; the first three with the 8-bit lightmap.

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
  value (byte / 255) is taken as its radiance and encoded alike.
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
The engine matches 464/472 records. The 8 others are the compressed cubemaps (Unbound keeps
their pixels uncompressed), listed exactly in `residuals.json`; an entry that stops occurring
fails the check.
