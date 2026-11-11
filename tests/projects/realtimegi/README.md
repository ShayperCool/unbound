# Precomputed realtime global illumination probe

Unity 6000.0.71f1, built-in render pipeline, linear color space. What precomputed realtime GI
(Lighting window: Realtime Global Illumination) draws in Play mode, case by case, with
`DynamicGI.synchronousMode` on. Unity documentation (6000.0):
[Realtime GI](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html),
[DynamicGI](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DynamicGI.html)
([SetEnvironmentData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DynamicGI.SetEnvironmentData.html),
[UpdateEnvironment](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DynamicGI.UpdateEnvironment.html)),
[RenderSettings.ambientIntensity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-ambientIntensity.html),
[Renderer.UpdateGIMaterials](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer.UpdateGIMaterials.html),
[LightmapSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightmapSettings.html),
[LightProbes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbes.html).

## Content

`Assets/Editor/RealtimeGISetup.cs` (`run.py setup`) makes three scenes and precomputes them
(realtime GI on, baked GI off, realtime resolution 2, realtime environment lighting):
an open-fronted room (white floor, ceiling, back wall and block, red left and green right walls,
all Contribute GI static), an emissive panel (Standard, realtime emissive), a realtime point light
with hard shadows, a disabled realtime spot light, a light probe group (2x2x2 and a center probe)
and a dynamic probe-lit sphere. Ambient: flat color, no skybox.

- `Room`: non-directional, Albedo Boost 1, Indirect Intensity 1.
- `RoomScaled`: Albedo Boost 2, Indirect Intensity 1.5.
- `RoomDirectional`: directional realtime lightmaps.
- `RoomSystems`: as `Room`, with the left and right walls and the panel given Lightmap Parameters
  of their own (the defaults with System Tag 1): two systems in one atlas, each reading the
  other's values (`run.py setup-systems` adds this scene alone).

`Assets/Scripts/RealtimeGIProbe.cs` (with `REALTIME_GI_OUT` set) changes one input at a time and
records, per case:

- `<case>.atlas` (and `<case>.dir` for directional scenes): every realtime lightmap texel a
  probe renderer covers, read with `Assets/Shaders/RealtimeGIDump.shader` (each renderer drawn at
  its realtime lightmap UVs, each texel read at its center); int32 width, int32 height, then
  RGBA half floats; alpha 0 marks texels no renderer covers.
- `values.jsonl`: `DynamicGI.isConverged`, `LightmapSettings.lightmapsMode`, each renderer's
  `realtimeLightmapIndex` and `realtimeLightmapScaleOffset`, `LightProbes.GetInterpolatedProbe` at
  the probe positions, the sphere and one more point, and `RenderSettings.ambientProbe`.
- `<case>.png`: the main camera at 192x108.

