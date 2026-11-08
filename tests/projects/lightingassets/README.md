# Lighting Settings and shader variant collections probe

What `LightingSettings`, `UnityEditor.Lightmapping`'s scene settings and `ShaderVariantCollection`
answer in Play mode, and which Lighting Settings change the frames. Built-in render pipeline,
linear color space.

Scripting API pages (6000.0): [LightingSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightingSettings.html)
("settings for precomputing lighting data"; most members "(Editor only)"),
[Lightmapping.lightingSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Lightmapping-lightingSettings.html)
("Will throw an exception if it is null"; no exception type or setter behaviour documented),
[ShaderVariantCollection](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderVariantCollection.html)
and its WarmUp/WarmUpProgressively pages (prewarming; fully supported on DX11 and OpenGL, partly on
DX12, Vulkan and Metal). Nothing documents setter limits, keyword normalization or warm-up counts:
those below are observations of this capture, Unity 6000.0.71f1 on Linux (GPU Editor, batchmode).

## Content

`Assets/Editor/LightingAssetsSetup.cs` made `Assets/Settings` and `Assets/Scenes` (and the Graphics
settings' preloaded collection) with `run.py setup`:

- `Plain`: no Lighting Settings, no lighting data; a holder references the assets read.
- `Lit`: static geometry baked with the CPU lightmapper (non-directional, baked indirect, no
  compression), a mixed directional and a baked point light. After the bake its `Lit.lighting` was
  changed without baking again: baked GI off, realtime GI on, subtractive, directional,
  compressed, other scales. The probe also changes its settings at runtime and assigns others.
- `Realtime`: a box lit by a realtime point light, Enlighten Realtime GI precomputed;
  `RealtimeOff` the same without GI.
- `Settings/Custom.lighting`: every property away from its default (assigned to no scene).
- Collections: `Warmup` (made with the API), `Authored` (written as a file: a shader without
  variants, a missing shader, keywords out of name order, a duplicate; the preloaded one),
  `Dupes` and `Missing` (files).
- `Assets/Shaders/Variants.shader`: an untagged pass with three keyword sets, a ForwardBase pass
  with the light pass set and a local keyword, a ShadowCaster pass.

`Assets/Scripts/LightingAssetsProbe.cs` (with `LIGHTING_ASSETS_OUT` set) writes `values.jsonl`
(enums as numbers) and PNGs of the main camera (320x180) after loading each scene.

## What the Editor does

- Lighting Settings are bake inputs in Play mode. `lit-authored`, `lit-edited` (settings changed
  at runtime) and `lit-assigned` (other settings assigned) are the same image; lightmaps, the
  lightmaps mode and each light's baking output stay the lighting data's. Enlighten Realtime GI
  precomputed into the lighting data does change the frames (`realtime-*` against
  `realtimeoff-red`); realtime GI enabled without precomputed data changes nothing.
- `Lightmapping.lightingSettings` throws `System.Exception` ("Lightmapping.lightingSettings is
  null. Please assign it to an existing asset or a new instance. ") without settings; assigning
  null takes them away; setting `Lightmapping.bakedGI` without settings gives the scene new ones;
  `lightingSettingsDefaults` is one object with a new object's values.
- Setters clamp and round (power-of-two sizes and sample counts, ranges); `realtimeEnvironmentLighting`
  reads true without baked GI, else realtime GI and the setting.
- Collections: queries keep the shader's declared keywords, once, in name order; assets keep their
  keywords as written. Any change makes all variants cold again; preloaded collections are not
  warmed when Play mode starts. The `ShaderVariant` constructor throws `ArgumentException` for a
  null shader, a pass type the shader (with its Fallback) lacks, and keywords that are no variant
  of that pass.

## Unbound

Matches every value except three `ShaderVariant` constructor checks that need each pass's own
keyword sets (a keyword of another pass, two keywords of one `multi_compile` set): Unbound
checks the shader's whole keyword space (`compare.py` RESIDUALS). Frames: the lit and
`realtimeoff-red` frames within a mean difference of 0.25 (limit 0.5); `realtime-red` and
`realtime-blue` differ (Unbound does not draw Enlighten Realtime GI and warns once per scene;
`unbound doctor` lists scenes whose loaded LightingData has realtime lightmap contributors,
with their referenced Lighting Settings. Settings enabled without such output are informational).

## Running

From the repository root, with the release in `UNBOUND_PROBE_UNITY_VERSION` (default 6000.0.71f1):

```sh
python3 tests/projects/lightingassets/run.py setup --label setup1     # only to remake the assets
python3 tests/projects/lightingassets/run.py record --label native1   # and native2
python3 tests/projects/lightingassets/run.py publish native1 native2
python3 tests/projects/lightingassets/run.py gate --label gate1       # the land gate (gate.json)
```

`gate` runs `$UNBOUND_CLI` (else `$UNBOUND`, else `target/release/unbound`) on a copy of the
project and compares with `Reference/unity-<release>/`. Run output goes to
`$XDG_CACHE_HOME/unbound/validation/runs/lightingassets/<label>/`.

## Doctor runtime-impact decision (Q136)

Unity 6000.0's [Enlighten workflow](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html)
says runtime indirect lighting uses precomputed visibility information.
[LightingSettings.realtimeGI](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightingSettings-realtimeGI.html)
enables the system for the scene, but does not itself supply that information. The capture above
shows settings edits do not replace lighting output. Doctor therefore checks the scene's actual
LightingData reference and its dynamic lightmap contributors, using the runtime readers. It keeps
scenes with precomputed output unsupported even if their settings were changed after the bake.
Unassigned settings and scenes with the switch on but no output are informational. Broken
references remain incomplete checks. This is a static inventory of serialized scene references;
lighting data assigned later by game code is outside its scope.
