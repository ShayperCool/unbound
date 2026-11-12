# Texture compression

What block-compressed textures give a game on the Standalone target (Gamma and Linear projects)
and the Android target (Gamma and Linear projects with the default compression format unset, and
Gamma projects whose Android defaults are ETC2 and ASTC).
`make_textures.py` writes the sources and the importer cases (one setting per case, the
textureimport probe's images); `ProbeRunner.Create` applies them in the Editor.
`Assets/Scripts/TextureCompressionProbe.cs` records:

- every import: size, mip count, `format`, `graphicsFormat`, readability, `GetRawTextureData`,
  each level's `GetPixelData<byte>` length, `GetPixels32` of every level, `GetPixel(5, 7)` and
  `GetPixelBilinear(0.3, 0.6)` as floats, and the GPU's samples (a blit into an ARGBFloat target
  read back); unreadable textures: what `GetPixels` and `GetPixelData` throw;
- `SystemInfo.SupportsTextureFormat` of every `TextureFormat`, `SystemInfo.IsFormatSupported` of
  the compressed graphics formats, `SystemInfo.copyTextureSupport`;
- compressed `Texture2D`s made by code (also 30x18 and 20x12), `LoadRawTextureData` with an
  import's blocks (whole chain, too short, too long, top level only), `Texture2D.Compress(false)`
  and `(true)` of RGBA32, RGB24, R8, RG16, a 30x18 RGBA32 and a compressed texture, and
  `Graphics.CopyTexture` between compressed textures (whole, a block-aligned region, an unaligned
  region, one mip, from an unreadable texture, into an unreadable texture, into a readable
  import, DXT1 to DXT5, DXT5 to BC7, DXT1 to RGBA32), `Reinitialize` of a compressed texture,
  writes to a compressed import, and explicit RGB24/R8 imports of a coloured, transparent source;
- R8, RG16 and Alpha8 `Texture2D`s made by code, linear and sRGB: format, raw data, pixels and
  the GPU's samples, from `LoadRawTextureData` (with mips) and from `SetPixels32`; `CopyTexture`
  from a linear R8/RG16 texture into an sRGB one, back, and from a render texture of the format.

Each control records what it threw and the errors it logged.

```sh
python3 tests/projects/texturecompression/make_textures.py
python3 tests/projects/texturecompression/run.py create --release 6000.0.71f1          # metas, scene
python3 tests/projects/texturecompression/run.py editor --release 6000.0.71f1 --label L   # two captures per profile
python3 tests/projects/texturecompression/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=<unbound> python3 tests/projects/texturecompression/run.py gate --release 6000.0.71f1 --label L
```

`gate` runs Unbound and compares each profile with `Reference/` exactly; `residuals.json` lists
the known differences (`{profile: {key or glob: why}}`).

## Measured (6000.0.71f1, both repeats identical)

Observations of this release, its Standalone target and the Editor's OpenGL Core device:

- For BC imports with mipmaps, only 2D textures whose sides are powers of two and multiples of 4 are compressed: 20x12,
  30x18 and 2x2 stay RGBA32 (RGB24 when opaque), automatic or an explicit DXT5; 64x16 compresses.
- Automatic formats: Normal and Low Quality give DXT1, or DXT5 when a texel is not opaque (alpha
  source None: DXT1); High Quality BC7; normal maps DXT5 (Normal, Low) and BC7 (High), DXT5nm
  layout; HDR (EXR) BC6H at every quality; a Single Channel texture's red BC4, its alpha Alpha8.
- Compressor quality (0, 50, 100) does not change DXT1, DXT5 or BC4 blocks; it changes BC7 and
  BC6H.
- `GetRawTextureData` of an unreadable compressed import returns its blocks;
  `GetPixels32` of a level smaller than a block returns the whole decoded block, the level
  repeated as tiles (blocks of 2x2 and 1x1 levels are encoded from the tiled level).