Cases: `room-base` (plus `room-lightprobes`: the baked probe count and coefficients);
`room-moved` (the point light moved; `room-moved-next-frame`: `DynamicGI.isConverged` on the frame
after the move, false); `room-intensity` (intensity 2 to 4); `room-bounce`
(`Light.bounceIntensity` 3); `room-indirectscale` (`DynamicGI.indirectScale` 2, then restored;
its default is recorded per scene); `room-emission-call` (the panel material's emission changed
and `Renderer.UpdateGIMaterials` called); `room-setemissive` (`DynamicGI.SetEmissive` on the
panel); `room-setemissive-then-call` (another material emission change and
`UpdateGIMaterials` after it); `room-albedo-nocall` and `room-albedo-call` (the red wall turned
blue, before and after `UpdateGIMaterials`); `room-env-nocall` and `room-env-call` (the ambient
color changed, before and after `DynamicGI.UpdateEnvironment`); `room-sky` (a procedural skybox
as the ambient source, with `UpdateEnvironment`), `room-sky-nocall` and `room-sky-call` (its
exposure changed, before and after the call); `room-pointoff`; `room-spot`; `room-spot-behind`
(the spot light moved to light the block from its side: the right wall behind the block is in its
shadow); `room-directional` and `room-directional-turned` (the spot light off, a realtime
directional light with shadows added through the open front, at Euler angles (40, 25) and (57,
-33)); `room-directional-noshadows` (that light replaced by a new one without shadows at (40,
25)); then the environment as the only light (no light on, the panel's emission set to black with
`DynamicGI.SetEmissive`): `room-gradient` (a gradient ambient), `room-sky-only` (the procedural
skybox as the ambient source), `room-sky-only-intensity` (ambient intensity 2;
`room-sky-only-intensity-half` records `RenderSettings.ambientProbe` at 0.5 within the frame),
`room-sky-sun` (intensity 1 and a directional light of bounce intensity 0 at (40, 25), without
`UpdateEnvironment`) and `room-sky-sun-turned` (that light at (15, -120)); once the sky without
the light has settled, `room-envdata` (`DynamicGI.SetEnvironmentData` with a cube whose faces
differ and grow brighter along their columns and rows), `room-envdata-intensity` (the ambient
intensity then set to 2), `room-envdata-update` (the data set again, then `UpdateEnvironment`)
and `room-envdata-errors` (what the call throws for an array of 100 floats and for none); with
the intensity still 2, `room-sky-none` (the skybox material removed, the ambient mode still
Skybox), `room-gradient-intensity` (the gradient again) and `room-ambient-custom` (a custom
ambient mode with another ambient color); `roomscaled-base`,
`roomscaled-moved`, then from bounce intensity 3 `roomscaled-indirectscale` (indirect scale 2,
bounce intensity 1) and, from bounce intensity 3 again, `roomscaled-threshold0` (the same with
`DynamicGI.updateThreshold` 0; restored after it); `roomdirectional-base`,
`roomdirectional-moved`, `roomdirectional-indirectscale`, `roomdirectional-threshold0` (as in
RoomScaled); `roomsystems-base`, `roomsystems-moved`, `roomsystems-indirectscale`,
`roomsystems-threshold0` (the same). A scene whose renderers use several realtime lightmap atlases
writes `<case>-a<index>.atlas` for every atlas after the first. The default of
`DynamicGI.updateThreshold` is recorded.

## What the Editor does (this capture)

Unity updates realtime GI over several frames on worker threads; even with
`DynamicGI.synchronousMode` the first frames after a change differ between runs. Each case
therefore waits 300 frames, then until `DynamicGI.isConverged`, then 60 frames: the references
are the converged lighting, and two independent captures are byte-identical. Cases that depend
on update timing are not recorded: a material emission change without `UpdateGIMaterials` (the
Editor picks it up in some runs and not in others) and `updateThreshold` above its default
(changes below the threshold wait for later ones, so the converged output depends on timing).

Observations (not documented guarantees):

- Light probes precomputed for realtime GI only: `LightProbes.count` is the number of probes and
  `bakedProbes` is empty.
- Point and spot lights light realtime GI without their shadows (`room-spot-behind`: the wall
  behind the block is lit); a directional light's realtime GI is shadowed (`room-directional*`),
  also when the light has no shadows (`room-directional-noshadows`).
- `DynamicGI.indirectScale` starts as the scene's Lighting Settings Indirect Intensity (1, and 1.5
  in `RoomScaled`); 2 makes the realtime lightmap texels 2.00 to 2.07 times those of
  `room-intensity`. `updateThreshold` defaults to 1.
- `DynamicGI.updateThreshold` decides converged output: at 1, parts of the lightmap whose inputs
  change by less than 1% keep their earlier values, so `room-indirectscale`, converged from the
  brighter `room-bounce`, keeps some of it; at 0 (`roomscaled-threshold0`, against
  `roomscaled-indirectscale` from the same state) every part is updated until the output stops
  changing.
- Doubling the light intensity raises the texels about fourfold and `Light.bounceIntensity` 3
  about tenfold: more than proportionally, as when the light's color is scaled before its
  conversion to linear (the project does not use linear light intensity).
