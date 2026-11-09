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

Cases: `room-base`; `room-moved` (the point light moved); `room-intensity` (intensity 2 to 4);
`room-bounce` (`Light.bounceIntensity` 3); `room-indirectscale` (`DynamicGI.indirectScale` 2,
then restored); `room-setemissive` (`DynamicGI.SetEmissive` on the panel); `room-emission-nocall`
and `room-emission-call` (the panel material's emission changed, before and after
`Renderer.UpdateGIMaterials`); `room-albedo-nocall` and `room-albedo-call` (the red wall turned
blue, before and after `UpdateGIMaterials`); `room-env-nocall` and `room-env-call` (the ambient
color changed, before and after `DynamicGI.UpdateEnvironment`); `room-threshold-small` and
`room-threshold-big` (`DynamicGI.updateThreshold` 50, intensity +5% then +90%);
`room-pointoff`; `room-spot`; `roomscaled-base`, `roomscaled-moved`; `roomdirectional-base`,
`roomdirectional-moved`.

## What the Editor does (this capture)

Unity updates realtime GI over several frames on worker threads; even with
`DynamicGI.synchronousMode` the first frames after a change differ between runs. Each case
therefore waits 30 frames, then until `DynamicGI.isConverged`, then 60 frames: the references are
the converged lighting, and two independent captures are byte-identical.

Observations (not documented guarantees):

- Doubling the light intensity raises the texels about fourfold and `Light.bounceIntensity` 3
  about tenfold: more than proportionally, as when the light's color is scaled before its
  conversion to linear (the project does not use linear light intensity).
- `DynamicGI.indirectScale` 2 doubles the realtime lightmap texels.
- `DynamicGI.SetEmissive` changes the panel's bounced light at once; a later material emission
  change is not picked up, not even after `Renderer.UpdateGIMaterials` (the color set with
  `SetEmissive` stays).
- An albedo change is ignored until `Renderer.UpdateGIMaterials`, as documented.
- The ambient color change is picked up without `DynamicGI.UpdateEnvironment`; the call changes
  nothing more.
- With `DynamicGI.updateThreshold` 50, a 5% intensity change still updates the lighting under
  `synchronousMode`.

## Unbound

Unbound reads none of the precomputed realtime GI yet: it warns once per scene and draws no
realtime lightmaps, so the atlases, probes and frames differ. The gate compares without a
verdict (`realtimegi: N failing (report)`); `compare.py --strict` fails on every difference.
See [docs/REALTIME_GI.md](../../../docs/REALTIME_GI.md) for the data and the planned solver.

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