- `GetPixels32` decodes colour with truncating thirds and alpha/red/green steps rounded to
  nearest; BC4 reads (r, 0, 0, 1), BC5 (r, g, 0, 1).
- `new Texture2D(30, 18, DXT1)` throws "Failed to create texture because of invalid parameters."
- R8 and RG16 made by code are R8_UNorm and R8G8_UNorm in the Gamma project, sRGB or not; in the
  Linear project sRGB ones are R8_SRGB and R8G8_SRGB and the GPU decodes their red (and green)
  from sRGB; R8G8_SRGB samples blue 1, R8G8_UNorm blue 0. Alpha8 samples (0, 0, 0, a) either way.
  `CopyTexture` between R8_UNorm and R8_SRGB textures (or an R8 render texture) copies the raw data
  and the GPU's texels; between R8G8_UNorm and R8G8_SRGB it copies the raw data, but the
  destination's GPU texels stay as they were.

## Measured on the Android target (6000.0.71f1, both repeats identical)

The same images with an Android override where a case names `android=` (its quality applies):

- Automatic formats at every compression level follow the project's default compression
  format: unset (ETC) gives ETC_RGB4 for opaque textures and ETC2_RGBA8 with transparent texels;
  ETC2 gives ETC2_RGB and ETC2_RGBA8. Normal maps are opaque; a Single Channel texture's red is
  EAC_R and its alpha Alpha8; HDR textures are ETC2_RGBA8.
- ETC_RGB4 needs power-of-two sides even without mips (20x12: RGB24 uncompressed, alpha dropped,
  while ETC2 compresses it); in the Linear project an sRGB-flagged ETC_RGB4 (normal maps too,
  whose flag stays on) is ETC2_RGB, a linear-flagged one stays ETC_RGB4.
- Compressor quality Fast (0), Normal (50) and Best (100) select the project's Fast, Normal and
  Best ETC compressors (EditorSettings; etcpak, ETCPACK Fast and Etc2Comp Best by default), whose
  blocks differ.
- `Texture2D.Compress` gives ETC2_RGBA8 for colour formats with alpha and ETC2_RGB for RGB24. Direct
  R8/RG16 textures give EAC_R/EAC_RG; copied imports named R8/RG16 may actually be RGB24 on this target.
- `SystemInfo.SupportsTextureFormat` is true for ETC and EAC formats, false for ASTC.

## Documented (Unity 6000.0 Scripting API)

- [Texture2D.Compress](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Compress.html):
  DXT1 without alpha, DXT5 with alpha, R8 to BC4, RG16 to BC5; `highQuality` dithers; nothing
  happens for a compressed texture.
- [Graphics.CopyTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.CopyTexture.html):
  region coordinates and sizes of block formats must be multiples of the block size; CPU data is
  copied when both textures are readable, but not for a region of a compressed texture.
- [Texture2D.LoadRawTextureData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html):
  the data must fill the whole mip chain in the texture's format, else it fails.
- [Texture2D.GetPixels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.GetPixels.html):
  needs `isReadable`; compressed textures are decompressed.

## Import encoder controls

The additional BC controls cover flat colour/channels, alpha edges, linear colour, normals,
rectangular levels, 20x12 without mips, 30x18 with mips, and qualities 0, 49, 50, 99 and 100.
`block_report.py` prints exact blocks / total for every compressed texture and format, alongside
all raw differences from `compare.py`. A different format, size, mip count or raw-data length
counts as zero matching blocks and is identified explicitly.

| Source, Unity 6000.0 | Contract or observation |
|---|---|
| [TextureImporterPlatformSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporterPlatformSettings.html) | An enabled platform override replaces the default platform parameters. `NoiseBC7Q49` has default quality 100 and an enabled Standalone override of 49; `NoiseHighDisabledOverride` disables the same override and uses default quality 100. Their blocks differ, and the disabled case matches NoiseHighQ100. |
| [SetPlatformTextureSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporter.SetPlatformTextureSettings.html) | Quality 0, 50 and 100 name Fast, Normal and Best. No compressed-block identity is promised. |
| Editor reference, added BC controls | BC7 changes at qualities 50 and 100; BC6H uses its Fast setting at 0, Best at 100 and Normal for intermediate values. These encoder choices are measured for 6000.0.71f1 only. |

