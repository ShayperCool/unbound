# Arm astc-encoder

The codec library sources are from [Arm astc-encoder 4.3.0](https://github.com/ARM-software/astc-encoder/tree/4.3.0), under Apache-2.0. `LICENSE.txt` contains the license. The command-line tool and image loaders are omitted.

Local changes keep separate encoder contexts independent and safe to use concurrently:

- `astcenc_compress_symbolic.cpp` uses the current context's search threshold.
- `astcenc_entry.cpp` selects preset tables in a local variable.
- `astcenc_weight_align.cpp` initializes shared angular tables once.

`../unbound_astc.cpp` provides the C interface used by the texture importer and CPU pixel reads. The build uses SSE2 on x86_64 and NEON on aarch64, with fused multiply-add contraction disabled. Encoder settings and matching block counts are recorded in `tests/projects/texturecompression`.
