# Compressed cubemaps

Unity 6000.0.71f1, built-in render pipeline, Standalone build target, linear colour space,
batchmode Editor on the GPU (Linux, OpenGL Core, NVIDIA driver). `create.py` ran
`ProbeRunner.Create` once in the Editor: it wrote seven 6:1 strips of 8x8 faces
(`Assets/Resources`) and imported each as a cubemap, point sampled, clamped, mipmapped:

| Cubemap | Source | Compression |
|---|---|---|
| `HdrPlain` | float EXR, radiance up to about 6 | uncompressed (control) |
| `HdrCompressed` | the same | default (Compressed) |
| `HdrConvolved` | the same | default, specular convolution |
| `LdrPlain` | opaque PNG | uncompressed (control) |
| `LdrCompressed` | the same | default |
| `LdrAlpha` | PNG with alpha | default |
| `LdrHigh` | opaque PNG | high quality |

The outputs (sources, `.meta` files, the scene) are committed; the gate does not run the Editor.
`Assets/Scripts/CubeCompressionProbe.cs` records each cubemap's `format`, `graphicsFormat`,
size, mip count and `isDataSRGB`, then samples every texel of every mip on the GPU
(`Hidden/CubeTexels`: the direction to each texel's centre, `texCUBElod`) into a float target
and reads it back (RGB, and alpha for `LdrAlpha`).

## What the Editor does (two identical captures)

* A cubemap's faces are stored as a 2D texture of the face size is: BC6H for floats, DXT1 for
  opaque bytes, DXT5 with alpha, BC7 at high quality, sRGB for sRGB bytes; uncompressed, floats
  are RGBAHalf and opaque bytes RGB24. The mip levels of 2x2 and 1x1 are compressed too.
* The texels the GPU samples are those of the blocks, every face in its own orientation.

Not measured: other build targets here (tests/projects/lightmapencoding records the formats
of a compressed float cubemap on Android), faces whose size is not a multiple of 4, cubemap
arrays.

```sh
python3 tests/projects/cubecompression/create.py --label <L>       # once: author the inputs
python3 tests/projects/cubecompression/run.py record --label <L>   # Editor capture
python3 tests/projects/cubecompression/run.py publish <L1> <L2>    # two identical captures
python3 tests/projects/cubecompression/run.py gate                 # engine vs Reference
```

The gate compares every value within 1e-6: the uncompressed controls read back the Editor's
values, whose text differs from the engine's by at most 1e-8 (float formatting).