The measured encoder mechanism is selected through unbound-compat. Other releases retain the
previous ISPC approximation; they have no exact-block claim. The default BC7 encoder is bc7e;
the alternate ISPC setting remains an approximation. Both encoders' upstream licences allow
source redistribution. No proprietary encoder library is required by an Unbound build.

Measured import block totals, Gamma / Linear, including every mip:

| Format | Exact / total, Gamma | Exact / total, Linear |
|---|---:|---:|
| BC4 | 2417 / 2417 | 2417 / 2417 |
| BC5 | 1149 / 1149 | 1149 / 1149 |
| BC6H | 2371 / 2401 | 2387 / 2401 |
| BC7 | 6218 / 6278 | 6218 / 6278 |

BC7 residuals are in CutoutBC7, FlatBC7 and SoftHigh. The largest level-0 channel difference
is 195/255 in CutoutBC7, and a lower FlatBC7 level reaches 109/255. These remain unresolved.
BC4/BC5 match every current import block. BC6H retains
encoding differences and its existing CPU float-read limitation. The comparator lists every
raw discrepancy; residual declarations for these imports name exact keys rather than globs.


The channel fitter compares six- and eight-step continuous fits before assigning indices to
rounded ramps. It uses nearest-even lattice quantization, four independent error sums and
reciprocal byte normalization. At most two distinct values use their endpoints directly;
other equal-error fits prefer six steps with fixed extremes. ChannelFitsBC4/BC5 cover narrow
fields, fixed extremes and repeated values. Independent channels, flat inputs, rectangular
levels and all mip levels remain covered. Two ordinary UI/effect textures add 2734/2734 exact
blocks for each of BC4 and BC5 outside the synthetic corpus; their private source images stay
outside the repository. BC7 on those same textures is 2732/2734 exact and remains unresolved.

