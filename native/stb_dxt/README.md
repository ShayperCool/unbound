# Runtime DXT colour encoder

`stb_dxt.h` is the public-domain v1.04 encoder from
[nothings/stb, revision bfef4c1](https://github.com/nothings/stb/blob/bfef4c1/stb_dxt.h).
Its source and author credit are retained unchanged.

`../unbound_runtime_dxt.cpp` enables rounded thirds when fitting the colour palette and maps
highQuality to colour dithering. Tables initialize once before parallel calls. Channel blocks
use the shared Rust runtime fitter in texture/bcn.rs. These settings are measured in
Unity 6000.0.71f1, tests/projects/texturecompression.
