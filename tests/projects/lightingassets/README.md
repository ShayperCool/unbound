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

Matches every recorded value, including per-pass `ShaderVariant` constructor checks. Frames: the lit and
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

## Per-pass variant validation

`VariantSets.shader` and `variant-sets` records measure global/local `multi_compile` and
`shader_feature`, dynamic branches, fog choices, duplicate keywords, empty/null entries,
placeholders and two distinct passes of the same type. A combination must belong to one
pass, with at most one choice from each declaration. `_` is a choice, so it conflicts with
a named choice in its declaration; empty/null entries select nothing. Stereo keywords
absent from a program are rejected even though they appear in the shader's keyword space.
Two independent 6000.0.71f1 GPU Editor captures agree, with all older values unchanged.
The [6000.0 constructor contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderVariantCollection.ShaderVariant-ctor.html)
specifies ArgumentException for a missing variant, shader or pass type. Exact messages and
choice rules above are reference observations, not a guarantee for other releases.

Choices are read by the shader-declarations provider under the compatibility profile's
keyword rules. Missing/unknown choices retain the older shader-wide check, including older
hosts, serialized built-in shaders, generated surface passes and fixed-function passes.
Built-in light-pass shorthand declarations currently constrain membership only; their
combination restrictions remain open. Explicit declarations and fog sets constrain choices.
The reply is session metadata, not a disk cache or an importer format.

## Shader pipeline warm-up (Q134)

The Unity 6000.0 [WarmUp contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderVariantCollection.WarmUp.html) prepares every collection variant, while [WarmUpProgressively](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderVariantCollection.WarmUpProgressively.html) prepares a bounded number and returns whether the collection is complete. The docs warn that DX12, Vulkan and Metal may need additional work when vertex layouts or render-target configurations differ. [WarmupAllShaders](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Shader.WarmupAllShaders.html) covers all loaded shader variants; its scope is wider than one collection.

Two identical Unity 6000.0.71f1 Linux OpenGL Core captures add sixteen state records without replacing the previous 333 records or image references. Observed details: empty progressive calls return true while leaving the collection cold; full WarmUp marks even an empty collection warm; mutation resets progress; a large progressive count saturates without integer overflow. Global warm-up leaves every collection's counters unchanged.

The cold scenarios use a separate empty scene and remove preloaded shader collections in the project copy. They warm five explicit variants, then draw four keyword combinations and command-buffer plus standalone mesh-instancing controls (with a distinct color under INSTANCING_ON) into an explicitly bound 4x4 target with depth; the camera disables HDR and MSAA to keep the attachment configuration fixed. Each scenario has two byte-identical Editor captures. The engine additionally queries GPU program and pipeline counts for the requested shader (camera helper shaders are outside the collection): both must grow during collection warm-up and remain unchanged across all six first draws and repeated warm-up. The partial progressive call must also create GPU objects immediately, before the remaining variants are requested. The gate rejects implementations that only update counters. Counts are queried only by the probe.

Collection warm-up prepares GPU modules and real render pipelines for the current attachments and render state, including triangle, line, strip and point topologies and ordinary or mirrored culling. Command-buffer and standalone mesh instance calls share the replay vertex program; warm-up prepares that program for instancing variants, along with the raw procedural form when available. A later layout, material render-state override or attachment configuration can require another pipeline, as covered by the documented driver limitation. Completed collections return before allocating or calling the renderer. Partial calls reuse one flattened plan, and only successful preparation advances a variant.

Unbound explicitly rejects Shader.WarmupAllShaders until enumeration of every loaded shader's complete variant space is supported, including built-in and generated passes. Complete loaded-variant enumeration remains a follow-up. Before the GPU exists, preparation requests keep their position among the existing startup uploads and commands; installation prepares them before later draws. The startup control calls WarmUp during scene initialization, then verifies that GPU programs and pipelines are already present before its first draw. Shader compilation errors use the same error-program fallback as drawing.

The cold global scenario retains the exact strict difference for that exception, verifies unchanged collection counters and compares every rendered pixel normally. It cannot accept a silently ignored global call or any additional difference.

State-only recording uses `run.py record --warmup-mode state --label NAME` and `run.py publish --warmup-mode state NAME-1 NAME-2`; publication appends only new state rows and preserves the original image references. The state block runs after the original full-probe image captures.

Commands: `run.py record --warmup-mode collection --label NAME` (likewise progressive/all); `run.py publish --warmup-mode collection NAME-1 NAME-2`. The normal `run.py gate` also runs all four cold scenarios; it never launches the Editor. Results remain under Library/Unbound or the configured cache.
