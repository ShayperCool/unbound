# Editor tooling APIs used by 2D content

What 2D tooling packages call in the Editor, recorded in Play mode by `Assets/Editor/ToolApisProbe.cs`
and compared with Unbound:

* `TextureGenerator.GenerateTexture` (the PSD and Aseprite importers' texture step):
  `new TextureGenerationSettings()` and `new TextureGenerationSettings(type)` for ten texture
  types (every setting, platform setting and field), `new TextureImporterSettings()`,
  `ApplyTextureType` for each type (also on settings that already differ), `CopyTo` and `Equal`,
  `new TextureImporterPlatformSettings()` and its `CopyTo`, `new SourceTextureInformation()` and
  `new SpriteImportData()`. Then generated textures from a 64x32 (and 37x21) pattern with
  transparent and half-transparent texels, as RGBA32 unless the case says otherwise: default,
  alpha source None, alpha is transparency, linear with point filtering and clamping, a platform
  maximum size of 32, non-power-of-two sizes scaled to the nearest power and kept, not readable,
  automatic compressed and uncompressed formats, a normal map, a single sprite with and without
  sprite data, three sprites in Tight mode (centre, custom pivot with a border and a tessellation
  detail, and a custom outline), one Full Rect sprite, sprite data on a Default texture, and the
  `NativeArray<Color>` overload. Each output records the texture's size, formats, mip count,
  sampler state, flags, every mip's texels (hashed) and four texels by place, the thumbnail, the
  warnings, and each sprite's rect, pivot, border, pixels per unit, packing, bounds, mesh, UVs,
  sprite ID and physics shapes.
* The postprocessor hooks the generation runs: with `enablePostProcessor` an `AssetPostprocessor`
  sees `OnPostprocessTexture` and `OnPostprocessSprites` (with the generated sprites), with
  `assetPath` the settings' and `context` null, and no `OnPreprocessTexture`; without it, none.
  This is the documented contract of
  [TextureGenerationSettings.enablePostProcessor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetImporters.TextureGenerationSettings-enablePostProcessor.html)
  (6000.0), and the case where the flag is off is the control that it changes the result.
* A buffer whose size is not width x height (and a 0x0 source) throws `ArgumentException`.
* `Assets/Layers.psd` (written by `make_psd.py`: three raw layers, the third hidden) imported by the
  `com.unity.2d.psdimporter` 9.1.3 package with its defaults: the objects of the asset, the
  mosaic texture, the two sprites and the prefab's hierarchy. Its `.meta` is the one the Editor
  wrote on the first import (the importer generates the sprite IDs then and keeps them there).
* The Editor's internal build platform list, which 2D Common reads to name a build target's texture
  platform settings: each platform's type, name, group, title, tooltip, visibility, subtarget and
  default target (Standalone's, and which platforms are installed, depend on the machine and are
  left out).
* Which importer `AssetImporter.GetAtPath` gives during an asset's own import (a ScriptedImporter
  of `Data.toolapis`, imported again in Play mode) and afterwards.
* Sprites outside an atlas (`Sprite.Create` of both mesh types): packing; a sprite whose positions
  are rewritten through `SpriteDataAccessExtensions` before and after its UVs are written; the
  materials of a `SpriteRenderer` a script adds and of the PSD prefab's renderers, and under a probe
  render pipeline (`ToolApisPipelineAsset`) whose default 2D material differs: a renderer added then
  and an instance of one added before (the documented use of `RenderPipelineAsset.default2DMaterial`).
* A `Material`'s `m_SavedProperties` through `SerializedObject` (the Visual Effect Graph's material
  settings write their float list this way): the floats, ints, colours and textures of a
  `Sprites/Default` material, and the result of replacing the float list with two entries (one the
  shader does not have) and applying it.
* USS `var()` references whose value has the wrong type for the property (`VarTypes.uss`): a
  length in `border-color` and `color`, a colour in `border-left-width`, beside valid
  declarations, a parent's colour, a less specific rule that would otherwise give the colour, and
  the controls that must keep working: colour names through `var()` and a `var()` fallback.
  The panel uses a constant pixel size of 1 and an empty theme; the panel's missing-theme warning
  is left out of the logs.

Measured with the Unity 6000.0.71f1 macOS Editor in batch mode (two byte-identical captures). The
values depend on neither the OS nor the graphics device (texture import follows the build target,
Standalone on both hosts), so the reference stands for Linux too.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/toolapis/run.py record --label native-a
python3 tests/projects/toolapis/run.py record --label native-b
python3 tests/projects/toolapis/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/toolapis/run.py gate
```

Values compare exactly (tolerance 0). `Packages/packages-lock.json` is the one the Editor wrote: it pins
the indirect dependencies (Burst 1.8.28) the reference was captured with.

Unbound on macOS, 2026-11-18: 3141/3166 values, 0 failures and 26 recorded residuals
(`residuals.json`): the material sheet (not modelled, queued), the `Color` overload's mips (filtered
from bytes; the Editor filters the float colours), and NotEditable on the PSD importer's texture.
Before the engine changes, 612 of the first 2079 values matched.
