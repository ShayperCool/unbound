# Precomputed realtime global illumination

Unity's precomputed realtime GI (Lighting window: Realtime Global Illumination; Unity 6000.0
Manual: [realtime GI](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html))
lights static geometry with bounced light from realtime lights, emissive materials and the
environment, updated at runtime from data precomputed in the Editor. This page records what the
data holds, how the runtime update behaves (documented, then measured against the Unity
6000.0.71f1 Editor in [tests/projects/realtimegi](../tests/projects/realtimegi)), and how Unbound
implements it. Status: the engine draws precomputed realtime GI for scenes whose renderers have
Preserve UVs or [automatic charts](#automatic-charts) of flat parts, lit by point, spot and directional lights and the environment (an ambient color, a
gradient, the skybox or a cube a script sets), non-directional or directional (with its
[directionality](#directionality)), in the built-in
pipeline and, for its lights and environment, under a render pipeline that gives realtime GI its
lights (the Universal Render Pipeline: [Lights of a render pipeline](#lights-of-a-render-pipeline);
frames drawn by a pipeline's own shaders are not compared). For tests/projects/realtimegi every
renderer covers the same realtime lightmap texels as in the Editor and the lightmaps of the light
cases match within the comparator's tolerance but for a few isolated texels, as its frames do (see [Residuals](#residuals)); `unbound doctor` lists scenes with realtime GI, which is
drawn only in part. Not supported yet, each with a warning: automatic charts of curved parts and of
turned renderers ([Automatic charts](#automatic-charts)),
light types other than point, spot and directional (the scene's realtime GI is then not drawn: its
renderers take light probes again; area lights are baked only), light probes whose probe set cannot be read or placed (they light nothing; the
lightmaps are drawn), light probes of a scene that are not the current ones (additive loads),
systems that reference another scene's systems, gamma-space
projects and linear light intensity in the built-in pipeline.

## Documented contract (6000.0)

- Scene switch `LightingSettings.realtimeGI`; resolution (`indirectResolution`, texels per unit),
  `realtimeEnvironmentLighting`, `albedoBoost`, `indirectScale` and Lightmap Parameters are
  precompute inputs. Static renderers with Contribute GI get realtime lightmap UVs; the others
  are lit through light probes.
- `DynamicGI`: `indirectScale` scales the indirect lighting; `updateThreshold` limits updates by
  the percentage change of the lighting; `synchronousMode` shows new output in each frame;
  `isConverged`; `SetEmissive(renderer, color)` sets a renderer's emissive color without
  rendering the system's emissive input; `UpdateEnvironment()` schedules an update of the
  environment cubemap; `SetEnvironmentData(float[])` overrides the environment with a cube of six
  faces of 8 x 8 texels of four floats (1536), which "changing the Distant Environment Lighting
  Source or Environment Lighting Intensity will overwrite";
  `RenderSettings.ambientIntensity` scales the ambient light "only when RenderSettings.ambientMode
  is AmbientMode.Skybox and the scene has a skybox material";
  `Renderer.UpdateGIMaterials()` schedules an update of the system's albedo and emissive textures.
- Shader inputs: `unity_DynamicLightmap` (and `unity_DynamicDirectionality` for directional
  mode) with `unity_DynamicLightmapST`, keyword `DYNAMICLIGHTMAP_ON`; light probes carry the
  realtime GI of dynamic objects.

## Precomputed data

The scene's LightingData asset lists, per renderer, `lightmapIndexDynamic` and
`lightmapSTDynamic` (the realtime lightmap atlas and the renderer's place in it), a scene
mapping (renderers per system, each system's atlas and offset, the atlas sizes, probe sets), and
a payload: a Unity bundle of per-system files named by the system's hash (`radiositySystemHash`).
A transport or probe set names the systems it reads by their `inputSystemHash`; in a scene of one
system both hashes are the same, in a scene of several (tests/projects/realtimegi `RoomSystems`)
they differ, and each system's transport references the others' cluster and tree values as well
as its own.

| File | Content |
|---|---|
| `.iws.{ref,sse,neo}` | input samples: clusters (bounding box, number of four-point groups), points quantized to 8 bits in their cluster's box with one 8-bit normal per group; the cluster tree (child count per parent, 8-bit child weights); per-cluster centroid and normal |
| `.rsc.{ref,sse,neo}` | transport: per output segment, the referenced input values (dependency, index, 16-bit weight), an 8-bit weight per (texel, reference), the texel coordinates, and for the directionality, per block of 2x2 texels the segment owns, a normal and an 8-bit direction per reference; per block, which neighbouring blocks belong to its chart; the bounce resampling (per cluster, texels and weights); per-texel chart data |
| `.caw` | which albedo/emission texels form each cluster |
| `.rgb` | an 8-bit RGBX texture at system resolution (not the albedo the update uses) |
| `.ecm` | chart mask at system resolution: per texel, the chart it belongs to (each chart a rectangle of even width and height) |
| `.vis` | precomputed visibility of directional lights: directions on up to 128 rings of latitude around +y (first direction and count per ring), and per direction and cluster the share of its points that see the sky that way, quantized to 1, 2, 4 or 8 bits, stored for groups of directions, each group run-length encoded (a byte n < 0x80 copies n bytes, 0x80 + n repeats the next byte n times) |
| `.pos`, `.rpc.{ref,sse,neo}` | light probe positions and their transport |

`ref`, `sse` and `neo` hold the same data laid out for different CPUs; Unbound reads `ref`.

## Runtime update

Unbound's model of one update of a system (`crates/unbound-gi`, compared with the converged
Room cases of tests/projects/realtimegi in its tests):

1. **Direct light per cluster**: the mean over the cluster's points of `max(0, n.l)` times the
   light's distance falloff, its angular falloff (spot lights) and its color, scaled by intensity
   and `Light.bounceIntensity` before its conversion to linear (in projects without linear light
   intensity). Each point's normal is three bytes b, each component b / 127 - 1 (so that axis
   normals are exact), not renormalized. The distance falloff is the built-in pipeline's legacy
   curve 1 / (1 + 25 x^2) (faded to 0 from x^2 = 0.64 to 1) over x = distance / range, sampled at
   the 16 table positions t = 15 / (1 + 14 x^2) = 0..15 (0 at t = 0 and 1, 1 at t = 15) and
   interpolated linearly in t. A spot light's angular falloff is s^2, with s the cosine of the
   angle from the light's direction placed between those of the outer half angle (half of
   `Light.spotAngle`: 0) and the inner one (1 and above), where the inner half angle is
   atan(tan(outer) * 0.71875) whatever `Light.innerSpotAngle` is (the probe's spot light has an
   inner angle of 21.8 against this rule's 37.1); s is tabulated at 31 equal steps of the cosine
   from the outer one to 1 and read at the step at or below the point's cosine, without
   interpolation. Point and spot lights are not shadowed in this input (the probe's lights have
   shadows enabled; `room-spot-behind` lights the wall behind the block). A directional light
   lights a cluster's first n points in their stored order, the others are in its shadow (also
   for a light without shadows, `room-directional-noshadows`): n is the
   cluster's point count times its precomputed visibility toward the light, rounded down, read at
   the four stored directions around the light's (latitude t = acos(y) * (rings - 1) / pi between
   two rings, longitude atan2(z, -x) / 2 pi + 1/2 times each ring's count between two of its
   directions, wrapping around), weighted bilinearly with byte weights that add up to 255. The
   Editor's direct light per cluster matches this within 0.05% (point) and 0.6% (spot); the
   directional cases' lightmaps match as the others do.
2. **Cluster radiance**: `albedo^2 * (direct + bounce) + emission`, with albedo the cluster's
   8-bit gamma-space albedo and emission linear (see [Material inputs](#material-inputs)).
3. **Cluster tree**: each parent value is the 8-bit-weighted (/255) sum of its children.
4. **Output texel** irradiance: the weighted sum over the referenced cluster, tree and
   environment values, clamped at 0, times `DynamicGI.indirectScale`, stored as RGB9E5 in the
   atlas.
5. **Bounce**: the precomputed bounce resampling lists, per cluster, weighted output texels; the
   cluster's next bounce input is the drawn value of its first one alone, divided by
   `DynamicGI.indirectScale` (with the weighted sum, clusters on slab edges, whose later samples
   lie on thin faces that only see the environment, get 5 to 30% less bounce than in the Editor).
6. **Environment**: six 8x8 faces (+X, -X, +Y, -Y, +Z, -Z, each row by row from its top as a
   cube map stores it) of linear RGB and their coarser levels, each value the mean of the 2x2
   under it (510 values); see [Environment](#environment) for what fills the faces.
7. **Update threshold**: the output texels are solved in segments (groups of texels with the
   values they reference). Each value carries how much it changed in this update (the mean of its
   channels' absolute changes; tree values the weighted sum of their children's). A segment adds
   up the weighted changes of its references since it was last solved and is solved again only
   when that sum reaches `DynamicGI.updateThreshold` percent of the mean of its weighted
   references' channels (plus 1e-4/127); otherwise its texels keep their earlier values. The
   first update and a change of the indirect scale solve every segment; a threshold of 0 solves
   every segment in every update.

So converged output depends on the state it converged from: with `DynamicGI.indirectScale` 2
after the brighter `room-bounce` case the Editor's texels are 2.00 to 2.07 times those of
`room-intensity`, as Unbound's are (the probe gates this default-threshold behaviour). With
`updateThreshold` 0 every texel reaches the fixed point (`roomscaled-threshold0`,
`roomdirectional-threshold0`; there the Editor's output differs from the default threshold's,
`-indirectscale`, by at most about 4%, so those cases record the behaviour without separating
the two). The update is converged (`DynamicGI.isConverged`) once an update of every system
leaves its drawn texels unchanged: the bounce reads them, so nothing changes until the inputs
do.

## Lights of a render pipeline

A scriptable render pipeline decides what its lights give global illumination: it registers a
delegate (`UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate`; Unity 6000.0
Scripting API) that turns each `Light` into a `LightDataGI` with the helpers of
`LightmapperUtils`. The Universal Render Pipeline's gives every light the inverse-squared
falloff (`FalloffType.InverseSquared`), a spot light its own `Light.innerSpotAngle`, and, with
the linear light intensity the pipeline sets, colors scaled after their conversion to linear.
Unbound implements that API (`Graphics/GlobalIlluminationLights.cs`) and, when a delegate is
registered, takes the frame's lights from it: the indirect color (the color times intensity and
bounce intensity), position, orientation, range, cone angles and falloff type of each; a light
the delegate gives no indirect light, or none at all (`InitNoBake`), adds nothing. Compared
with the Editor under the Universal Render Pipeline (tests/projects/realtimegi `run.py
--pipeline urp`, its own reference set `Reference/unity-<version>/urp`: the Room with a point
light moved, brighter, of a shorter range and with bounce intensity 3, the spot light with its
own and a narrow inner angle, a directional light, the point light close to the floor, an
ambient color, and a skybox at ambient intensities 1 and 2; lightmaps, light probes and
`RenderSettings.ambientProbe`, no frames):

- **Distance falloff**: the table of step 1 above holds the light's own falloff: the
  inverse-squared one is `(1 - x^4)^2 / distance^2` over x = distance / range at the
  13 sampled positions (so it depends on the range) and 0 from the range on
  (`pipeline-range`: a range the far walls are beyond). At the light itself the table ends at 1,
  below its innermost sample (196 / range^2 at a fourteenth of the range), so a point nearer
  than that gets less light than the curve gives (`pipeline-near`: the light a fifth of a unit
  above the floor, range 10). `InverseSquaredNoRangeAttenuation`, `Linear` and `Undefined`
  are not measured and fail the scene's realtime GI loudly.
- **Spot lights**: the cone of step 1 with the inner angle the delegate gives
  (`pipeline-spot` and `pipeline-spot-inner`, the bright spot light alone with inner angles
  21.8 and 5: 139 of their 5070 values differ by more than the comparator's tolerance); the
  built-in pipeline's inner angle is the one `LightmapperUtils.ExtractInnerCone` computes.
- **Colors**: `LightmapperUtils` applies a light's color temperature, with linear light
  intensity, to the components of the converted colors (not captured: the probe's lights use
  none).
- **Environment**: as in the built-in pipeline, the skybox included (`pipeline-sky`,
  `pipeline-sky-intensity`): the sky is drawn into the faces, and into the ambient probe, by
  the engine itself and not by the pipeline's camera (`RenderSettings.ambientProbe` after
  `DynamicGI.UpdateEnvironment` equals the built-in pipeline's).

With these the engine's lightmaps differ from the Editor's by 0.0003 to 0.0016 on average in
the light cases (one value of one case outside the comparator's tolerance) and 0.0007 to 0.0035
in the sky cases, and no light probe or ambient probe value is outside it. Not measured: other
pipelines' delegates (the High Definition Render Pipeline's), the other falloff types, area
lights (baked only in the Universal Render Pipeline; a light the delegate gives as one adds
nothing, with a warning), directional lightmaps and several systems under a pipeline, and a
delegate a script registers in the built-in pipeline (Unbound takes its lights the same way). A
delegate that throws leaves the frame without lights, with a warning.

## Directionality

With directional lightmaps (`LightmapSettings.lightmapsMode` CombinedDirectional; Unity 6000.0
Manual: [directional lightmaps](https://docs.unity3d.com/6000.0/Documentation/Manual/LightmappingDirectional.html))
each realtime lightmap comes with a directionality texture (`unity_DynamicDirectionality`, sampled
with the lightmap's sampler under `DIRLIGHTMAP_COMBINED`): RGB is the dominant direction of the
texel's light times 1/2 plus 1/2, and Unity's shaders light a surface with the lightmap's color
times the half Lambert term of the shaded normal with that direction, divided by the texture's
alpha (the "rebalancing" term). Every system's transport holds the data for it, whatever the
scene's mode (the probe's four scenes have the same blocks). Unbound's model
(`crates/unbound-gi/src/directionality.rs`), compared with the Editor's texture in
tests/projects/realtimegi `roomdirectional-*`:

1. **Blocks**: the directionality is solved per block of 2x2 texels. The first texels a segment
   lists are one per block it owns (block x / 2, y / 2), each with a normal (three bytes b for
   b * 2 / 255 - 1) and, per reference of the segment, the index of the direction that
   reference's light arrives from.
2. **Directions**: index 0 is no direction; indices 1 to 255 are unit vectors spread over the
   sphere, direction i at height z = 2 r(i - 1) - 1, with r the base-2 radical inverse, and at
   azimuth (i - 1/2) / 255 of a turn from +x toward +y.
3. **A block's direction**: the mean of its references' directions, each weighted by the texel
   weight of the block's first texel times the luminance (0.2126, 0.7152, 0.0722) of the
   reference's weighted value. Light from all around gives a short direction. When the sum of
   those weights (the luminance the texel receives, before the indirect scale) is below 0.001,
   the sum of the weighted directions is divided by 0.001 instead: a darker block's direction is
   shorter in proportion, and a black one has none (`roomdirectional-dim`, where 743 of 1690
   texels receive less than that).
4. **Stored value**: the direction times 1/2 plus 1/2 in RGB, and in alpha the half Lambert term
   of the block's normal with it, `dot(normal, direction) / 2 + 1/2`, at least 1/2.
5. **Texels**: each block's value belongs to the middle of its 2x2 texels, and a texel takes
   9/16 of its own block, 3/16 of each of the two blocks beside it and 1/16 of the diagonal one,
   but only from blocks of its chart (the transport's neighbour flags per block): a missing
   neighbour beside it counts as the block itself, a missing diagonal one as the sum of the two
   beside it less the block. The result is clamped to [0, 1] and truncated to 8 bits.
6. **Update threshold**: a segment that is not solved in an update leaves its blocks' texels as
   they are; neighbouring blocks that are solved read such a block as the mean of its four
   texels.
7. **Copies**: a segment's first copies, as many as its header says, copy whole blocks before
   the texels are made; the others copy texels afterwards. None of the probe's systems has
   copies, so this part is unmeasured (a directional scene with copies says so in a warning).

A system whose blocks cannot be read keeps its lightmap and has no directionality: its scene is
drawn as a non-directional one, with a warning.

With these rules the engine's directionality differs from the Editor's by 0.2/255 on average; at
most 3 of a case's 6760 values differ by more than 2.5/255 (by up to 4/255), as the lightmap's
isolated texels do. Not measured: `LightmapSettings.lightmapsMode` changed by a script (Unbound
follows the mode of each frame: a scene that becomes directional, also once again, solves every
part of its lightmaps once more and draws an atlas with its directionality once every system of
the atlas has, one that stops being directional draws without directionality), a directional scene of
several systems or atlases (the probe's has one system), a renderer with a baked and a realtime
lightmap of which only one has a direction (the other then reads a direction that leaves its
light as it is: 1/2 in every channel), frames in which the directionality matters (none of the
probe's materials has a normal map, so its frames show the lightmap alone), directionality under
a scriptable render pipeline, and gamma-space projects.

## Light probes

A scene's light probes are lit by its probe sets (the scene mapping's `m_Probesets`; the
LightProbes asset's `m_Data.m_ProbeSets` places each, by hash, at a range of probe indices).
A probe set's transport lists, per probe, the input values it references (as a system's
transport: cluster and tree values of its systems, the environment) and, per reference and
coefficient, an 8-bit weight around 127 times the coefficient's 16-bit scale times 2^-23 and a
fixed factor per coefficient ([1, 2, 2, 2, 0.3125, 3.75, 3.75, 3.75, 0.9375], picked per
coefficient by the file's order bytes). The weighted sums of the references' values from the
systems' last update, times `DynamicGI.indirectScale`, are the probe's SphericalHarmonicsL2
coefficients as `LightProbes.GetInterpolatedProbe` returns them at the probe (tests/projects/
realtimegi: within the comparator's tolerance in every point and spot light case but the
brightest two, `room-bounce` and the `room-indirectscale` that follows it, where a few small
higher-band coefficients differ by up to about 0.5% of band 0; the direct light matches, the
cause is open). Probes interpolate as
baked ones do; `LightProbes.bakedProbes` stays empty. Probes that have baked lighting too
(`RoomBakedEnvironment`, whose baked probes hold the environment) give the baked coefficients
plus the realtime GI's (`roombakedenvironment-*`: within the comparator's tolerance in every
case; the baked part is small there, 0.002 to 0.007, so the sum shows in `-bakedset-now`, where
the script raises the baked first coefficients by 0.5 and the interpolated ones are 0.5 higher
in that frame, gated).
`LightProbes.bakedProbes` returns the baked part alone (`-lightprobes`, `-bakedset-lightprobes`,
gated). Probes of several scenes combined by `LightProbes.Tetrahedralize` join their baked
coefficients, zeros for a scene without any; realtime GI lights each scene's own set
(unmeasured). Unbound updates them with every update of
the systems.

Measured in the probe: the update runs over several frames on worker threads (bounces converge
frame by frame), so the first frames after a change are not reproducible between runs even with
`synchronousMode`; the probe compares converged output, and Unbound replays the probe's cases in
order (`crates/unbound-gi` tests) since each converges from the one before. Also measured, beyond
the documented contract: `indirectScale` starts as the scene's Indirect Intensity; an albedo change waits for
`UpdateGIMaterials`; after `SetEmissive` a material emission change is ignored even after
`UpdateGIMaterials`; realtime GI picks up ambient color and skybox exposure changes without
`UpdateEnvironment`, while the call is what updates `RenderSettings.ambientProbe` for the
skybox (see [Environment](#environment)). Not measurable as a reference, since the result depends on update timing: a material
emission change without `UpdateGIMaterials`, and `updateThreshold` above its default of 1.

## Environment

What fills the environment's faces, measured in tests/projects/realtimegi with nothing else
lighting the room (the documented parts are in the contract above):

- The ambient color (`room-env-*`): its linear value in every texel. A custom ambient mode
  (`room-ambient-custom`) and a skybox ambient mode without a skybox material (`room-sky-none`)
  read the ambient color too. The ambient intensity does not scale it (`room-sky-none` and
  `room-ambient-custom` at intensity 3).
- A gradient (`room-gradient`): the sky color on +Y, the ground color on -Y and the equator color
  on the four other faces, linear; the same at ambient intensity 3 (`room-gradient-intensity`).
- A skybox (`room-sky*`): the sky drawn into the six faces, one texel per pixel of a 90 degree
  view, as the ambient probe's capture draws it (with the sun's direction and without its color,
  so without a sun disk; without a main light, a light direction of (0, 0, -1)). The ambient
  intensity scales each texel as a gamma-encoded value: linear to sRGB, times the intensity, back
  to linear (`room-sky-only-intensity`: 2).
- A change of these inputs shows without `DynamicGI.UpdateEnvironment`: the ambient colors, the
  ambient intensity, a skybox material property (`room-sky-nocall`), a sun added and turned
  (`room-sky-sun`, `room-sky-sun-turned`; its bounce intensity is 0, so it adds nothing but its
  place in the sky).
- `DynamicGI.SetEnvironmentData` (`room-envdata`): the cube's RGB as given, linear, faces and
  rows in the order above. It stands until one of those inputs changes
  (`room-envdata-intensity`: the ambient intensity with a skybox, and `room-envdata-source`:
  another ambient mode, both as documented) or
  `DynamicGI.UpdateEnvironment` is called (`room-envdata-update`). Where the intensity scales
  nothing it is not such an input: data set in the gradient mode stays when the intensity
  changes (`room-envdata-gradient-intensity`). An array of another length
  throws ArgumentException ("Array size is wrong. It must have 1536 elements. The resolution of
  each face is 8x8 texels."), none ArgumentNullException.
- `RenderSettings.ambientProbe` with a skybox: `DynamicGI.UpdateEnvironment` derives it from the
  sky; reading it scales it by the ambient intensity of the moment taken as a gamma value
  (GammaToLinearSpace: 0.214 at 0.5, 4.59 at 2; `room-sky-only-intensity-half` and the cases
  after it), and a sun added later does not change it without the call.
- `RenderSettings.ambientProbe` in general (the probe's `end-*` cases, each read in the frame of
  its change; the documentation promises none of it): one stored probe seen through the ambient
  mode. The stored probe is the lighting data's after a scene's load (zero for data made without
  a sky and for a scene without lighting data, `nodata*`; the active scene's: a scene added to
  it changes nothing until `SceneManager.SetActiveScene` makes it the active one, `*-plus-*`),
  then the sky's
  once `DynamicGI.UpdateEnvironment` derives it, then one a script gives,
  the latest of them. The flat and gradient modes do not read it (a probe given in either
  shows once the custom mode is set); the custom mode returns it as it is, whatever the ambient
  intensity (`room-ambient-custom`: the sky's probe at intensity 3, unscaled); the skybox mode
  returns it times the intensity's scale, and without a skybox material the ambient color's
  probe, unscaled. The Editor makes the probe again when an ambient input changes, not when
  `RenderSettings.skybox` alone is assigned: a skybox removed after the mode was set leaves the
  scaled sky probe (`room-sky-none`), one assigned in the skybox mode leaves the color's
  (`end-skybox-material`) until the next change. The intensity's scale is that of the bullet
  above in a linear project; a gamma project's was measured for the lighting data's probe
  (GammaToLinearSpace as well) and Unbound gives the sky's and a given probe the same. Not
  measured: a probe given after a skybox was assigned alone, `DynamicGI.UpdateEnvironment`
  without a skybox after a probe was given (Unbound keeps the probe), and a probe given before
  another scene becomes the active one (Unbound keeps it with the scene it was given in).
  Realtime GI's own environment does not read the probe (the bullets above).
- The default reflection: a scene whose lighting data was made without a sky, and one without
  lighting data, reflect a black cube (`ReflectionProbe.defaultTexture` is `UnityBlackCube`,
  one texel, decode values (1, 1, 0, 0); `end-reflection`, `nodata*-reflection`). It is the
  active scene's, as the ambient probe is.
- A scene without lighting data takes the built-in lighting data (the default sky's probe and
  reflection, `Default-Skybox-Cubemap`) when its RenderSettings were saved by an earlier
  release with a new scene's environment: the skybox ambient mode, the default skybox reflected
  at the default resolution and a sun at the default light's angles, (50, -30, 0)
  (`legacydefault`, `legacyauto`; the 6000.0 upgrade guide: scenes that "use the default
  environment lighting settings" are upgraded "to use the default Lighting Data Asset"). With
  the sun turned or missing or with another sky it has none (`legacyturned`, `legacynolight`,
  `legacyownsky`). What makes a scene an earlier release's is its RenderSettings' version alone
  (`legacysettingsonly`, `legacylightmapsonly`, `currentdefault`); the sun's angles are its own,
  not its direction under its parents (`legacychildsun`), and its color and intensity do not
  count (`legacydimsun`); the sun is the light the scene names, else its brightest directional
  light (`legacynamedsun`, `legacytwosuns`, `legacytwosunsturned`); rendering features that
  say neither the ambient probe nor the default reflection is baked
  (`SupportedRenderingFeatures.active`, a render pipeline's to declare) leave the scene
  without lighting data, one of the two alone does not (`legacydefault-no*baking`); a
  gamma-space project upgrades no scene (`--color-space gamma`: all seventeen scenes zero and
  black). Unbound applies the rule as measured, reading the rendering features at the scene's
  load. Other ambient or reflection sources and another resolution are not measured and leave
  the scene without lighting data; a named sun that is disabled or not directional is taken
  as it is, and one that is not a light of the scene gives way to the brightest light (neither
  measured, nor a named sun at the default angles: the named one measured is turned). The
  rendering features were measured as a script sets them before the load: what a pipeline
  declares itself is not, and Unbound's pipeline declares them when it first renders, after
  its first scene's load, so that scene is read with the features of before; features changed
  after a load are not measured either. `Lightmapping.bakeOnSceneLoad` is not honoured (a warning once). All of this is the
  measured release's, through the compatibility profile: in another release a scene without
  lighting data gets zero and black too, with a warning once, since releases before Unity 6
  bake the ambient probe and the sky's reflection by default (the Unity 6 upgrade guide) and
  later ones are not measured.

Data set in the frame of a change of the ambient source stands when it is set after the change
(`room-envdata-after-color`, `room-envdata-after-sky`) and is replaced by a change that follows
it in that frame (`room-envdata-before-color`), in the Editor as in Unbound, which draws and
reads the sky in the frame its inputs change. Observed once and not modelled: data given to
`SetEnvironmentData` one frame after the sun was destroyed did not show (the lightmap stayed the
sky's): the Editor reads the sky back over several frames, and an update still on its way
replaced the data.

A scene whose environment lighting is baked (`LightingSettings.realtimeEnvironmentLighting` off
with baked GI on; tests/projects/realtimegi `roombakedenvironment-*`) gives its realtime GI no
light from the ambient source: an ambient color change shows nothing and the environment alone
lights nothing; a cube set with `DynamicGI.SetEnvironmentData` lights it as any scene and,
unlike there, stays whatever the ambient source does (another color, a gradient or a skybox as
the source, the skybox's ambient intensity: `-envdata-color`, `-source`, `-sky`, `-intensity`),
until `DynamicGI.UpdateEnvironment` (`-envdata-update`: black again). Unbound keeps the script's
cube beside the ambient source's faces and gives such a scene the cube or black faces; while
every loaded scene is such a one it does not make the ambient source's faces at all.

A scene loaded alone takes nothing of a cube given before it: its environment is made from its
ambient source, also when the source's inputs are what they were when the cube was given
(`room-envdata-loaded`), and a scene with baked environment lighting has none
(`roombakedenvironment-envdata-loaded`). Not measured: a scene loaded beside others (Unbound:
it takes a cube still kept).

Not measured: sky materials other than the procedural one
(they are drawn by the same capture), and gamma-space projects.

Unbound (`Graphics/RealtimeGIEnvironment.cs`) keeps the cube and what it was made from (the
ambient mode and colors, or the skybox material and its change count, the ambient intensity and
the sun's direction in thousandths, so that a slowly turning sun does not draw the sky again in
every frame); a frame makes it again only when that key differs (the sky's capture sends scripts
no camera messages or events), and each native scene takes a
changed cube once (`gi_set_environment`), which restarts its update only when values changed.

## Material inputs

Each system takes its albedo and emission from two textures at system resolution in which its
renderers' meta passes (`LightMode` `Meta`, Unity's own shaders) are drawn at their realtime
lightmap UVs. Unbound's model of how they are made and read (`Graphics/RealtimeGI.cs`,
`crates/unbound-gi`); with it the engine's atlases for tests/projects/realtimegi match the
Editor's but for the residuals below, which do not come from the materials (the clusters' albedo
equals the Editor's):

- Each renderer is drawn with `unity_LightmapST` and `unity_DynamicLightmapST` set to its scale
  and offset within the system (the scene mapping's `dynamicLightmapSTInSystem`),
  `unity_MetaVertexControl` (0, 1, 0, 0), `unity_MetaFragmentControl` (1, 0, 0, 0) for albedo and
  (0, 1, 0, 0) for emission, `unity_OneOverOutputBoost` 1 / Albedo Boost, `unity_MaxOutputValue`
  0.97 and `unity_UseLinearSpace`, into a target cleared to black (emission alpha 1) under an
  orthographic projection of [0, 1]^2; then again as wireframe, so that texels the triangles
  only touch are written too. Albedo goes into 8-bit sRGB texels in a linear project; emission
  into half floats, and only for materials whose `globalIlluminationFlags` have RealtimeEmissive
  without EmissiveIsBlack.
- The cluster material data (`.caw`) lists, per cluster, groups of points of one renderer, each
  with texel samples whose integer weights add up to the group's point count. A cluster's albedo
  is the square root of the weighted mean of its samples' squared bytes (the square standing in
  for the gamma curve), kept as 8 bits; its emission the weighted mean of its texels, kept as a
  half float. `DynamicGI.SetEmissive` gives a renderer one emission for all its points instead of
  its texels, its color taken to linear as a material color is; a later material update does not
  replace it (measured: `room-setemissive`, `room-setemissive-then-call`; that other renderers'
  emission still follows their materials is Unbound's model, the probe changes the panel alone).
  With these rules and each renderer's color in all its texels, the converged `room-emission-call`,
  `room-setemissive*` and `room-albedo-call` lightmaps match as the other Room cases do.
- The wireframe pass makes these textures depend on how the graphics device draws lines, in the
  Editor too: a capture of the probe on the Editor's Vulkan device differs from the OpenGL Core
  reference in one light probe's coefficients by about 13% in every scene (17 values outside the
  comparator's tolerance; the lightmaps within the gate). Vulkan's lines leave the texel an edge
  ends in unwritten where OpenGL's write it: drawn through Vulkan, the top right corner texel of
  every chart of the probe's rooms stays black (42 of `Room`'s 2160 texels; no other texel
  differs) and the clusters that read such a texel are darker. Unbound's OpenGL Core device is
  drawn by wgpu's OpenGL backend and has those texels, as the reference does. On its Vulkan
  device Unbound's textures lack them, and differ from the Editor's Vulkan capture as well (27
  values reported against it); which texels the Editor's Vulkan device leaves out is not
  measured.

## Realtime lightmap UVs

Renderers draw realtime GI at their own realtime lightmap UVs, which are not the mesh's lightmap
UVs scaled by `realtimeLightmapScaleOffset`: the precompute repacks each mesh's charts. The Editor keeps the
result in its GI cache, not in the project (a player build stores it in
`MeshRenderer.enlightenVertexStream`); the LightingData asset holds only where each renderer's UV
space lies in its system and the system's chart mask. Unbound makes them itself
(`crates/unbound-gi/src/realtime_uv.rs`, cached in `Library/Unbound/RealtimeUVs` by a hash of
the inputs and a format version), for renderers with Preserve UVs and triangle meshes, whatever
their Lightmap Parameters (parameters that change the charts, such as their resolution, fail the
layout check below; `RoomSystems` gives three renderers parameters of their own with System Tag
1):

- Input: `Mesh.uv3` when the mesh has it, else the baked lightmap UVs (`Mesh.uv2`), else
  `Mesh.uv`. The Manual documents the first two
  ([Generate lightmap UVs](https://docs.unity3d.com/6000.0/Documentation/Manual/LightingGiUvs-GeneratingLightmappingUVs.html):
  "If you already have baked lightmap UVs in `Mesh.uv2`, and you want to provide different UVs
  as input for your real-time lightmaps, place the real-time lightmap UVs in Mesh.uv3";
  otherwise "Unity falls back to sharing the baked lightmap UVs"); `RoomCharts`' `Channels`
  plate, 2 x 1 m with a square in `Mesh.uv3`, gets a square of 4 x 4 texels, not the 6 x 4 of
  its `Mesh.uv2`. How the precompute then scales and packs the charts is not documented: what
  follows is what the Editor's precomputed data shows in the probes' scenes.
- Charts: the triangles of those UVs that share a vertex, or a vertex position and UV; in the
  order of their first triangle.
- Each chart keeps its UV shape, scaled uniformly so its area is its world area at the realtime
  resolution (Lighting Settings' resolution times the renderer's Scale In Lightmap), in blocks of
  2x2 texels. Its width and height become whole blocks separately: `max(ceil(size + 0.5 +
  1e-4), minimum chart size / 2)`, the chart filling it less half a texel on each side.
- Charts are rasterized into block masks (every block a chart triangle, grown by a quarter block,
  touches) and packed largest first (ties in chart order), each where it wastes the least area
  below and to the left of it, resting on the columns or against the rows already used (ties: the
  lowest, then the leftmost). The space starts at one block and, when a chart does not fit, grows
  along its shorter side (the height when square) by that chart's size; it ends cropped to what
  is used. Every growth packs all charts again from the start.
- Holes: a chart that finds no such place, in a space large enough for its mask, goes into a
  free region between the charts placed before it. The free blocks form 4-connected regions; the
  chart takes the first region, fewest free blocks first, that has at least its number of blocks
  and holds its mask, at the lowest, then leftmost position. From that chart on the regions are
  kept up to the end of the pass: a placed chart's blocks leave their regions, which split into
  what is still connected, and a region that cannot hold the smallest chart (fewer free blocks
  than it has, a narrower or lower box, or no place for it when it is a full rectangle) is given
  up and counts as used. The places resting on the columns or against the rows still come
  first, so in a grown space the same charts may leave a hole empty.

In the Room scenes of tests/projects/realtimegi and the Realtime scene of
tests/projects/lightingassets this gives the Editor's layouts: the size of each renderer's UV space
and every chart's blocks in the precomputed chart mask, which Unbound checks for every renderer
(a difference keeps the system from being drawn, with a warning). `RoomCharts` measures the
holes with four flat panels, each a chart with holes and loose square plates: a ring whose hole
holds its two plates side by side; the same ring with three plates, whose UV space grows by two
blocks and holds all three above the ring, the hole empty; a plate with two holes, where the
first loose plate takes the hole of its own size although the larger hole comes first from the
bottom left, and the second the larger hole's lowest, leftmost place; and an arch, whose pocket
is open to the lower edge and takes a plate as a hole does. Each loose plate lies on the
precomputed chart of its own number. Not measured, and implemented as one consistent rule: what
giving a region up changes for the charts placed after it, the order of regions of equal size,
how many blocks a region that reaches the rows above every chart counts, and up to what region
size a place for the smallest chart is searched before a region is given up; a difference
there shows as a failed layout check, not as wrong lighting. (Within a system, other renderers'
charts may lie in the blocks a renderer's own charts leave free: the check compares only the
blocks its charts use.)

### Automatic charts

A renderer with Optimize Realtime UVs (Mesh Renderer, Lightmapping; serialized as
`m_PreserveUVs: 0`, which a new renderer has) does not keep its mesh's charts. The Manual
([Introduction to lightmap UVs](https://docs.unity3d.com/6000.0/Documentation/Manual/LightingGiUvs.html),
Merging): "Optionally, you can instruct Unity to merge UV charts where possible during this
process", with the note that it "sometimes makes mistakes about discontinuities in the original
UV mapping. For example, an intentionally sharp edge may be misinterpreted as a continuous
surface"; and under Packing: "Mesh Renderers that use the same mesh with the same scale and
lightmap resolution share the same UVs". The
[Mesh Renderer reference](https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshRenderer.html)
gives the limits: Max Distance, "the maximum worldspace distance for UV chart simplification in
real-time lightmaps. If charts are within this distance, Unity simplifies them"
(`m_AutoUVMaxDistance`, 0.5 in a new renderer); Max Angle, "the maximum angle in degrees between
faces sharing a UV edge in real-time lightmaps. If the angle between the faces is below this
value, Unity simplifies the UV charts" (`m_AutoUVMaxAngle`, 89); Ignore Normals, "to prevent
Unity from splitting the UV charts" (`m_IgnoreNormalsForChartDetection`). How charts are merged
and laid out is not documented. What follows is Unbound's rule
(`crates/unbound-gi/src/auto_charts.rs`); it gives the Editor's precomputed layouts for the
shapes of `RoomAuto` in tests/projects/realtimegi, and is not measured beyond them.

- Geometry: the mesh with the instance's scale and without its rotation (positions times the
  scale, normals over it, normalised), which is what sharing by mesh and scale needs. Each
  triangle counts as facing the way its vertices' normals do, whatever its winding.
- Parts: triangles connected through their vertices, where vertices with the same position,
  lightmap UV and normal are one vertex. A part has its area, its normal (the mean of its
  triangles' normals by area, a triangle's normal being that of its three vertex normals
  together) and the plane that fits its vertices by least squares. It is flat when that plane
  is within Max Angle of its normal, all its vertices are within Max Distance of the plane and
  no more than a tenth of its area faces away from its normal.
- Joining, in rounds while at least two flat parts are left (at most a hundred rounds): 300
  pairs are tried, a flat part and one of its neighbours (the flat parts whose bounds, grown
  by 0.6 of Max Distance, meet its own; itself among them), picked by a fixed pseudo-random
  sequence (the standard Mersenne Twister with its default seed). A pair whose normals face the
  same half-space gets the plane that fits both; when both are within the two limits of that
  plane, the flat parts on it that connect to the pair as neighbours join them. The set with
  the largest area along its plane wins the round (the first of equals) and leaves the pool.
  A part paired with itself is a set too, so the faces of a box leave one by one, largest first.
- The limits bound a part against the plane through a set, not two parts against each other.
  The two sides of `RoofAngleWide` are 40 degrees apart and each 20 from the plane through
  both: with Max Angle 30 they are one chart, and with 15 (`RoofAngleTight`) two. That is
  wider than the Manual's wording of Max Angle ("the angle between the faces is below this
  value") read literally; it is what this release's precompute did with these two shapes.
  The step has five charts for its eight faces with Max Distance 0.5, and a chart a face with
  0.3 (`StepMid`) and with 0.15 (`StepNear`). By the rule its two treads join at 0.5, as its
  two risers do: each pair is 0.4 m apart, its corners at most 0.2 m from the plane through
  both. At 0.3 the corners are still within the limit of that plane; what keeps the pairs
  apart is that their bounds, grown by 0.6 of the limit each, no longer meet.
- Leftovers (parts that are not flat, then the flat parts no round took) are folded into the
  nearest joined set whose bounds they meet, when they have less than a tenth of its area and
  all their vertices are closer to its plane than Max Distance. Their normal is not asked: the
  last end face of a 6 x 0.2 x 4 m slab becomes a line in a large face's chart, and the slab has
  five charts. What is still left is a chart of its own.
- Each set is projected onto its plane, in world units, with its axes turned to the direction
  along which the legs of its right-angled triangles carry the most area (directions within 3
  degrees of parallel or perpendicular count as one). A part left on its own is projected too,
  unless its own lightmap UVs are truer to its shape (compared by the angles of its triangles).
- The charts are sized and packed as [above](#realtime-lightmap-uvs), from their world extents
  instead of their UV shapes: a width and a height in blocks, `max(ceil(extent / block + 0.5 +
  1e-4), minimum chart size / 2)`, each axis stretched to fill its blocks. So a chart has the
  proportions of the surface, not of its UVs: at two texels a metre a 6 x 4 m face takes 14 x 10
  texels and a 6 x 0.2 m one 14 x 4.

`RoomAuto` is the Room with Optimize Realtime UVs on every lit renderer (minimum chart size 4;
the limits of a new renderer but for the four shapes named above) and fourteen more shapes: a
thin bar, a wedge (five charts), the same wedge mesh scaled unevenly, a wedge whose triangles
are wound against its normals, an L-shaped step (five charts for its eight faces), a roof of
two quads 10 degrees to either side of level (one chart) and one of 50 degrees (two), a grid of
cells that share their vertices, Unity's quad and plane meshes scaled unevenly, and the two
roofs and two steps of the limits. For every one of its renderers Unbound's charts cover the
blocks of the precomputed chart mask, in chart order, and its lightmaps match the Editor's
(`roomauto-base`, `roomauto-moved`).

The check against the precomputed chart mask compares the blocks the charts cover, not which
face lies in them: two charts of one size taken in the other order would pass it with their
lighting exchanged. So what Unbound cannot make as the Editor does is refused, not left to the
check. Each refusal is a warning that names the renderer, and its system is then not drawn:

- A turned renderer, and a renderer with the mesh and scale of a turned one in the same scene
  (the Editor makes one set of UVs for both). The Editor's geometry of a turned instance
  carries rounding in the last digits (a normal of (-1, 0, 3.7e-8), say), and that rounding
  decides charts: faces at a right angle face the same half-space or not by it, and in a bake
  of a turned slab of 3 x 0.2 x 2 m (not part of the probe's scene) the Editor joined each
  thin side to a large face and made three charts, not five. Unbound does not reproduce that
  rounding.
- Choices that rounding decides in an instance that is not turned, where the mesh itself is
  off the coordinate axes: two neighbouring flat parts at a right angle but for rounding that
  one plane could hold within the limits (their normals' cosine is near zero, or zero only
  because its terms cancel, as for a slab modelled at 45 degrees); two different sets that are
  the largest of a round, of the same area or of one but for rounding (within 1e-5 of each
  other); a leftover as near to two sets in the same way. Along the coordinate axes the
  arithmetic is exact and such ties are taken in order, as the Editor takes them in the scene's
  boxes and steps (the two charts of a step's tread and riser of one size among them). Off the
  axes the very same measure is taken in order only for a mirror image: when flipping the signs
  of some coordinates takes the one set's vertices onto the other's (and a leftover onto
  itself), which keeps every magnitude and sum of the precompute's as it is, as for the two
  sides of the scene's roofs. Congruent sets of a shape turned or moved off its mesh's origin
  are refused.
- A mesh whose flat parts take more than the hundred rounds to join.
- A part that is not flat within the limits and that nothing folds in: curved surfaces. Their
  charts need a measure of overlap in the projection, which is not implemented.
- A bent part left on its own (flat within the limits, not planar): whether it keeps its own
  lightmap UVs or is projected is a comparison of the two that is not implemented. Joined in
  a round, as when its mesh has other parts and it is not the last of them, it is projected.
- A planar part left on its own on a plane that is not along the coordinate axes, whose own
  lightmap UVs are an undistorted copy of its shape and are laid out otherwise than its
  projection: both are then equally true but for rounding.
- Ignore Normals, Max Distance or Max Angle of zero, a zero or mirrored scale, a mesh without
  normals, and a part without area that lies by no chart: not measured.

Not measured, and implemented as written above: scale under parents that skew it (the
transform's `lossyScale` is used), meshes that mix triangles wound with and against their
normals, and a small part that is not flat folded into a set it lies by (projected onto that
set's plane, as the flat leftovers measured are). Not guarded: a part exactly at a limit but for rounding (its lean or its distance
within the last digits of Max Angle or Max Distance, off the coordinate axes), and what the
Editor's own cache of earlier bakes may share with a renderer (it keeps results by mesh and
scale across bakes; a scratch bake reused one for a renderer that had since been turned).

## Engine

`Graphics/RealtimeGI.cs` loads a scene's systems (`realtime-gi-load`), makes the renderers'
realtime lightmap UVs and renders their meta passes into each system's material textures at load
and after `Renderer.UpdateGIMaterials`. In PostLateUpdate.EnlightenRuntimeUpdate it passes the
frame's lights (realtime, and mixed ones without baked GI, with bounce intensity), and the
environment when it changed, to the native update (`crates/unbound-runtime/src/realtime_gi.rs`): one update of every system in
synchronous mode, else of as many as fit in 2 ms (at least one), each system in turn reading the
other systems' values of their last updates, without allocating; the scene
is converged once an update of every system left its drawn texels unchanged. Changed atlases are
uploaded as RGBA half floats; renderers draw them as `unity_DynamicLightmap` with their realtime
scale and offset and `DYNAMICLIGHTMAP_ON`, and take no light probes. In a frame whose lightmaps
mode is directional the systems solve their directionality too; each atlas then has a second
texture (RGBA8, `gi_read_directionality`, readable once every system of the atlas has solved its
directionality), which its renderers draw as `unity_DynamicDirectionality` with
`DIRLIGHTMAP_COMBINED`. The light probes'
coefficients are read back after each update (`gi_read_probes`) into the scene's LightProbes.
`DynamicGI.isConverged`, `synchronousMode`, `SetEmissive`, `SetEnvironmentData`,
`UpdateEnvironment`, `indirectScale` and `updateThreshold` act on it.

## Residuals

Measured with tests/projects/realtimegi (the comparator allows 0.02 + 5%):

- `DynamicGI.indirectScale` 0 draws black texels and so leaves the bounce at 0; a later scale
  converges again from there (unmeasured in the Editor).
- Isolated texels: since a segment is solved again only when its inputs change by the update
  threshold, input differences well below it decide which update last solved a segment, and so
  move a few texels (2 to 6 values of about 5000 per case in the engine, up to about 10%). The
  comparator gates the lightmaps with at most 0.5% of their values outside the
  tolerance, each within 0.02 + 10%. What is left on the OpenGL Core device:
  texel (45,34) of `Room`, 7 to 8% low from `room-bounce` on, and the texels next to the emissive
  panel ((1,9), (1,12), (2,12), (4,12)), 1 to 4% off in `room-base`, 2 to 5% after
  `DynamicGI.SetEmissive` and 6 to 11% in the later cases where little else lights them. With
  the panel's emission set to black (the environment cases from `room-gradient` on) no value is
  outside the tolerance, so the emitting panel's nearest texels are the open part, not the
  update history alone. (Drawn through Vulkan the material textures lack each chart's last
  corner texel, see [Material inputs](#material-inputs): `RoomScaled` and `RoomSystems` then
  have isolated texels as well, and (0,11), (0,12), (0,28), (8,8), (32,33) in `Room`.)
- A small bias: with every material texel written (`crates/unbound-gi`'s replay of the `Room`
  cases) the solver's texels are 0.5% below the Editor's on average in the cases lit by the point
  light (5% of them more than 2.5% below, 5% more than 1.4% above), 0.2 to 0.3% with the bright
  ambient color, 0.1% with the gradient alone; the direct light matches within 0.05%, so the
  bounce is the open part.
- Several systems: the Editor's own converged lightmaps of `RoomSystems` differ between runs
  (two states in six captures, 64 to 68 of 5070 values apart by at most 1%, each stable once
  reached; tests/projects/realtimegi README). The reference holds the more frequent one; both are
  well within the comparator's tolerance of Unbound's, which updates the systems in their order.
- Directionality: at most 3 of a case's 6760 values more than 2.5/255 from the Editor's (up to
  4/255), 0.2/255 on average (see [Directionality](#directionality)).
- Environment: with the environment as the only light the lightmaps differ from the Editor's by
  0.0004 to 0.001 on average for colors and gradients (at most 0.015), 0.001 for the sky (at
  most 0.008; 0.004 and 0.02 at ambient intensity 2, on values around 0.4) and 0.001 for a
  script's cube (the comparator's mean and largest difference).
- Frames: the engine's differ from the Editor's by 0.4 of 255 on average over the cases (0.75
  at most, where a spot or directional light's shadow edge crosses a few pixels) and are gated
  on a mean of 2. (They were 3 to 21 off while a scene without a default reflection reflected
  grey instead of the Editor's black cube, `end-reflection`.)
- Light probes (reported, 5 values in the engine's run): a probe's coefficients are sums of
  large weights of both signs, so small differences of the values they read show in the small
  higher-band ones. In `room-bounce`, the brightest case, higher-band coefficients of five of
  the eight outer probes differ by up to about 0.5% of band 0: the bias above, cause open.
  (Drawn through Vulkan, one coefficient of one probe in `RoomScaled` is 15% low, three
  coefficients are off in `room-indirectscale` and three more probes in `room-bounce`: the
  missing corner texels of the material textures.)

## Unbound plan

- Phase 2, runtime over Unity's precomputed data (above): unmeasured inputs fail loudly until a
  probe case covers them.
- Phase 3, Unbound's own precompute from scene geometry (the rest of the automatic charts,
  system packing, clusters, transport, bounce resampling, probe transport) for projects without precomputed data or with
  changed scenes, cached under `Library/Unbound/` by content hash and format version, compared with the Editor's
  precompute through the same probe.
