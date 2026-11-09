# Precomputed realtime global illumination

Unity's precomputed realtime GI (Lighting window: Realtime Global Illumination; Unity 6000.0
Manual: [realtime GI](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html))
lights static geometry with bounced light from realtime lights, emissive materials and the
environment, updated at runtime from data precomputed in the Editor. This page records what the
data holds, how the runtime update behaves (documented, then measured against the Unity
6000.0.71f1 Editor in [tests/projects/realtimegi](../tests/projects/realtimegi)), and how Unbound
implements it. Status: the data is described and the probe is recorded; Unbound reads none of
it yet and warns once per scene (`Graphics/Lightmapping.cs`), and `unbound doctor` lists the
affected scenes.

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
| `.rgb` | the system's albedo texture as precomputed (8-bit, system resolution) |
| `.ecm` | chart mask at system resolution |
| `.vis` | precomputed visibility used for directional lights |
| `.pos`, `.rpc.{ref,sse,neo}` | light probe positions and their transport |

`ref`, `sse` and `neo` hold the same data laid out for different CPUs; Unbound reads `ref`.

## Runtime update

Unbound's model of one update of a system; not yet checked against the reference (phase 2
implements it and compares its converged output with tests/projects/realtimegi):

1. **Direct light per cluster**: the mean over the cluster's points of `max(0, n.l)` times the
   light's distance falloff (the built-in pipeline's legacy curve over distance/range) times the
   light's color, scaled by intensity and `Light.bounceIntensity` before its conversion to linear
   (in projects without linear light intensity). In this model point lights are not shadowed
   in this input (the probe's point light has shadows enabled).
2. **Cluster radiance**: `albedo^2 * (direct + bounce) + emission`, with albedo the cluster's
   8-bit gamma-space albedo (the material's meta pass output) and emission linear.
3. **Cluster tree**: each parent value is the 8-bit-weighted (/255) sum of its children.
4. **Output texel** irradiance: the weighted sum over the referenced cluster, tree and
   environment values, clamped at 0, stored as RGB9E5 in the atlas; `DynamicGI.indirectScale`
   scales it.
5. **Bounce**: each cluster's next bounce input is a weighted sum of output texels.
6. **Environment**: six 8x8 faces and their coarser levels (510 values); a flat ambient color
   gives its linear value everywhere.

Measured in the probe: the update runs over several frames on worker threads (bounces converge
frame by frame), so the first frames after a change are not reproducible between runs even with
`synchronousMode`; the probe compares converged output. Also measured, beyond the documented
contract: `indirectScale` starts as the scene's Indirect Intensity; an albedo change waits for
`UpdateGIMaterials`; after `SetEmissive` a material emission change is ignored even after
`UpdateGIMaterials`; `UpdateEnvironment` changed nothing measurable (ambient color and skybox
exposure changes are picked up without it). Not measurable as a reference, since the result
depends on update timing: a material emission change without `UpdateGIMaterials`, and
`updateThreshold` above its default of 1.

## Unbound plan

- Phase 2, runtime over Unity's precomputed data: read the payload and scene mapping into a
  content-keyed cache entry (`Library/Unbound/`), run the update above in Rust on a worker with a
  per-frame budget and no per-frame allocations, upload the atlas (RGB9E5) and bind
  `unity_DynamicLightmap`/`unity_DynamicLightmapST`/`DYNAMICLIGHTMAP_ON` for the built-in
  pipeline and URP; light probes get the probe transport's SH. Material albedo and emission come
  from the material's own meta pass rendered at the realtime lightmap UVs. Unmeasured inputs
  (directional output, spot/area/directional light details, skybox/gradient environments,
  transparency) fail loudly until a probe case covers them.
- Phase 3, Unbound's own precompute from scene geometry (charts, clusters, transport, bounce
  resampling, probe transport) for projects without precomputed data or with changed scenes,
  cached under `Library/Unbound/` by content hash and format version, compared with the Editor's
  precompute through the same probe.
