# Precomputed realtime global illumination probe

Unity 6000.0.71f1, built-in render pipeline, linear color space. What precomputed realtime GI
(Lighting window: Realtime Global Illumination) draws in Play mode, case by case, with
`DynamicGI.synchronousMode` on. Unity documentation (6000.0):
[Realtime GI](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html),
[DynamicGI](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DynamicGI.html),
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
`room-moved` (the point light moved); `room-intensity` (intensity 2 to 4); `room-bounce`
(`Light.bounceIntensity` 3); `room-indirectscale` (`DynamicGI.indirectScale` 2, then restored;
its default is recorded per scene); `room-emission-call` (the panel material's emission changed
and `Renderer.UpdateGIMaterials` called); `room-setemissive` (`DynamicGI.SetEmissive` on the
panel); `room-setemissive-then-call` (another material emission change and
`UpdateGIMaterials` after it); `room-albedo-nocall` and `room-albedo-call` (the red wall turned
blue, before and after `UpdateGIMaterials`); `room-env-nocall` and `room-env-call` (the ambient
color changed, before and after `DynamicGI.UpdateEnvironment`); `room-sky` (a procedural skybox
as the ambient source, with `UpdateEnvironment`), `room-sky-nocall` and `room-sky-call` (its
exposure changed, before and after the call); `room-pointoff`; `room-spot`; `roomscaled-base`,
`roomscaled-moved`; `roomdirectional-base`, `roomdirectional-moved`. The default of
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
- `DynamicGI.indirectScale` starts as the scene's Lighting Settings Indirect Intensity (1, and 1.5
  in `RoomScaled`); 2 doubles the realtime lightmap texels. `updateThreshold` defaults to 1.
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

## Unbound

Unbound draws precomputed realtime GI for these scenes (docs/REALTIME_GI.md): it makes the
renderers' realtime lightmap UVs, renders their meta passes and updates the systems each frame.
The gate fails on state values (cases, realtime lightmap indices and scale/offset, lightmaps mode,
probe count and baked coefficients, `isConverged`, API defaults, and which atlas texels the
renderers cover), which match, and reports lighting values (`realtimegi: 0 failing state values,
N failing lighting values`); `compare.py --strict` gates those too. Every failing value is written
to `compare-details.txt` in the run directory. The atlases of the point-light cases match but for
the residuals listed in docs/REALTIME_GI.md; light probes, the spot light case and the skybox
cases are not supported yet (the lighting values of those differ).

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
