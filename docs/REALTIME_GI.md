# Precomputed realtime global illumination

Unity's precomputed realtime GI (Lighting window: Realtime Global Illumination; Unity 6000.0
Manual: [realtime GI](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html))
lights static geometry with bounced light from realtime lights, emissive materials and the
environment, updated at runtime from data precomputed in the Editor. This page records what the
data holds, how the runtime update behaves (documented, then measured against the Unity
6000.0.71f1 Editor in [tests/projects/realtimegi](../tests/projects/realtimegi)), and how Unbound
implements it. Status: the engine draws precomputed realtime GI for scenes whose renderers have
Preserve UVs, lit by point lights and a flat ambient color, in the built-in pipeline (URP binds the
same inputs; unmeasured there). For tests/projects/realtimegi every renderer covers the same
realtime lightmap texels as in the Editor and the lightmaps of the point-light cases match within
the comparator's tolerance but for a few isolated texels (see [Residuals](#residuals)); `unbound doctor` lists scenes with realtime GI, which is
drawn only in part. Not supported yet, each with a warning: Preserve UVs off (automatic charts),
spot and directional lights (the scene's realtime GI is then not drawn: its renderers take light
probes again), skybox and gradient environments (no
environment light), light probes lit by realtime GI, directional realtime lightmaps, systems
that reference other systems, gamma-space projects and linear light intensity.

## Documented contract (6000.0)

- Scene switch `LightingSettings.realtimeGI`; resolution (`indirectResolution`, texels per unit),
  `realtimeEnvironmentLighting`, `albedoBoost`, `indirectScale` and Lightmap Parameters are
  precompute inputs. Static renderers with Contribute GI get realtime lightmap UVs; the others
  are lit through light probes.
- `DynamicGI`: `indirectScale` scales the indirect lighting; `updateThreshold` limits updates by
  the percentage change of the lighting; `synchronousMode` shows new output in each frame;
  `isConverged`; `SetEmissive(renderer, color)` sets a renderer's emissive color without
  rendering the system's emissive input; `UpdateEnvironment()` schedules an environment update;
  `Renderer.UpdateGIMaterials()` schedules an update of the system's albedo and emissive textures.
- Shader inputs: `unity_DynamicLightmap` (and `unity_DynamicDirectionality` for directional
  mode) with `unity_DynamicLightmapST`, keyword `DYNAMICLIGHTMAP_ON`; light probes carry the
  realtime GI of dynamic objects.

## Precomputed data

