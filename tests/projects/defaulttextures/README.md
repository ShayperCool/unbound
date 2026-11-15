# Default textures in a linear project

`Assets/Scripts/DefaultTexturesProbe.cs` draws one texel into a float render target and records,
per channel to four decimals, what a shader samples from each texture property's default
(`white`, `black`, `gray`, `grey`, `bump`, `red`, `linearGray`), from a sampler without a property,
from each default again after the material set the property to null, and from the built-in
textures of `Texture2D` (`whiteTexture`, `blackTexture`, `grayTexture`, `linearGrayTexture`,
`normalTexture`, `redTexture`) set on a property, with each one's `graphicsFormat`. The project is
in the linear colour space (`tests/projects/resources` measures the defaults in gamma space).
Numbers compare within 0.002.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA driver):

* `bump` and `linearGray` hold linear data: they sample 127/255 without sRGB decoding (the flat
  normal reads 0.498, 0.498, 1, 1). `gray`, `grey` and a sampler without a property are colours,
  decoded from sRGB.
* A property set to null samples its default.
* `Texture2D.normalTexture` and `linearGrayTexture` are `R8G8B8A8_UNorm`, the other built-in
  textures `R8G8B8A8_SRGB`; `blackTexture` and `redTexture` have alpha 0, `grayTexture` 127/255 in
  every channel.

The engine matches every value (`residuals.json` is empty); the built-in textures' colours,
colour spaces and mips are also recorded by tests/projects/lightmapencoding `default/*` on
Standalone and Android, in linear and gamma projects.

    python3 tests/projects/defaulttextures/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/defaulttextures/run.py record --label B
    python3 tests/projects/defaulttextures/run.py publish A B
    UNBOUND=/path/to/unbound python3 tests/projects/defaulttextures/run.py gate