[Single Channel import settings](https://docs.unity3d.com/6000.0/Documentation/Manual/texture-type-singlechannel.html)
describe red/alpha selection and mip generation. The source filtering rule is a measured
6000.0.71f1 observation: compressed automatic red textures filter mips using the authored
sRGB flag, then store a linear channel. HeightRed and explicit HeightBC4 have identical raw
blocks, while HeightRedLinear differs below mip zero. Uncompressed HeightSingleSrgb and
HeightSingleLinear are identical R8 data and filter linearly. These controls separate filtering
from GPU sampling. The existing measured import profile selects the rule for 2D textures;
other releases retain their prior behavior. Texture cache v30 records the two output changes.


The part's 4397-texture clean-cache import had zero failures throughout. Its first pass was
15.95 s before; final after-runs took 23.07 s and 19.68 s on the shared machine. Alternating
same-build-setting runs then took 15.97/20.34 s before and 19.40/25.46 s after. Total process CPU
time, including the cached second pass, was 92.07/92.91 s before and 90.37/91.60 s after.
The wall times overlap the concurrent-work variation; these runs show no increase in process
CPU cost and do not isolate encoder time. Earlier after-runs were 21.04 s and 14.88 s.


## Runtime encoder and Alpha8 storage

The 6000.0.71f1 profile uses a runtime channel quantizer shared by DXT5 alpha, BC4 and BC5,
and a colour fit with rounded thirds. `highQuality` enables colour dithering. The seed controls
put the first red/green component below the rest of its block, so the bounds' first alpha
sample affects the result. Rectangular RGB24/RGBA32 controls fill each mip independently,
including partial blocks. A constant RGB field with changing alpha exercises the colour fit
in both quality modes. These controls extend the existing noise, gradient and channel cases.
Other releases retain the previous approximation, including its missing highQuality dithering.

Alpha8 keeps one CPU byte per texel and samples (0,0,0,a), as the channel controls record.
It still uses RGBA8 GPU storage. [wgpu-types 27.0.1's texture view descriptor](https://docs.rs/wgpu-types/27.0.1/wgpu_types/struct.TextureViewDescriptor.html)
has no component mapping, and its format set has no alpha-only format. An R8 view supplies
red and an alpha of one. Consequently, a one-channel upload cannot preserve Alpha8 sampling
through the current view API; a shader-side component mapping is a separate renderer change.

Measured runtime blocks (Gamma and Linear are identical):

| Format | Fast exact / total | High quality exact / total |
|---|---:|---:|
| BC4 | 347 / 347 | 347 / 347 |
| BC5 | 347 / 347 | 347 / 347 |
| DXT1 | 350 / 353 | 350 / 353 |
| DXT5 | 621 / 628 | 626 / 628 |

Runtime edge blocks repeat their remaining rows and columns. The 3x2 RGB mip still has an
unresolved colour/padding difference, reaching 35/255 in a decoded channel. Other residuals
are small-mip colour fitting and the flat-RGB fast case (GPU difference at most 2/255).
They remain exact-key residuals; no tolerance has changed. The full comparator records
7283/7467 exact values in each Standalone profile, with 184 listed residuals, and
6635, 6616, 6626 and 6985/7467 in AndroidGamma, AndroidLinear, AndroidETC2 and AndroidASTC,
with 832, 851, 841 and 482 residuals.
Android runtime ETC encoding is unchanged by these encoder changes; direct R8/RG16 runtime
EAC storage remains unresolved. The runtime encoder profile is part of the compression key.

For this runtime change, the ordinary-project clean-cache import processed 4397 textures with
zero failures: 16.65 s before and 18.36 s after. Two subsequent runs took 21.44 s and 26.69 s
on the shared machine while other validation jobs were active. Import encoder selection and
quality are unchanged in this part; these wall times do not isolate encoder cost.

## ASTC profiles and Crunch controls

The AndroidASTC profile selects ASTC by default. Automatic selection is 4x4 at High Quality,
8x8 at Low Quality, and 6x6 at Normal, including Single Channel red and alpha. Normal maps
keep XYZ channels with opaque alpha. Automatic HDR remains ETC2_RGBA8; explicit ASTC HDR
formats use HDR blocks. These choices are measured for 6000.0.71f1.

| Source, Unity 6000.0 | Contract or observation |
|---|---|
| [Texture compression formats](https://docs.unity3d.com/6000.0/Documentation/Manual/class-TextureImporterOverride.html) | ASTC uses 128 bits per block, with six square footprints from 4x4 through 12x12. HDR support depends on the graphics device and API; unsupported devices require a fallback. The probe checks every footprint, partial edge blocks and sub-block mips. |
| [compressionQuality](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporter-compressionQuality.html) | Quality ranges from 0 to 100. The API does not promise block identity. NoiseASTC6Q1/Q49/Q50/Q51/Q99/Q100 distinguish the measured search boundaries. |
| Editor reference, added ASTC controls | Standard LDR quality 0 at 4x4/5x5/6x6/8x8 uses ISPC Fast. Other qualities, 10x10/12x12, HDR and Single Channel use Arm astc-encoder 4.3.0, with effort 10 below quality 50, 60 at 50, and 98 above 50. Single red swizzles RRR1; single alpha uses 000A. HDR inputs are quantized to half precision before encoding. |

The complete Fast C interface packaged with intel_tex_2 remains in use for that slice;
[ISPCTextureCompressor](https://github.com/GameTechDev/ISPCTextureCompressor) is MIT licensed.
The other measured profiles use the vendored [Arm astc-encoder 4.3.0](https://github.com/ARM-software/astc-encoder/tree/4.3.0),
under Apache-2.0. Its dependency notes record the context-local settings and shared-table fixes.
Mixed-quality controls verify that earlier textures cannot alter a later encoder's search settings.
The same Compat flag selects the measured ASTC mechanism. Other releases retain diagnosed
uncompressed storage; no exact-block claim applies to them.

Padding clamps the edge texel, including sub-block mips. GetPixels32 returns actual mip dimensions.
Tiny 2x2 and mipless 20x12/30x18 imports compress; non-power-of-two mip chains use RGBA32, including opaque and HDR sources. The fallback
filters HDR radiance before clamping each level to bytes, without HDR gamma conversion.
Constructors accept odd dimensions. Distinct source/destination copy controls verify matching
5x5 footprints copy CPU blocks, while 5x5-to-6x6 leaves the destination unchanged. HDR constructor,
raw-view and mip controls retain values above one and alpha, including divergent writes of red
4 in the first mip and red 6 in another mip. HDR GPU fallbacks retain float radiance, using RGBA16F for initial imports and RGBA32F for
runtime block uploads; LDR uses RGBA8. Neither path applies a second lossy encoding. Native
ASTC upload remains a renderer follow-up.

The CPU decoder uses the same Arm library, with the linear LDR profile even for an sRGB texture.
It tolerates arbitrary raw payloads; invalid blocks produce the codec's error colour. Deterministic
arbitrary-payload tests cover malformed data. HDR reads decode to float; byte reads clamp to bytes.
Per-thread decoder contexts are cached by footprint/profile, and HDR GetPixel writes into caller-owned
storage. The previous byte decoder and its separate mode validator have been removed.

The normal-map control also records a Standalone RGB24 override becoming DXT5, while Default
RGB24 imports stay RGB24. Explicit Single Channel RGBA32 preserves all four source channels.
Explicit colour formats keep their authored sRGB flag. In a Gamma project, a Default 2D HDR
source encodes colour to gamma independently of that flag, then stores a linear HDR format;
HdrRGBAFloat and HdrBC6H distinguish source preparation from compressed decoding.
Texture cache v33 includes these ASTC and source-processing changes.

Crunch controls cover DXT1 and DXT5 at qualities 0, 50 and 100, with ETC_RGB4Crunched and
ETC2_RGBA8Crunched Android counterparts, gradients, noisy alpha and flat colour. Crunch encoding
and archive storage remain unresolved. The [public Crunch fork's ZLIB licence](https://github.com/Unity-Technologies/crunch/blob/unity/license.txt)
permits redistribution; this is an implementation follow-up. The importer keeps uncompressed
pixels and diagnoses that difference. block_report.py counts the missing logical GPU blocks
from dimensions and mip levels, rather than treating archive bytes as blocks. If two archives
of the same format differ, their per-block exactness needs a transcoded comparison; the report
labels its zero verified lower bound instead of asserting every decoded block differs.


Measured ASTC import blocks, including every mip:

| Footprint / importer quality | AndroidGamma, AndroidLinear, AndroidETC2 | AndroidASTC default profile |
|---|---:|---:|
| LDR 4x4, 0 | 619 / 686 | 1171 / 1372 |
| LDR 4x4, 50 and 100 | no cases | 1029 / 1029 at each quality |
| LDR 5x5, 0, 50 and 100 | 241 / 241 at each quality | 241 / 241 at each quality |
| LDR 6x6, 0 | 1408 / 1409 | 2066 / 2101 |
| LDR 6x6, 1, 49, 51 and 99 | 173 / 173 at each quality | 173 / 173 at each quality |
| LDR 6x6, 50 | 1557 / 1557 | 4328 / 4328 |
| LDR 6x6, 100 | 1211 / 1211 | 1557 / 1557 |
| LDR 8x8, 0 | 113 / 114 | 201 / 202 |
| LDR 8x8, 50 | no cases | 176 / 176 |
| LDR 8x8, 100 | 88 / 88 | 88 / 88 |
| LDR 10x10, 0, 50 and 100 | 73 / 73 at each quality | 73 / 73 at each quality |
| LDR 12x12, 0, 50 and 100 | 53 / 53 at each quality | 53 / 53 at each quality |
| HDR 4x4, 0, 50 and 100 | 343 / 343 at each quality | 343 / 343 at each quality |
| HDR 5x5, 0, 50 and 100 | 241 / 241 at each quality | 241 / 241 at each quality |
| HDR 6x6, 0 and 100 | 173 / 173 at each quality | 173 / 173 at each quality |
| HDR 6x6, 50 | 350 / 350 | 350 / 350 |
| HDR 8x8, 0, 50 and 100 | 88 / 88 at each quality | 88 / 88 at each quality |
| HDR 10x10, 0, 50 and 100 | 73 / 73 at each quality | 73 / 73 at each quality |
| HDR 12x12, 0, 50 and 100 | 53 / 53 at each quality | 53 / 53 at each quality |

Single Channel controls include varying red and alpha, three qualities, an authored sRGB flag,
and both automatic and explicit formats. All their blocks match. HDR includes negative, infinite
and NaN source values, partial edge blocks, and float reads above one. Non-power-of-two Single
Channel and HDR controls verify the RGBA32 fallback separately from compression.

The remaining probe fitting differences are in the existing ISPC Fast slice: opaque gradients,
one Flat6 block and one Noise8 block. Flat6 reaches 88/255 decoded channel error. The previous
byte-decoder differences, including arbitrary constructor payloads, are resolved in this corpus.
Four HDR pixel strings in the Gamma Android profiles and nine in AndroidLinear differ only in
Mono/CoreCLR decimal midpoint formatting. Their numbers parse to identical float32 values;
raw blocks, byte reads and GPU samples match. They remain exact-key residuals without changing
the comparator or its tolerance.

Two ordinary UI/effect images, captured twice on the GPU outside the repository, give:

| LDR footprint | Quality 0 exact / total | Quality 50 exact / total | Quality 100 exact / total |
|---|---:|---:|---:|
| 4x4 | 1855 / 2734 | 2734 / 2734 | 2734 / 2734 |
| 5x5 | 1508 / 1834 | 1834 / 1834 | 1834 / 1834 |
| 6x6 | 1087 / 1314 | 1314 / 1314 | 1314 / 1314 |
| 8x8 | 613 / 688 | 688 / 688 | 688 / 688 |
| 10x10 | 484 / 484 | 484 / 484 | 484 / 484 |
| 12x12 | 348 / 348 | 348 / 348 | 348 / 348 |

The ordinary images' uncompressed source controls match at every mip. These results extend the
measured content; the Fast residuals show why the probe's exact 5x5 result is not a universal claim.
The part's clean-cache import of 4397 ordinary-project textures had zero failures, 19.96 s before
and 18.86 s after, with total process CPU including the cached second pass 92.19 s before and
90.08 s after. An earlier after-run took 17.07 s and 83.51 s CPU. Shared-machine wall time does not isolate encoder cost. This Standalone workload
does not measure the added ASTC searches, whose Normal/Best settings perform more work than
retaining uncompressed pixels.

Crunch storage is absent for 0/1029 DXT1Crunched and 0/1372 DXT5Crunched logical blocks in each
Standalone profile. AndroidGamma, AndroidETC2 and AndroidASTC have 0/1029 ETC_RGB4Crunched
and 0/1372 ETC2_RGBA8Crunched; AndroidLinear has only ETC2_RGBA8Crunched, 0/2401. Quality-specific counts remain in block_report.py's output. The part's ordinary-project
clean-cache import was 20.58 s before and 21.52 s after, 4397 textures and zero failed imports.

The ASTC_5x5 region controls distinguish 5-aligned coordinates and sizes from 4-aligned ones.
The source has CPU green/GPU red and the destination CPU yellow/GPU blue before the call.
The 5-aligned region changes only the destination GPU; the 4-aligned region changes nothing.
The destination's CPU blocks stay yellow in both controls.
