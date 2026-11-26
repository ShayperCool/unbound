# Texture mipmap limit probe

Which mips the GPU samples under `QualitySettings.globalTextureMipmapLimit`. Twenty-one textures
are drawn side by side into a 64-texel-high render texture, one 64x64 cell each, with point
filtering, and read back after every step. Per texture and step the probe writes `size`,
`mipmapCount`, `activeMipmapLimit`, `ignoreMipmapLimit` and the colours of texels 0..7 of the
cell's first two rows (`pixels`: one colour when they all agree).

Textures (`Assets/Resources/`, made by `Editor/ProbeSetup.Create`):

- `png`, `pngIgnore`, `pngNoMips`, `pngCompressed`, `pngLightmap`, `pngSprite`: one imported
  64x64 PNG of layered checkers (periods 1, 2, 4 and 8 texels), box-filtered mips, so each dropped
  mip removes one layer; with the importer's Ignore Mipmap Limit, without mips, block compressed,
  of the Lightmap and Sprite texture types.
- `asset`, `assetIgnore`, `small16`, `small8`, `small4`, `small2`, `chain3`, `chain5`, `wide`,
  `tall`: Texture2D assets with a distinct colour per mip (64x64 with and without the ignore
  flag; 16, 8, 4 and 2 texels square; 64x64 with 3 and 5 mips; 64x8 and 8x64).
- `array`: a two-slice Texture2DArray asset of the same per-mip colours (slice 1 drawn).
- `script`, `scriptDefault`, `scriptLocked`: textures made at run time with per-mip colours, with
  the ignore flag cleared, as made (a script texture ignores the limit by default), and with the
  flag cleared then made unreadable by `Apply(false, true)` under limit 2.

Steps: the level Play mode starts with (`Quarter`, limit 2), level 0 (`Full`, limit 0) and back,
the ignore flag toggled under limit 2 (on a readable asset, and on the non-readable PNG: the
setter's exception is recorded), script limits 0, 1, 3, 4, 7, -1 and 1 again, level 0 and level 1
again (a script limit is kept by the level it was set on), then `Texture.masterTextureLimit = 3`
and the quality level's limit 0 (`textureLimits.*`: the quality level's, `masterTextureLimit` and
`globalMipmapLimit`).

Measured (Unity 6000.0.71f1, Linux; OpenGL Core and Vulkan, and the Android target, all the same
limits): a texture with mips that does not ignore the limit drops `min(limit, mipmapCount - 1)`
top mips, but never so many that its smaller side falls below 8 texels; arrays, Lightmap and
Sprite types and script textures follow the same rule; `width`/`height` stay the full size; a
negative limit reads 0. `Texture.masterTextureLimit` (and `globalMipmapLimit`) is the limit
textures are sampled under: the quality level's sets it, setting it leaves the level's value. A
texture made unreadable keeps the mips dropped when it was uploaded; its `activeMipmapLimit`
follows the limit. On the Android target Unbound's own block encoder and lightmap encoding give
other colours (`residuals-android.json`). Not measured: textures whose sides are not powers of
two, mipmap limit groups, the Metal device.

Record twice with the Unity Editor (batch mode on the GPU), publish when the captures agree, and
compare Unbound:

    python3 tests/projects/mipmaplimit/run.py record --label a [--device vulkan | --profile android]
    python3 tests/projects/mipmaplimit/run.py record --label b [--device vulkan | --profile android]
    python3 tests/projects/mipmaplimit/run.py publish a b [--device vulkan | --profile android]
    python3 tests/projects/mipmaplimit/run.py gate     # every reference: OpenGL, Vulkan, Android

`ProbeSetup.Create` authors the textures and the scene; run it in a scratch copy of the probe
(`-executeMethod ProbeSetup.Create -quit`) and copy `Assets/` and
`ProjectSettings/EditorBuildSettings.asset` back.
