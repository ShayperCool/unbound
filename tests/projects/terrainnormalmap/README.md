# Terrain.normalmapTexture

`Assets/Scripts/TerrainNormalmapProbe.cs` makes terrains from fixed heights (33 × 33 samples over
64 × 16 × 64 units; 65 × 65 over 30 × 40 × 50), renders them with `Camera.Render` and records
`Terrain.normalmapFormat`, `normalmapRenderTextureFormat` and `normalmapTextureFormat`; whether a
terrain has `normalmapTexture` when it is made, after a render, once `drawInstanced` is on (before
and after a render), after `SetHeights` (before and after the next render) and once instancing is
off again, and read for the first time after `SetHeights` (with and without a render since
instancing went on: `firstread.*`, rows 9-17); the texture's name, size, formats, mip levels, sampling and hide flags; and its texels
(the first level, the second and the last of the first terrain), read back as floats and written
in the steps of its 10-bit channels (alpha in its 2-bit steps). The texels are the only numbers and
compare within 5 (one 8-bit step of the 10-bit values); every other value is exact.

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core, NVIDIA; two identical captures):

* The formats are `A2B10G10R10_UNormPack32`, `ARGB2101010` and no `TextureFormat` (value 0).
* A terrain has the texture only while `drawInstanced` is on: from the moment it is turned on,
  already filled; turning it off destroys the texture. It is named `TerrainNormalMap`, one texel
  per heightmap sample, not sRGB, with mip levels (not generated automatically) filled from the
  first, bilinear, clamped, `HideFlags` 125.
* Each texel is the heightmap's normal at its sample: the Sobel gradient of the heights (weights
  1, 2, 1 across the axis, edges clamped) times the terrain's height over the cell size of that
  axis, the normal (-dx, 1, -dz) normalized and stored as n · 0.5 + 0.5 in 8 bits (the 10-bit
  values are 8-bit ones widened: 103 is 413), red along x, green up, blue along z, alpha one. The
  Editor computes it on its GPU: about one value in eight is one 8-bit step from this rule.
* A height change reaches the texture at the next camera render (measured with one that did not
  show the terrain: `firstread.rendered.again`; the same texture; only
  the changed samples and their neighbours change), not when it is made; a texture read for the
  first time after a change holds the heights from before it.

Unbound makes the texture on the CPU by that rule and copies it in (its mip levels from the
first) when instancing is turned on, and refreshes the whole of it at a render after the heights
changed; every value matches within the step. Unmeasured: other graphics devices, a terrain loaded
with instancing on (Unbound makes the texture when it is enabled), a terrain disabled, its
TerrainData replaced or its heightmap resolution changed while instanced (Unbound refills the
texture at the next render, a new one when the resolution changed), holes.

    python3 tests/projects/terrainnormalmap/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/terrainnormalmap/run.py record --label B
    python3 tests/projects/terrainnormalmap/run.py publish A B
    UNBOUND=/path/to/unbound python3 tests/projects/terrainnormalmap/run.py gate
