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
[directional lightmaps](https://docs.unity3d.com/6000.0/Documentation/Manual/LightmappingDirectional.html),
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
- `RoomCharts`: as `Room`, with four flat panels on the walls and under the ceiling whose
  lightmap UV charts leave holes (`run.py setup-charts` adds this scene alone;
  `RealtimeGISetup.Panel` makes the meshes): `Ring`, a ring and two loose square plates; `RingGrown`,
  the same ring with three; `Holes`, a plate with two holes of different sizes and two loose
  plates; `Arch`, an arch and one. A fifth, `Channels`, lies on the floor: a 2 x 1 m plate
  whose `Mesh.uv3` is a square (the Manual's input of realtime lightmap UVs when a mesh has
  it), its `Mesh.uv2` the plate's own shape.
- `RoomAuto`: as `Room`, every lit renderer with Optimize Realtime UVs (`m_PreserveUVs: 0`, the
  limits of a new renderer), so the precompute makes their charts (`run.py setup-auto` adds this
  scene alone; `RealtimeGISetup.Solid`, `Prism`, `Roof` and `Grid` make the meshes, with
  lightmap UVs that stretch every face to a cell). Ten more shapes stand in the room, none
  turned: `Bar` (a cube of 2 x 0.05 x 0.3), `Wedge` (a right-angled prism), `WedgeTall` (the
  same mesh scaled (1, 1.4, 0.75)), `WedgeInverted` (its triangles wound against its normals),
  `Step` (an L-shaped prism), `RoofFlat` and `RoofSteep` (two quads 10 and 50 degrees to
  either side of level), `Grid` (3 x 2 cells that share their vertices), and Unity's quad and
  plane meshes scaled (1.5, 1, 1) and (0.2, 1, 0.15). Four more have limits of their own:
  `RoofAngleWide` and `RoofAngleTight` (roofs of 20 degrees with Max Angle 30 and 15),
  `StepMid` and `StepNear` (the step's mesh, 1.2 and 0.8 times as long, with Max Distance 0.3
  and 0.15).
- `RoomHoles`: as `Room` (`run.py setup-holes` adds this scene alone), with two panels made as
  `RoomCharts`' are: `TwinHoles` (two holes of one size side by side) and `TwinHolesStacked`
  (the same stacked), each with one plate either hole holds.
- `RoomTurned`: as `RoomAuto`'s room (`run.py setup-turned` adds this scene alone), with
  `SlabTurned` (a cube of 1.3 x 0.2 x 0.9 turned 30 degrees about Y), `BoxTurned` (a cube of
  0.7 x 0.5 x 0.6 turned (20, 40, 10)), `WedgeTurned` (the wedge turned 30 degrees and scaled
  (0.5, 0.4, 0.45)), `Child` (a cube of 0.5 x 0.3 x 0.4 turned 10 degrees about X under `Pivot`,
  turned 45 about Y and scaled 1.5), `SharedTurned` and `SharedFlat` (cubes of 0.35, one turned
  25 degrees about Y), `RoofDistance` (the roof of 20 degrees with Max Distance 0.1),
  `FoldKept` and `FoldIgnored` (a floor and a wall at a right angle that share their edge's
  positions and UVs but not its normals, and a loose quad; the second with Ignore Normals),
  `StepNoDistance` and `StepNoAngle` (the step with Max Distance 0 and with Max Angle 0),
  `BoxSliver` and `BoxPoint` (a box of 0.6 m
  and a triangle of no area a metre away: three points on a line, three at one point), and
  `Many` (11 x 10 loose quads in one plane with Max Distance 0.1).
- `RoomCurved`: as `RoomAuto`'s room (`run.py setup-curved` adds this scene alone), with
  curved and bent parts, each left on its own so that the precompute keeps its lightmap UVs or
  takes its projection: Unity's sphere (`Ball`), cylinder (`Drum`) and capsule (`Pill`); strips
  of a quarter cylinder (`RealtimeGISetup.Strip`, radius and height 1.2 m) whose UVs unroll it
  true to the surface (`Arc`), stretch it along the surface (`ArcStretched`) and fold it onto
  itself (`ArcFolded`); two flat quads 40 degrees apart sharing their edge, unrolled true to
  them (`Bent`), and two of different widths, each stretched over half of a narrow UV strip
  (`BentStretched`); and Scale In Lightmap 4 on the sphere and a cube of 0.6 (`ScaleUp`) and
  0.25 on a 2.4 x 0.1 x 1.6 slab (`ScaleDown`).
- `RoomRounding`: as `RoomAuto`'s room (`run.py setup-rounding` adds this scene alone), with
  shapes whose charts the rounding of the precompute's arithmetic decides: a 1.6 x 0.1 x 1.2
  slab modelled turned 45 degrees about Y (`Slab45`), a roof whose sides lean 20 degrees from the plane through both with Max Angle 20
  (`RoofAtLimit`), a 0.6 x 0.4 x 0.5 box modelled turned 30 degrees about Y away from its mesh's
  origin (`BoxOffOrigin`) and a 2 x 1.2 m quad on a tilted plane whose lightmap UVs are a true
  copy of it turned a quarter (`TiltedQuad`).

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
intensity then set to 2), `room-envdata-update` (the data set again, then `UpdateEnvironment`),
`room-envdata-source` (the data set again, then the ambient mode set to the gradient),
`room-envdata-gradient-intensity` (the data set in that mode, then the ambient intensity set to
3) and `room-envdata-errors` (what the call throws for an array of 100 floats and for none); with
the intensity still 3, `room-sky-none` (the ambient mode Skybox without a skybox material),
`room-gradient-intensity` (the gradient again) and `room-ambient-custom` (a custom
ambient mode with another ambient color); then `DynamicGI.SetEnvironmentData` in the frame of
a change of the ambient source: `room-envdata-after-color` (the flat ambient color changed and
the data set after it), `room-envdata-before-color` (the data set and the color changed after
it) and `room-envdata-after-sky` (the skybox as the source, its exposure changed and the data
set after it); `roomscaled-base`,
`roomscaled-moved`, then from bounce intensity 3 `roomscaled-indirectscale` (indirect scale 2,
bounce intensity 1) and, from bounce intensity 3 again, `roomscaled-threshold0` (the same with
`DynamicGI.updateThreshold` 0; restored after it); `roomdirectional-base`,
`roomdirectional-moved`, `roomdirectional-indirectscale`, `roomdirectional-threshold0` (as in
RoomScaled) and `roomdirectional-dim` (the directional scene with hardly any light: the point
light off, the panel's emission black and an ambient color of (0.02, 0.025, 0.03), so that
almost half of the covered texels receive less than 0.001); `roomsystems-base`, `roomsystems-moved`, `roomsystems-indirectscale`,
`roomsystems-threshold0` (the same). A scene whose renderers use several realtime lightmap atlases
writes `<case>-a<index>.atlas` for every atlas after the first. The default of
`DynamicGI.updateThreshold` is recorded.