The scene's LightingData asset lists, per renderer, `lightmapIndexDynamic` and
`lightmapSTDynamic` (the realtime lightmap atlas and the renderer's place in it), a scene
mapping (renderers per system, each system's atlas and offset, the atlas sizes, probe sets), and
a payload: a Unity bundle of per-system files named by the system's hash.

| File | Content |
|---|---|
| `.iws.{ref,sse,neo}` | input samples: clusters (bounding box, number of four-point groups), points quantized to 8 bits in their cluster's box with one 8-bit normal per group; the cluster tree (child count per parent, 8-bit child weights); per-cluster centroid and normal |
| `.rsc.{ref,sse,neo}` | transport: per output segment, the referenced input values (dependency, index, 16-bit weight), an 8-bit weight per (texel, reference), the texel coordinates; the bounce resampling (per cluster, texels and weights); per-texel chart data |
| `.caw` | which albedo/emission texels form each cluster |
| `.rgb` | an 8-bit RGBX texture at system resolution (not the albedo the update uses) |
| `.ecm` | chart mask at system resolution: per texel, the chart it belongs to (each chart a rectangle of even width and height) |
| `.vis` | precomputed visibility used for directional lights |
| `.pos`, `.rpc.{ref,sse,neo}` | light probe positions and their transport |

`ref`, `sse` and `neo` hold the same data laid out for different CPUs; Unbound reads `ref`.

## Runtime update

Unbound's model of one update of a system (`crates/unbound-gi`, compared with the converged
Room cases of tests/projects/realtimegi in its tests):

1. **Direct light per cluster**: the mean over the cluster's points of `max(0, n.l)` times the
   light's distance falloff (the built-in pipeline's legacy curve over distance/range) times the
   light's color, scaled by intensity and `Light.bounceIntensity` before its conversion to linear
   (in projects without linear light intensity). Point lights are not shadowed in this input
   (the probe's point light has shadows enabled). The falloff is the curve itself; the
   Editor's matches it within about 1%.
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
6. **Environment**: six 8x8 faces and their coarser levels (510 values); a flat ambient color
   gives its linear value everywhere.
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
`room-intensity`, as Unbound's are; with `updateThreshold` 0 (`room-threshold0`) every texel
reaches the fixed point. The update is converged (`DynamicGI.isConverged`) once an update of
every system leaves its drawn texels unchanged: the bounce reads them, so nothing changes until
the inputs do.

Measured in the probe: the update runs over several frames on worker threads (bounces converge
frame by frame), so the first frames after a change are not reproducible between runs even with
`synchronousMode`; the probe compares converged output, and Unbound replays the probe's cases in
order (`crates/unbound-gi` tests) since each converges from the one before. Also measured, beyond
the documented contract: `indirectScale` starts as the scene's Indirect Intensity; an albedo change waits for
`UpdateGIMaterials`; after `SetEmissive` a material emission change is ignored even after
`UpdateGIMaterials`; realtime GI picks up ambient color and skybox exposure changes without
`UpdateEnvironment`, while the call is what updates `RenderSettings.ambientProbe` for the
skybox. Not measurable as a reference, since the result depends on update timing: a material
emission change without `UpdateGIMaterials`, and `updateThreshold` above its default of 1.

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

## Realtime lightmap UVs

Renderers draw realtime GI at their own realtime lightmap UVs, which are not the mesh's UV2 scaled
by `realtimeLightmapScaleOffset`: the precompute repacks each mesh's charts. The Editor keeps the
result in its GI cache, not in the project (a player build stores it in
`MeshRenderer.enlightenVertexStream`); the LightingData asset holds only where each renderer's UV
space lies in its system and the system's chart mask. Unbound makes them itself
(`crates/unbound-gi/src/realtime_uv.rs`, cached in `Library/Unbound/RealtimeUVs` by a hash of
the inputs and a format version), for renderers with Preserve UVs, the scene's default Lightmap
Parameters and triangle meshes:

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
frame's lights (realtime, and mixed ones without baked GI, with bounce intensity) and environment
to the native update (`crates/unbound-runtime/src/realtime_gi.rs`): one update of every system in
synchronous mode, else of as many as fit in 2 ms (at least one), without allocating; the scene
is converged once an update of every system left its drawn texels unchanged. Changed atlases are
uploaded as RGBA half floats; renderers draw them as `unity_DynamicLightmap` with their realtime
scale and offset and `DYNAMICLIGHTMAP_ON`, and take no light probes. `DynamicGI.isConverged`,
`synchronousMode`, `SetEmissive`, `indirectScale` and `updateThreshold` act on it.

## Residuals

Measured with tests/projects/realtimegi (the comparator allows 0.02 + 5%):

- Direct light: the point light falloff within about 2% of the Editor's per cluster (the
  Editor interpolates its falloff curve from a table of samples).
- Isolated texels: since a segment is solved again only when its inputs change by the update
  threshold, input differences well below it decide which update last solved a segment, and so
  move a few texels (2 to 11 values of about 5000 per Room case in the engine, up to about 8%;
  one chart-edge texel by 8% also with a threshold of 0). The comparator gates the point-light
  cases' lightmaps with at most 0.5% of their values outside the tolerance.

## Unbound plan

- Phase 2, runtime over Unity's precomputed data (above): next, light probes from the probe
  transport's SH, spot and directional lights (with the precomputed visibility), skybox and
  gradient environments, directional output, several systems; unmeasured inputs fail loudly
  until a probe case covers them.
- Phase 3, Unbound's own precompute from scene geometry (automatic charts for Preserve UVs off,
  system packing, clusters, transport, bounce resampling, probe transport) for projects without precomputed data or with
  changed scenes, cached under `Library/Unbound/` by content hash and format version, compared with the Editor's
  precompute through the same probe.