- After `DynamicGI.SetEmissive`, a material emission change is ignored even with
  `Renderer.UpdateGIMaterials` (the color set with `SetEmissive` stays). Without `SetEmissive`,
  the change shows after `UpdateGIMaterials`.
- An albedo change is ignored until `Renderer.UpdateGIMaterials`, as documented.
- Realtime GI picks up an ambient color change and a skybox exposure change without
  `DynamicGI.UpdateEnvironment` (the atlases and frames do not change with the call); the call is
  what updates `RenderSettings.ambientProbe` after the skybox change.
- The ambient intensity scales the realtime GI of a skybox (`room-sky-only-intensity`) and of
  nothing else, as documented: the gradient's lightmap at intensity 2
  (`room-gradient-intensity`) equals `room-gradient` within 0.002; a skybox ambient mode
  without a skybox material (`room-sky-none`) and a custom ambient mode (`room-ambient-custom`)
  give the ambient color's light, unscaled.
- A directional light changes the sky's realtime GI by its direction, added and turned without
  `UpdateEnvironment` (`room-sky-sun`, `room-sky-sun-turned`), while `RenderSettings.ambientProbe`
  stays as the last call left it.
- `DynamicGI.SetEnvironmentData` replaces the environment (`room-envdata`); the ambient intensity
  changed afterwards brings the sky back, as documented (`room-envdata-intensity` equals
  `room-sky-only-intensity` within 0.005), and so does `UpdateEnvironment`
  (`room-envdata-update`). An array of another length throws ArgumentException with the message
  recorded in `room-envdata-errors`; none throws ArgumentNullException.
- In an earlier capture the data was set one frame after the directional light was destroyed and
  did not show: the lightmap was the sky's. The probe therefore waits for the sky to settle
  before the call.
- `RenderSettings.ambientProbe` with a skybox follows the ambient intensity without
  `UpdateEnvironment`: 0.2140 times the intensity 1 probe at 0.5 and 4.5948 times at 2 (the
  intensity taken as an sRGB-encoded value). In the custom ambient mode it is the probe last
  derived from the sky, at intensity 1.
- Three captures were recorded for this reference: two are byte-identical in every file and are
  published; the third differs from them in `roomsystems-base` and `roomsystems-moved` only (up
  to 0.02 in 1505 and 221 of 5070 lightmap values).

## Unbound

Unbound draws precomputed realtime GI for these scenes (docs/REALTIME_GI.md): it makes the
renderers' realtime lightmap UVs, renders their meta passes and updates the systems each frame.
The gate fails on state values (cases, realtime lightmap indices and scale/offset, lightmaps mode,
probe count and baked coefficients, `isConverged`, API defaults, what `SetEnvironmentData`
throws, and which atlas texels the renderers cover) and on the realtime lightmaps of every case
(at most 0.5% of their values outside the tolerance, for
the isolated texels docs/REALTIME_GI.md explains), and reports the other lighting values
(`realtimegi: 0 failing state values, 0 failing gated lighting values, N failing lighting
values`); `compare.py --strict` gates those too. Every failing value, and every allowed isolated
one, is written to `compare-details.txt` in the run directory. Light probes are lit by realtime GI
(their coefficients are reported: a few small higher-band ones differ in the brightest case);
directional output is not supported yet (its values differ), and `RenderSettings.ambientProbe`
in the custom ambient mode differs (reported).

## Running

From the repository root, with the release in `UNBOUND_PROBE_UNITY_VERSION` (default 6000.0.71f1):

```sh
python3 tests/projects/realtimegi/run.py setup --label setup1     # only to remake the scenes and precompute
python3 tests/projects/realtimegi/run.py record --label native1   # and native2
python3 tests/projects/realtimegi/run.py publish native1 native2
python3 tests/projects/realtimegi/run.py gate --label gate1       # the land gate (gate.json)
```

`gate` runs `$UNBOUND_CLI` (else `$UNBOUND`, else `target/release/unbound`) on a copy of the
project and compares with `Reference/unity-<release>/`. Run output goes to
`$XDG_CACHE_HOME/unbound/validation/runs/realtimegi/<label>/`.
