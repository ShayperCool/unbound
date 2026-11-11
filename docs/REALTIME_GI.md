# Precomputed realtime global illumination

Unity's precomputed realtime GI (Lighting window: Realtime Global Illumination; Unity 6000.0
Manual: [realtime GI](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html))
lights static geometry with bounced light from realtime lights, emissive materials and the
environment, updated at runtime from data precomputed in the Editor. This page records what the
data holds, how the runtime update behaves (documented, then measured against the Unity
6000.0.71f1 Editor in [tests/projects/realtimegi](../tests/projects/realtimegi)), and how Unbound
implements it. Status: the engine draws precomputed realtime GI for scenes whose renderers have
Preserve UVs, lit by point, spot and directional lights and the environment (an ambient color, a
gradient, the skybox or a cube a script sets), non-directional or directional (with its
[directionality](#directionality)), in the built-in
pipeline (URP binds the same inputs; unmeasured there). For tests/projects/realtimegi every
renderer covers the same realtime lightmap texels as in the Editor and the lightmaps of the light
cases match within the comparator's tolerance but for a few isolated texels, as its frames do (see [Residuals](#residuals)); `unbound doctor` lists scenes with realtime GI, which is
drawn only in part. Not supported yet, each with a warning: Preserve UVs off (automatic charts),
light types other than point, spot and directional (the scene's realtime GI is then not drawn: its
renderers take light probes again; area lights are baked only), light probes whose probe set cannot be read or placed (they light nothing; the
lightmaps are drawn), light probes of a scene that are not the current ones (additive loads),
systems that reference another scene's systems, gamma-space
projects and linear light intensity.

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
case; the baked part is small there, 0.002 to 0.007, so the sum shows in `-bakedset`, where the
script raises the baked first coefficients by 0.5 and the interpolated ones follow).
`LightProbes.bakedProbes` returns the baked part alone (`-lightprobes`, `-bakedset-lightprobes`,
gated). Probes of several scenes combined by `LightProbes.Tetrahedralize` join their baked
coefficients; realtime GI lights each scene's own set (unmeasured). Unbound updates them with every update of
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
  `legacyownsky`). Unbound applies the rule in a linear project with those settings, through
  the compatibility profile; other ambient or reflection sources, another resolution, a
  gamma-space project and several directional lights (Unbound takes the brightest as the sun)
  are not measured and leave the scene without lighting data.

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
unlike there, stays when the ambient color changes (`-envdata-color`), until
`DynamicGI.UpdateEnvironment` (`-envdata-update`: black again). Unbound keeps the script's cube
beside the ambient source's faces and gives such a scene the cube or black faces; while every
loaded scene is such a one it does not make the ambient source's faces at all.

Not measured: a skybox under a scriptable render pipeline (it
adds no realtime GI light there, with a warning: the capture is the built-in pipeline's; colors,
gradients and a script's cube work), sky materials other than the procedural one
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

Renderers draw realtime GI at their own realtime lightmap UVs, which are not the mesh's UV2 scaled
by `realtimeLightmapScaleOffset`: the precompute repacks each mesh's charts. The Editor keeps the
result in its GI cache, not in the project (a player build stores it in
`MeshRenderer.enlightenVertexStream`); the LightingData asset holds only where each renderer's UV
space lies in its system and the system's chart mask. Unbound makes them itself
(`crates/unbound-gi/src/realtime_uv.rs`, cached in `Library/Unbound/RealtimeUVs` by a hash of
the inputs and a format version), for renderers with Preserve UVs and triangle meshes, whatever
their Lightmap Parameters (parameters that change the charts, such as their resolution, fail the
layout check below; `RoomSystems` gives three renderers parameters of their own with System Tag
1):

- Charts: the triangles of the lightmap UVs (UV2, else UV1) that share a vertex, or a vertex
  position and UV; in the order of their first triangle.
- Each chart keeps its UV shape, scaled uniformly so its area is its world area at the realtime
  resolution (Lighting Settings' resolution times the renderer's Scale In Lightmap), in blocks of
  2x2 texels. Its width and height become whole blocks separately: `max(ceil(size + 0.5 +
  1e-4), minimum chart size / 2)`, the chart filling it less half a texel on each side.
- Charts are rasterized into block masks (every block a chart triangle, grown by a quarter block,
  touches) and packed largest first (ties in chart order), each where it wastes the least area
  below and to the left of it, resting on the columns or against the rows already used (ties: the
  lowest, then the leftmost). The space starts at one block and, when a chart does not fit, grows
  along its shorter side (the height when square) by that chart's size; it ends cropped to what
  is used.

In the Room scenes of tests/projects/realtimegi and the Realtime scene of
tests/projects/lightingassets this gives the Editor's layouts: the size of each renderer's UV space
and every chart's rectangle in the precomputed chart mask, which Unbound checks for every renderer
(a difference keeps the system from being drawn, with a warning). A chart that only a hole between
other charts would hold, which the precompute fills with another heuristic, fails loudly. With
Preserve UVs off the precompute generates the charts themselves (not supported yet).

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
- Phase 3, Unbound's own precompute from scene geometry (automatic charts for Preserve UVs off,
  system packing, clusters, transport, bounce resampling, probe transport) for projects without precomputed data or with
  changed scenes, cached under `Library/Unbound/` by content hash and format version, compared with the Editor's
  precompute through the same probe.