At the end of the run, in one frame and as values only: `end-reflection`
(`ReflectionProbe.defaultTexture`'s name, dimension and size and its decode values in a scene
without a sky) and `RenderSettings.ambientProbe` after each of a series of changes: the custom
ambient mode after the scene's load (`end-custom-loaded`), a probe given by the script
(`end-custom-set`), the ambient intensity 2 (`end-custom-set-intensity`), the flat mode
(`end-custom-set-flat`), the custom mode again (`end-custom-again`), the skybox mode without a
skybox material (`end-custom-skybox`), a skybox material assigned (`end-skybox-material`), the
intensity 1.5 (`end-skybox-intensity`), another probe given (`end-skybox-set`), the intensity 0.5
(`end-skybox-set-intensity`), the flat mode with a probe given (`end-flat-set`), another ambient
color (`end-flat-set-color`), the skybox removed (`end-flat-noskybox`), the skybox mode
(`end-skybox-none`), the custom mode (`end-custom-last`), the gradient mode (`end-gradient`),
another probe given in it (`end-gradient-set`) and the custom mode (`end-gradient-custom`).

Then seventeen scenes without lighting data (`m_LightingDataAsset` none; their files, in
`Assets/NoData`, are written by hand, with nothing in them but their settings and, where said, a
directional light; each is in the skybox ambient mode). With this release's RenderSettings:
`NoDataAuto` (LightmapSettings as an earlier release saved them with lighting generated
automatically, the default skybox), `NoDataOnDemand` (the same with that off), `NoDataCurrent`
(this release's LightmapSettings) and `NoDataAutoOwnSky` (as the first, with a procedural sky
material of its own). With RenderSettings as an earlier release saved them: `LegacyDefault` (a
new scene of then: the default skybox and a directional light at (50, -30, 0)), `LegacyAuto`
(the same with lighting generated automatically), `LegacyTurned` (the light at (20, 40, 0)),
`LegacyNoLight` (no light) and `LegacyOwnSky` (the default light, a sky material of its own).
Then the default scene with one thing changed: `CurrentDefault` (both settings as this release
saves them), `LegacySettingsOnly` (the RenderSettings an earlier release's, the
LightmapSettings this release's), `LegacyLightmapsOnly` (the other way round),
`LegacyChildSun` (the light under a parent turned 90 degrees about the vertical, its own
angles still (50, -30, 0)) and `LegacyDimSun` (the light red at intensity 0.3); and with a
second directional light at (20, 40, 0): `LegacyTwoSuns` (the second at intensity 0.3),
`LegacyTwoSunsTurned` (the default one at 0.3, the second at 1) and `LegacyNamedSun` (as
`LegacyTwoSuns`, with the second light named as the scene's sun, `RenderSettings.sun`).
Each records, in the frame after its load, `<scene>-reflection` (as `end-reflection`),
`<scene>-ambient` (`RenderSettings.ambientProbe` as loaded) and `<scene>-custom` (in the custom
mode). Then two pairs, the first scene loaded and the second added to it:
`legacydefault-plus-nodatacurrent` and `nodatacurrent-plus-legacydefault`, each recording the
reflection and the ambient probe two frames after the additive load, right after
`SceneManager.SetActiveScene` with the added scene (`-active`) and a frame later
(`-active-next-frame`). `LegacyDefault` is loaded three more times with
`SupportedRenderingFeatures.active` replaced, a render pipeline's way of saying what it bakes:
`ambientProbeBaking` off (`legacydefault-noambientbaking`), `defaultReflectionProbeBaking` off
(`-noreflectionbaking`) and both off (`-nobaking`).

Last, `RoomBakedEnvironment` (`run.py setup-baked-environment`: the room with baked global
illumination on as well and its environment lighting baked, Lighting Settings' Realtime
Environment Lighting off; its lights are realtime, so its baked lightmap holds the environment
alone): `roombakedenvironment-base` (and `-lightprobes`: its probes' baked coefficients, read
while realtime GI lights them too); `-bakedset-now` (`LightProbes.bakedProbes` given by the
script, each probe's first coefficient of every color raised by a half: the interpolated probes
at the sample points in that frame, nothing else changed), `-bakedset` (the same after the usual
wait, with `-bakedset-lightprobes` reading the coefficients back) and `-bakedrestored-now` (the
scene's own coefficients given back); `-color` (a brighter ambient color); `-alone` (the point
light off and the panel's emission black: the environment as the only light); `-envdata`
(`DynamicGI.SetEnvironmentData` then); over the data, `-envdata-color` (another ambient color),
`-envdata-source` (a gradient as the source), `-envdata-sky` (a skybox as the source) and
`-envdata-intensity` (its ambient intensity 2), the data set again before each; and
`-envdata-update` (the data set again, then `DynamicGI.UpdateEnvironment`).

Then `RoomCharts`: `roomcharts-base` and `roomcharts-moved` (the point light moved). Where each
panel's charts lie is in the scene's precomputed data (`Assets/Scenes/RoomCharts/LightingData.asset`:
each renderer's place in the system and the system's chart mask); the cases record the lightmap
drawn at those texels.

A cube a script gave through a scene load: `Room` loaded again alone, its lights put out,
then the cube given (`room-envdata-alone`); the same scene loaded alone once more, its ambient
inputs what they were before the load, and its lights put out again (`room-envdata-loaded`);
the cube given again and `RoomBakedEnvironment` loaded alone, its lights put out
(`roombakedenvironment-envdata-loaded`).

Then `RoomAuto`: `roomauto-base` and `roomauto-moved` (the point light moved), recorded as the
`RoomCharts` cases are. The charts the precompute made are in the scene's precomputed data, as
there. Then `RoomTurned` the same way: `roomturned-base` and `roomturned-moved`, `RoomCurved`
(`roomcurved-base`, `roomcurved-moved`) and `RoomRounding` (`roomrounding-base`,
`roomrounding-moved`). Then `RoomHoles` (`roomholes-base`). Then the Room loaded seven more times with a script's light delegate
(`GlobalIllumination.Lightmapping.SetDelegate`, set before the load) that extracts the lights as
`LightmapperUtils` does but gives the point and spot lights one falloff type each:
`room-delegate-legacy` and `room-delegate-undefined` at the point light's own range,
`room-delegate-inversesquared` and `room-delegate-norange` (InverseSquaredNoRangeAttenuation)
with its range 4, `room-delegate-linear` (Linear) at its own range,
`room-delegate-linear-range6` with its range 6 and `room-delegate-linear-near` at its own range
0.4 below the ceiling (the spot light is off in all of them). Last, `lights-dilatedrange`:
`Light.dilatedRange` of a light of range 5 as a rectangle (2 x 1, 3 x 3, no size), a disc
(`areaSize` 1.5 by 0.3 and by 7, 4 by 0.3), and a point, spot, directional, pyramid, box and
tube light of `areaSize` 2 x 1, and the range
`LightmapperUtils.Extract` gives the 2 x 1 rectangle and the first disc.

With `--color-space gamma` the same project is in the gamma color space (run.py changes the
copy's player settings) and the probe records the scenes without lighting data, their rows
named as above, then the Room lit: `room-base`, `room-moved` with its point light moved, the
three emission cases (`room-emission-call`, `room-setemissive`,
`room-setemissive-then-call`), and with the panel dark the environment as the ambient color
(`room-env-call`), a procedural sky (`room-sky`) and a gradient (`room-gradient`). Then the Room
loaded again (its panel's material keeps the emission set above, magenta at 3, so the panel
glows until it is made dark): `room-intensity` (point light intensity 4), `room-bounce` (bounce intensity 3),
`room-indirectscale-default` and `room-indirectscale` (2), `room-threshold-default`,
`room-albedo-call` (the red wall blue, with UpdateGIMaterials), `room-spot` (the spot light
alone), `room-directional` (a directional light alone) and, with the panel dark, `room-sky-only`
and `room-sky-only-intensity` (the procedural sky at ambient intensities 1 and 2) and
`room-envdata` (a script's cube); then, the environment back to the flat ambient color,
`RoomDirectional` with three of the linear sequence's cases (without the indirect scale and
threshold steps between them): `roomdirectional-base`, `roomdirectional-moved` and
`roomdirectional-dim`. These rows share the linear set's names but
follow this sequence's own states. Recorded while the machine was building, two captures of this set
differed from the two made on an idle machine: one in `room-gradient` (1607 values, up to 0.04;
the reference holds that case since before), the other in `room-sky-only` (1988 values, up to
0.001), `room-sky-only-intensity` (16, up to 0.0005) and `room-envdata` (1, 0.0001). The two
captures on the idle machine were identical, their earlier cases equal to the reference, and
are its new rows. That set is its own reference, `Reference/unity-<version>/gamma`.

With `--pipeline urp` the same project is drawn by the Universal Render Pipeline (run.py adds
the package and the pipeline assets of tests/projects/urp to the run's copy) and the probe
records a shorter sequence in `Room`, lightmaps and values only (the room's materials are the
built-in pipeline's, so no frames): `pipeline-base`, `pipeline-moved`, `pipeline-intensity` (4),
`pipeline-range` (the point light's range 4, which the far walls are beyond),
`pipeline-bounce` (range 10, intensity 2, bounce intensity 3), `pipeline-near` (bounce
intensity 1, the light a fifth of a unit above the floor), `pipeline-spot` (the spot light
alone at intensity 30, the panel's emission black), `pipeline-spot-inner` (its inner angle 5
instead of 21.8), `pipeline-directional` (a directional light with shadows), then the
environment alone: `pipeline-color` (an ambient
color), `pipeline-sky` (a procedural skybox) and `pipeline-sky-intensity` (its ambient intensity
2); then, the environment back to the flat ambient color, `RoomDirectional`:
`roomdirectional-base`, `roomdirectional-moved` and `roomdirectional-dim` (directional
lightmaps under the pipeline). That set is its own reference, `Reference/unity-<version>/urp`.
The directional cases of the `urp` and `gamma` sets come from two captures each that agree in them; the gamma
pair's earlier cases differ from each other and from the reference by up to 0.002
(`room-base`, `room-moved`, `room-sky-only`, `room-sky-only-intensity`, `room-envdata`; no
value outside the comparator's tolerance), as earlier gamma captures did, and stay as they were.

With both `--pipeline urp --color-space gamma` the pipeline's sequence runs in the gamma-space
project; its own reference is `Reference/unity-<version>/urp-gamma` (two byte-identical
captures). It is recorded but not part of gate.json: Unbound's run has one value outside the
comparator's bounds there, the corner texel (16,41) of `pipeline-directional`
(docs/REALTIME_GI.md, Open).

With `--bake` the capture opens `RoomBake` instead (`run.py setup-bake` adds it and
`RoomBakeLoaded`: the Room saved without lighting data, its LightmapSettings asking for a bake
when the Editor opens it, `Lightmapping.bakeOnSceneLoad` IfMissingLightingData), waits in Edit
mode until the bake that opening it starts is done, and enters Play mode
(`RealtimeGIRunner.RecordBake`, `REALTIME_GI_BAKE`): `roombake-base` and `roombake-moved` (the
point light moved) record the realtime GI the bake made, each renderer's realtime lightmap also
in its own realtime lightmap UV space (`<case>-<renderer>.rlm`), `roombake-lightprobes` the
light probes; `RoomBake` loaded again at runtime records `roombakereload-base`; then
`RoomBakeLoaded`, loaded at runtime, records `roombakeloaded-base`. Its own
reference set is `Reference/unity-<release>/bake`.

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
  nothing else, as documented: the gradient's lightmap at intensity 3
  (`room-gradient-intensity`) equals `room-gradient` within 0.002; a skybox ambient mode
  without a skybox material (`room-sky-none`) and a custom ambient mode (`room-ambient-custom`)
  give the ambient color's light, unscaled.
- A directional light changes the sky's realtime GI by its direction, added and turned without
  `UpdateEnvironment` (`room-sky-sun`, `room-sky-sun-turned`), while `RenderSettings.ambientProbe`
  stays as the last call left it.
- `DynamicGI.SetEnvironmentData` replaces the environment (`room-envdata`); the ambient intensity
  changed afterwards brings the sky back, as documented (`room-envdata-intensity` equals
  `room-sky-only-intensity` within 0.005), and so does `UpdateEnvironment`
  (`room-envdata-update`). Another ambient source replaces the data too, as documented
  (`room-envdata-source` equals `room-gradient` within 0.002). In the gradient mode, where the
  intensity scales nothing, a change of the intensity leaves the data in place
  (`room-envdata-gradient-intensity` equals `room-envdata` within 0.002). An array of another length throws ArgumentException with the message
  recorded in `room-envdata-errors`; none throws ArgumentNullException.
- In an earlier capture the data was set one frame after the directional light was destroyed and
  did not show: the lightmap was the sky's. The probe therefore waits for the sky to settle
  before the call.
- Data set in the frame of a change of the ambient source, after the change, stands
  (`room-envdata-after-color` and `room-envdata-after-sky` equal `room-envdata` within 0.004);
  a change after the data, in its frame, replaces it (`room-envdata-before-color` is the
  color's light). Three captures agree.
- A scene whose environment lighting is baked gives its realtime GI no light from the ambient
  source: the brighter ambient color changes no texel (`roombakedenvironment-color` is
  `-base`, byte for byte) and the environment alone leaves every texel at zero (`-alone`);
  `RenderSettings.ambientProbe` follows the color all the same. `DynamicGI.SetEnvironmentData`
  lights it as it lights `Room` (`-envdata`), and there the data stays whatever the ambient
  source does: another color, a gradient or a skybox as the source, a skybox's ambient
  intensity (`-envdata-color`, `-source`, `-sky` and `-intensity` are `-envdata`), unlike in
  the other scenes, where the documented overwriting shows; `UpdateEnvironment` removes it
  (`-envdata-update` is black).
- A scene loaded alone has nothing of a cube given before: `room-envdata-loaded` is lit by the
  ambient color (a mean of 0.006, 0.009, 0.021 against `room-envdata-alone`'s 0.218, 0.244,
  0.196), although the ambient inputs are the same before and after the load, and
  `roombakedenvironment-envdata-loaded` is black.
- That scene's light probes have baked coefficients too (the environment's, about 0.002 to
  0.007 in the first coefficient). `LightProbes.GetInterpolatedProbe` gives them plus realtime
  GI's lighting: with the environment alone it is the baked coefficients, and when the script
  raises every baked first coefficient by 0.5 the interpolated ones are 0.5 higher in that
  very frame (`-bakedset-now`: 1.3179 against `-base`'s 0.8179) and back when the scene's own
  are given again (`-bakedrestored-now`). `LightProbes.bakedProbes` returns the baked part
  while realtime GI lights the probes (`-lightprobes`) and what the script gave after that
  (`-bakedset-lightprobes`).
- `RenderSettings.ambientProbe` with a skybox follows the ambient intensity without
  `UpdateEnvironment`: 0.2140 times the intensity 1 probe at 0.5 and 4.5948 times at 2 (the
  intensity taken as an sRGB-encoded value). In the custom ambient mode it is the probe last
  derived from the sky, at intensity 1.
- `RenderSettings.ambientProbe` reads as one stored probe seen through the ambient mode (the
  `end-*` cases, each read in the frame of its change; the documentation says only that the probe
  can be set and that the custom mode's lighting is "defined by a custom cubemap"). The stored
  probe is zero after the load of a scene whose lighting data was made without a sky, the sky's
  after `DynamicGI.UpdateEnvironment`, and the one a script gives. The custom mode returns it as
  it is, whatever the ambient intensity; the flat mode returns the ambient color's whatever probe
  is given, and the given probe is back when the custom mode is; the skybox mode returns it times
  the intensity's scale (2.44 at 1.5, 0.214 at 0.5), also right after a probe is given, and
  without a skybox material the ambient color's, unscaled. Assigning `RenderSettings.skybox`
  alone changes nothing until an ambient input changes (`end-skybox-material`, as
  `room-sky-none`, where the skybox was removed after the mode was set).
  The gradient mode does not read the stored probe either (`end-gradient*`).
- Without a sky a scene's default reflection is black: `ReflectionProbe.defaultTexture` is a cube
  of one texel named `UnityBlackCube`, with decode values (1, 1, 0, 0) (`end-reflection`).
- A scene without lighting data reflects that black cube too and its stored ambient probe is
  zero: `RenderSettings.ambientProbe` is zero in the skybox mode and in the custom mode
  (`nodata*`, with the default skybox as with another sky; the four scenes gave the same
  answers as the scene open when Play mode starts, then and 120 frames later: one capture each,
  not part of the reference). One exception, which the 6000.0 upgrade guide describes ("If you
  rely on the previous automatic baking behavior but you use the default environment lighting
  settings, Unity upgrades the scene to use the default Lighting Data Asset"): a scene whose
  RenderSettings an earlier release saved, with the default skybox and a directional light at
  the default angles, reflects `Default-Skybox-Cubemap` (128, decode values (5^2.2, 2.2, 0, 1))
  and has the default sky's ambient probe, also in the custom mode (`legacydefault`), whether
  or not its lighting was generated automatically (`legacyauto`). With the light turned
  (`legacyturned`), without a light (`legacynolight`) or with a sky of its own (`legacyownsky`)
  it is as the other scenes without lighting data. It is the RenderSettings' version that
  counts: with them an earlier release's and the LightmapSettings this release's the scene is
  upgraded (`legacysettingsonly`), with this release's RenderSettings it is not, whatever its
  LightmapSettings (`currentdefault`, `legacylightmapsonly`). The light's own angles count, not
  its direction in the scene (`legacychildsun` is upgraded under a turned parent), and neither
  its color nor its intensity (`legacydimsun`). The light is the one the scene names as its
  sun, else its brightest directional light: with a dimmer second light the scene is upgraded
  (`legacytwosuns`), with a brighter one it is not (`legacytwosunsturned`), and neither when
  the dimmer one is named (`legacynamedsun`; a named light at the default angles is not
  captured). With the rendering features saying that neither
  the ambient probe nor the default reflection is baked the scene is not upgraded
  (`legacydefault-nobaking`); with one of the two off it still is (`-noambientbaking`,
  `-noreflectionbaking`). In a gamma-space project none of the seventeen scenes is upgraded:
  each reflects the black cube and has a zero ambient probe (`--color-space gamma`). Not
  captured: other ambient or reflection sources, another reflection resolution, a named sun
  that is disabled, not directional or not a light of the scene, rendering features a render
  pipeline declares itself or changed after a load, and these environment scenes with
  `Lightmapping.bakeOnSceneLoad` on (a Room asking for it is the `--bake` capture).
- The reflection and the ambient probe are the active scene's: a scene added to another changes
  neither (`*-plus-*`), and `SceneManager.SetActiveScene` with the added scene switches both
  within the frame (`-active`), as the documentation says of the active scene ("from what Scene
  the lighting settings are used").
- A script's delegate in the built-in pipeline: the `Undefined` falloff lights as the legacy one
  (the two lightmaps are byte-identical), and InverseSquaredNoRangeAttenuation is the
  inverse-squared falloff without its window (brighter toward the range, nothing from it on).
  The `Linear` falloff grows with the range: at the point light's own range 10 the lightmaps
  are about five times the legacy falloff's, and at range 6 the scale follows the range (each
  from two byte-identical captures, every earlier file of the reference byte-identical in them;
  a first pair with range 4 stays outside the reference, docs/REALTIME_GI.md). The pair that
  added `room-delegate-linear-near` agrees in it; one of the two had `roomturned-base` and
  `roomturned-moved` in another converged state (RoomTurned has two systems, as RoomSystems:
  up to 0.02 apart, no value outside the comparator's tolerance), the other the reference's.
  `Light.dilatedRange` is a rectangle light's range plus half its diagonal (6.118034 for 2 x 1),
  a disc light's plus half its `areaSize.x` (5.75 and 7, whatever `areaSize.y`), any other
  light's range (every other light type); `LightmapperUtils` gives area lights that range.
- Under the Universal Render Pipeline the lights reach realtime GI as the pipeline's delegate
  gives them (`Lightmapping.SetDelegate`): an inverse-squared falloff with the range's window
  (the lightmaps are a third to a tenth of the built-in pipeline's; with range 4 the far walls
  get nothing from the light), intensity and bounce intensity scaling the light linearly
  (`pipeline-intensity`, `pipeline-bounce`, where the built-in pipeline's grow fourfold and
  tenfold), and a spot light's own inner angle (`pipeline-spot-inner` differs from
  `pipeline-spot` in 139 of 5070 values by more than the comparator's tolerance). A light a
  fifth of a unit above the floor (`pipeline-near`) bounces far less light off it than the
  inverse square of the distance would give: the falloff stops growing next to the light. The skybox lights realtime GI there as in the built-in pipeline, and
  `RenderSettings.ambientProbe` after `DynamicGI.UpdateEnvironment` is the built-in pipeline's
  (0.0979 in its first coefficient, 0.45 at intensity 2).
- The directionality (`roomdirectional-*.dir`): where light arrives from one side its RGB is
  that direction times 1/2 plus 1/2 (the floor's texels (127, 211, 127): light from above, at
  about two thirds of a unit length since it comes from a spread of directions) and its alpha the
  half Lambert term of the surface normal with it (211 on the floor), never below 127. With
  hardly any light (`roomdirectional-dim`) the direction shortens, and texels that receive
  nothing are (127, 127, 127, 127).
- The reference was recorded four times, byte-identical in every file (the three earlier
  captures too, in the files they share). `roomsystems-base` and `roomsystems-moved` have more
  than one converged state, though: of six further captures of that scene alone, four gave the
  reference's lightmaps and two another pair, 64 and 68 of 5070 values apart by at most 0.004
  and 0.006 (1%). Each run's lightmaps stayed as they were for 1200 more frames, with
  `DynamicGI.isConverged` true from the first of them, so this is not a capture taken too
  early: which state the two systems settle in differs from run to run (they update on worker
  threads; that the order of their updates decides it is a hypothesis). An earlier round of
  three captures had one that differed more in the same two cases (up to 0.02 in 1505 and 221
  values). A reference is published from two identical captures; one that lands in another state
  is repeated.
- Two more variations between captures: in one of ten later captures `room-moved-next-frame`
  recorded `DynamicGI.isConverged` true where the reference and the other nine have false (the
  frame after an input changes, the update may not have started); and in one of the three
  captures taken with a cube given two frames after a scene's load (`DynamicGI.SetEnvironmentData`
  before the first solve had converged) the lightmap was up to 0.0015 apart in 379 values,
  which is why `room-envdata-alone` waits for the scene to converge before it gives the cube.
- Automatic charts (`RoomAuto`, each renderer's place in the system and the chart mask of
  `Assets/Scenes/RoomAuto/LightingData.asset`): a chart has the proportions of its surface in
  the world, not of its lightmap UVs, which stretch every face of the scene's meshes to a cell: at
  the scene's two texels a metre a 6 x 4 m face has 14 x 10 texels and a 6 x 0.2 m one 14 x 4.
  The 6 x 0.2 x 4 m floor slab has five charts for its six faces, one end face lying in a large
  face's chart as a line; the wedge has five; the two quads of `RoofFlat`, 20 degrees apart, are
  one chart and those of `RoofSteep`, 100 degrees apart, two; `Step` has five charts for its
  eight faces; the wedge wound against its normals takes the same 10 x 12 texels as the wedge.
  The limits bound each part against a plane through the parts joined: the sides of the roofs
  of 20 degrees, 40 apart, are one chart with Max Angle 30 and two with 15; the step, five
  charts with Max Distance 0.5, has a chart for each of its eight faces with 0.3 and with 0.15
  (its treads, as its risers, are 0.4 m apart, their corners 0.2 m from the plane through
  both).
  `RoomTurned` (two systems): the slab turned 30 degrees has six charts (the rotation's
  removal is exact for it), the box turned (20, 40, 10) and the one under a turned parent four
  (two pairs of faces at a right angle but for that removal's rounding joined), the turned
  wedge five; the two cubes of 0.35 share one geometry; the roof with Max Distance 0.1 has two
  charts, the steps with limits of 0 keep their own lightmap UVs (a chart a face, as with
  Preserve UVs); with Ignore Normals the fold's floor and wall are one
  chart of 4 x 6 texels (projected onto the plane between them and scaled to their area),
  without it two; a triangle of no area is a chart of one block with all its corners at its
  corner; the 110 quads are 110 charts.
  `RoomCurved`: each strip's and bent part's chart is its own lightmap UVs where they are true
  to it (`Arc`, `Bent`) and its projection where they are stretched (`ArcStretched`,
  `BentStretched`) or folded (`ArcFolded`), in charts of other sizes than the other choice's
  (the Editor's GI cache has the same UVs vertex by vertex); Unity's sphere, cylinder side and
  capsule keep their own UVs; Scale In Lightmap 4 and 0.25 leave the charts and clusters as at
  1. `RoomRounding`: the roof at its Max Angle is one chart, the tilted quad is its projection
  (6 x 4 texels; its own UVs would be 4 x 6), and the slab and the box are laid out as the one of the ways the rounding
  allows that the system's clusters tell from the other that fits the chart mask.
  docs/REALTIME_GI.md (Automatic charts) has the rule Unbound makes them by and what it does
  not make.

- A scene without lighting data that asks for a bake when it opens (`--bake`): the Editor bakes
  it when it opens it (Lightmapping.isRunning at once; one capture each, not part of the
  reference: in batch mode too); entering Play mode before the bake is done stops the bake
  and the scene plays without realtime GI, as a scene set to bake never; once the bake is done
  the Editor writes `LightingData.asset` beside the scene and assigns it, and Play mode draws
  its realtime GI (`roombake-*`), which is the Room's (the lightmap values within 0.7% of
  `room-base`'s on average). A scene loaded at runtime is not baked, whatever it asks for:
  `RoomBakeLoaded` has no realtime GI (realtime lightmap index -1) and its lightmaps mode is 1
  (`roombakeloaded-base`), while `RoomBake` itself, loaded again at runtime after the bake,
  keeps its realtime GI (`roombakereload-base`).

## Unbound

Unbound draws precomputed realtime GI for these scenes (docs/REALTIME_GI.md): it makes the
renderers' realtime lightmap UVs, renders their meta passes and updates the systems each frame.
The gate fails on state values (cases, realtime lightmap indices and scale/offset, lightmaps mode,
probe count and baked coefficients, `isConverged`, API defaults, what `SetEnvironmentData`
throws, the default reflection, the baked light probe coefficients, the `Light.dilatedRange`
answers, and which atlas texels the renderers cover), on `RenderSettings.ambientProbe` and the interpolated probes recorded around
a script's baked coefficients, on the frames (a mean difference of at most 2 of 255; the
engine's: 0.75 at most, 0.4 on average) and on the realtime lightmaps of every case
(at most 0.5% of their values outside the tolerance, for
the isolated texels docs/REALTIME_GI.md explains), and reports the other lighting values
(`realtimegi: 0 failing state values, 0 failing gated lighting values, N failing lighting
values`); `compare.py --strict` gates those too. The directional scene's directionality is gated
as well: all four channels of every covered texel within 2.5/255 of the Editor's, at most 0.5%
of them further, each within 5/255 (the engine's: at most 3 of a case's 6760 values beyond
2.5/255, the furthest 4/255 off, 0.16/255 on average). Every failing value, and every allowed isolated
one, is written to `compare-details.txt` in the run directory. Light probes are lit by realtime GI
(their coefficients are reported: 5 values in the engine's run, higher-band coefficients of
five probes in the brightest case; docs/REALTIME_GI.md, Residuals).

## Running

From the repository root, with the release in `UNBOUND_PROBE_UNITY_VERSION` (default 6000.0.71f1):

```sh
python3 tests/projects/realtimegi/run.py setup --label setup1     # only to remake the scenes and precompute
python3 tests/projects/realtimegi/run.py record --label native1   # and native2
python3 tests/projects/realtimegi/run.py publish native1 native2
python3 tests/projects/realtimegi/run.py gate --label gate1       # the land gate (gate.json)
python3 tests/projects/realtimegi/run.py gate --pipeline urp --label gate-urp1   # and its URP part
python3 tests/projects/realtimegi/run.py gate --color-space gamma --label gate-gamma1   # and its gamma part
python3 tests/projects/realtimegi/run.py gate --bake --label gate-bake1   # and its bake-on-load part
```
A script's bake (`script_bake.py`, its own reference `Reference/unity-6000.0.71f1/scriptbake`):
`RoomScriptBake` is `RoomBake` without the bake on load (`script_bake.py setup` writes it); editor
code that runs as Play mode is entered (`Assets/Editor/ScriptBake.cs`) bakes it with
`Lightmapping.Bake`, and Play mode (`Assets/Scripts/ScriptBakeProbe.cs`) records the answer and
events, how many of the renderers that contribute to GI have a realtime lightmap, and what Bake and
BakeAsync answer, send and log in Play mode. Recorded with the macOS Editor in batch mode (two
byte-identical captures): the bake answers true with both events, Play mode starts without its
realtime GI (all 7 renderers at index -1, no lighting data), and in Play mode both calls warn
"Computing lightmaps is not available in play mode." and answer false.

A script's bake in Edit mode (`script_bake.py --edit`, `Reference/unity-6000.0.71f1/scriptbake/edit.jsonl`):
`Assets/Editor/EditBake.cs` opens `RoomScriptBake` in Edit mode and bakes it twice with
`Lightmapping.Bake`, recording each answer, the events with `isRunning` at each (and apart,
whether the lighting data is on disk and named at each), the scene's lighting data, dirty
flag, files and file content and its renderers' realtime lightmap indices; then it enters Play
mode, where `Assets/Scripts/EditBakeProbe.cs` records what is drawn. The Editor runs it with
`-executeMethod EditBake.Record` (Linux, batch mode on the GPU; two byte-identical captures),
Unbound with `unbound test` in Edit mode, where it starts itself
(`REALTIME_GI_EDIT_BAKE_AUTO`). Both bakes answer true: `bakeStarted` (isRunning false), then
`lightingDataUpdated` 22 and 14 times (isRunning true; compared by runs, the count not being
specified), then `bakeCompleted` (isRunning false); the first writes
`RoomScriptBake/LightingData.asset`, its `.meta` and the folder's `.meta`, which the scene names
from then on; the scene is dirty, its file unchanged; every renderer has realtime lightmap index
0 at once and in Play mode. The records of the lighting data asset (`files`, `lightingData`,
`eventData`, `play.lightingData`) and of the Editor's log (on this machine an assertion about
its denoiser library) are reported, not gated: Unbound writes no lighting data asset (said
once).

```bash
python3 tests/projects/realtimegi/script_bake.py native --label sb1   # and sb2
python3 tests/projects/realtimegi/script_bake.py publish sb1 sb2
python3 tests/projects/realtimegi/script_bake.py native --edit --label eb1   # and eb2
python3 tests/projects/realtimegi/script_bake.py publish --edit eb1 eb2
python3 tests/projects/realtimegi/script_bake.py check               # Unbound, both (gate.json)
```


`gate` runs `$UNBOUND_CLI` (else `$UNBOUND`, else `target/release/unbound`) on a copy of the
project and compares with `Reference/unity-<release>/`; `gate --pipeline urp` (and `record`,
`publish` with it) does the same with the Universal Render Pipeline and
`Reference/unity-<release>/urp`, and `--color-space gamma` with the project in the gamma color
space and `Reference/unity-<release>/gamma` (both together: `Reference/unity-<release>/urp-gamma`,
not in gate.json). The engine's result in both: no state or gated
value failing and no value reported. `gate --bake` starts the engine in `RoomBake`, which
Unbound bakes with its own precompute as the Editor opens it, and compares with `--own-layout`
(the renderers lie where each precompute put them, so their places are not compared; each
renderer's own lightmap is): no state or gated value failing (the opened scene's renderers draw
realtime GI, the scene loaded at runtime's do not, every renderer covers the Editor's texels of
its own UV space, the light probes' count), the renderers' lightmaps gated on a share of 4% of
each case's values outside the comparator's tolerance (81 of the 5070 values of `roombake-base`
and 122 of `roombake-moved` from Unbound's precompute), the light probes reported
(docs/REALTIME_GI.md). Run output goes to
`$XDG_CACHE_HOME/unbound/validation/runs/realtimegi/<label>/`.
