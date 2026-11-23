# Cubemaps imported from images

What a texture of the Cube shape (`textureShape: 2`) imports as, by its `generateCubemap` mode
(1 Spheremap, 2 Cylindrical, 5 FullCubemap, 6 AutoCubemap) and its image's aspect: 2:1, 16:9,
1:1, 3:2, 1:2, a 6:1 strip and a 4:3 cross, one non-power-of-two 2:1 image, and one 16:9 image
with `nPOTScale` None. `make_textures.py` writes the sources: RGBA8 PNGs whose texel (x, y) from
the top-left holds R = x, G = y, B = A = 255, imported readable, uncompressed, linear and
without mipmaps, so a face texel read back tells where in the image it was taken from.

`Assets/Editor/CubemapImportProbe.cs` records in Play mode, for each case: the main asset type,
the type `LoadAssetAtPath<Texture>` gives, and for a `Cubemap` its size, format, mip count,
readability and `GetPixel` of a 4x4 grid of texels of every face (R and G as integers, B and A
together). Numbers compare within 1: a texel interpolated from the image may round either way.

```sh
/usr/bin/python3 tests/projects/cubemapimport/make_textures.py
python3 tests/projects/cubemapimport/run.py record --label A     # twice, A and B
python3 tests/projects/cubemapimport/run.py publish A B
python3 tests/projects/cubemapimport/run.py gate
```
