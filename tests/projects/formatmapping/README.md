# Texture and graphics format mapping

`Assets/Scripts/FormatMappingProbe.cs` records `GraphicsFormatUtility.GetTextureFormat` for every
`GraphicsFormat` name, `GraphicsFormatUtility.GetGraphicsFormat` (linear and sRGB) for every
`TextureFormat` name (values as integers; some values have two names), and the `format` and
`graphicsFormat` of `Texture2D`, `Texture3D` and `Texture2DArray` created from `R8_UNorm`,
`R8_SRGB`, `R8G8_UNorm` and `R16_UNorm`. Values compare exactly.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA driver): `R8_UNorm` and
`R8_SRGB` are `R8` (not `Alpha8`, whose graphics format is the hidden value 54); `R8` and `RG16`
in sRGB are `R8_SRGB` and `R8G8_SRGB`; a few graphics formats name a texture format that does not
map back to them (`B8G8R8_*` 8, `RGBA_DXT3_*` 11, PVRTC, `YUV2`). A probe-volume pool of
`R8_UNorm` validity data depends on the first: the textures it copies from must keep the format.
`residuals.json` lists the `GetGraphicsFormat` rows Unbound does not reproduce yet.

    python3 tests/projects/formatmapping/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/formatmapping/run.py publish A B        # two identical captures
    python3 tests/projects/formatmapping/run.py gate               # the engine against Reference/
