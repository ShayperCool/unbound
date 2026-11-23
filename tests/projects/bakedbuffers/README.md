# Baked lightmap buffers probe

Unity 6000.0.71f1, built-in render pipeline, Progressive CPU lightmapper. What a bake in Edit
mode makes of each baked lightmap before it puts the lightmap together: which texels are used,
the chart of each, the surface normal at each of a texel's samples, the order the bake gives its
renderers and its lights, and the light its lights send straight to each texel.
Unity documentation (6000.0):
[Lightmapping.Bake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Lightmapping.Bake.html),
[Lightmap Parameters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LightmapParameters.html)
(Anti-aliasing Samples: "the number of times to supersample a texel to reduce aliasing"; the
ranges the page gives for 2× and 4× are not the release's, see `Samples` below),
[Lightmap UVs](https://docs.unity3d.com/6000.0/Documentation/Manual/LightingGiUvs.html) (charts),
[Mesh Renderer](https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshRenderer.html),
[Light Mode](https://docs.unity3d.com/6000.0/Documentation/Manual/LightModes-introduction.html)
and [Lighting Mode](https://docs.unity3d.com/6000.0/Documentation/Manual/lighting-mode.html)
(Baked lights: "Unity bakes direct lighting and indirect lighting from Baked Lights into
lightmaps"; Mixed lights: their direct light is baked in Subtractive mode and stays realtime in
Baked Indirect and Shadowmask), [Light](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Light.html).
[Lighting Settings](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LightingSettings.html)
(Environment Samples: the samples the lightmapper takes of the environment's light;
Environment Importance Sampling) and
[Environment Lighting](https://docs.unity3d.com/6000.0/Documentation/Manual/lighting-window.html)
(the ambient source: the skybox, a gradient or a colour, and its Intensity Multiplier). The
documentation gives no rule for the sky's images, for the picked directions, for the mesh a sky
is drawn on or for a submesh without a material: those are the release's observed results.
The documentation gives no rule for the texels a chart covers or for the charts' numbers: what
is compared here is the release's observed result.

## Content

`Assets/Editor/BuffersSetup.cs` (`bake_buffers.py create`) makes the scenes of `Assets/Scenes`,
each with its own Lighting Settings (one sample, no filtering, no compression, non-directional,
padding 2):

- `Shapes` (5 texels per unit, 256): every primitive, placed, turned and scaled, one mirrored
  (a negative scale), one under a scaled and turned parent, a cube with Scale In Lightmap 0, one
  lit by light probes (not in the lightmap; the others keep their numbers in the bake), a LOD
  Group.
- `Samples` (6, 64): a cube and a sphere for each of the Anti-aliasing Samples 1, 3, 4, 5, 8, 9,
  16 and 256: a texel gets one sample for 1, 2 × 2 for 3 and 4 × 4 from 4 on (2 gives 2 × 2:
  tests/projects/bakedatlas `Tags`), and the lightmaps come in the order of their groups.
- `Charts` (12, 128): meshes whose lightmap UVs meet the texels in particular ways: two quads
  over the same UVs, a triangle over part of a quad (given after it, and before it), a fan of
  slivers thinner than a texel, long needles, triangles without area in the lightmap and one
  without area in space, two submeshes of their own vertices and two sharing an edge's, three
  triangles in a row given so that the last joins two charts, a quad whose triangles have their
  own vertices along the shared edge (equal there, and with other normals), normals without
  length and of other lengths, UVs wound the other way.
- `Bare` (12, 64): a mesh without normals beside a cube. The Editor leaves it out of the bake and
  says "Some meshes have invalid normals and will be ignored by the Global Illumination system."
  (a warning, once a bake; `Lightmapping.Bake` says the same and leaves its renderer without a
  lightmap).

The lit scenes have no sky and no ambient light, 16 Direct Samples (a power of two not below a
texel's samples: the Editor's result is then the same from bake to bake but for its last bits),
a ground, a cube and a sphere, and lights with hard shadows:

- `Sun` (4, 128): a coloured baked directional light (intensity 2, Indirect Multiplier 3) over
  renderers with Cast Shadows Off and Shadows Only, with Receive Shadows off, lit by light probes
  (it casts its shadow), mirrored, and with Scale In Lightmap 0.
- `Lamps`: baked point and spot lights: ranges that end on the ground, cones of 20, 60 and 170
  degrees, a light without shadows, one inside the sphere (off its centre: with a light at the
  centre the Editor lets a few rays through the sphere's mesh, along its edges, which Unbound
  does not), and lights that are disabled, on an inactive GameObject or out of everything's
  reach. Seven of its lights reach the scene's one region of the light grid: a path's surface
  tests four of them, picked by their power and evenly (three at the second surface, ...).
- `Spread` (0.5, 128): nine point and four spot lights (cones of 20 to 90 degrees, down,
  sideways and along the ground) over a ground of 120 m with a pillar of 70 m: the light grid
  has five by two by five regions, each listing the lights whose range reaches it (a spot
  light's where it reaches in front of the light, its cone not tested); 16 indirect samples,
  three bounces without the chance of stopping (four, three and two lights tested).
- `DirectionalLamps`, `DirectionalBounce`, `DirectionalSky`: directional lightmaps (Directional
  Mode Directional): a sun, a point light and a spot light over the lit scene with one bounce,
  the coloured scene under a sun with two bounces, and an ambient colour alone. A path's light
  comes from the direction the path left the texel in, the sky's straight from its own.
- `DirectionalMixed`: the coloured scene under a sun and an ambient colour, two bounces, 16
  indirect and 64 environment samples with picked directions: a texel's paths and its sky
  samples each weigh as one ray in where its light comes from, whatever their numbers.
- `Masks`, `MasksIndirect`, `MasksSuns`: mixed lights and their shadowmask channels: two suns
  (channels 0 and 1), five point lights in a row whose ranges overlap (two take the last two
  channels, the other three are baked in the Shadowmask Lighting Mode and stay mixed without a
  channel in Baked Indirect), a point light far away, two spot lights side by side whose
  pyramids do not meet (one channel), one whose pyramid reaches a point light, a mixed light
  without shadows (no channel, still mixed; in the light grid it comes before the lights with
  shadows) and a baked light; and five mixed suns (the fifth is baked). A light's channel and
  its mode as baked are compared with the bake input's.
- `MasksBright`: a bright mixed point light without shadows among five dimmer ones with
  them, all in one region of the light grid: the light without shadows comes first in the
  region's order whatever its colour. `MasksEdges`: mixed point lights whose spheres just meet
  (3.98 m apart, ranges 2: different channels); how far beyond its range a light's reach stops
  is not measured.
- `Far` (0.5, 128): a ground of 120 m 500 m from the origin, six point lights and a spot light
  near one end under a ceiling: the light grid's box starts from the scene's origin (its
  lights' reach alone would make one region), so the lights fall into two regions along x.
- `ModeIndirect`, `ModeSubtractive`, `ModeShadowmask`: a mixed sun and a mixed spot, a baked
  bulb and a realtime spot, in each Lighting Mode. Mixed lights' direct light is in the lightmap
  in Subtractive mode alone; the realtime light is not of the bake.

The scenes of indirect light have no sky and no ambient light either, materials of their own
(`Assets/Scenes/*.mat`, Standard shader) and 16 Direct Samples:

- `Bounce`, `BounceFar`, `BounceMany`, `BounceFew`, `BouncePoint` (4, 128): a grey ground, a
  white cube, a blue sphere and a red wall under a sun with Indirect Multiplier 1.5: one bounce
  of 64 Indirect Samples; four bounces (Min Bounces 2: paths end by chance from the second
  surface on); two bounces of 512 samples with Min Bounces 1; 10 samples asked for (a texel
  takes 8); and two bounces of one point light's (off whole coordinates, as the lamp in the
  sphere above).
- `Glow`: no light but emission: a lamp facing down with an emission map and an HDR colour,
  and panels whose materials are flagged BakedEmissive, RealtimeEmissive alone (it lights the
  baked lightmaps too), EmissiveIsBlack and with no flag (neither does).
- `Roof`: surfaces met from behind: a roof whose back faces the ground (the ground's rays
  that meet it bring nothing and count in `validity`), one with Double Sided Global
  Illumination, and a mirrored cube.
- `Textured`: Albedo Boost 2 and materials with textures: a rough ground with a ramp, a cube
  with a tiled checker, a metal sphere, a smooth wall.
- `Levels` (300, 512): a strip whose albedo image runs through every 8-bit level the bake can
  get (0 to 252: albedo is capped at 0.97): the linear value of each
  (`crates/unbound-gi/src/baked/levels.rs`).

The scenes of the sky's light are the coloured scene without a sun, with a slab that casts no
shadow (it stops the sky's light like any renderer) and 64 Indirect Samples:

- `Sky`, `SkyPicked`: an ambient colour, 64 Environment Samples, two bounces; without and with
  the Lighting Settings' environment importance sampling (directions picked by the sky's
  brightness).
- `SkyGradient`: the three colours of a gradient, 8 samples, picked directions.
- `SkyDrawn`, `SkyDrawnPicked`: the procedural skybox (`Assets/Scenes/SkyDrawn*.mat`) at
  ambient intensity 1 with 64 samples, and at 2.5 with 512 samples, three bounces and picked
  directions.
- `SkyNoBounce`: Max Bounces 0: no ray is sent, to the sky either.
- `Spare`: a sun and an ambient colour over the coloured scene and a roof of two submeshes with
  one material: the submesh without a material is in the lightmap, and no ray meets it (it
  casts no shadow, bounces nothing, counts in no validity and lets the sky through).
- `SpareEmpty`: `Spare`'s sun and sky over the coloured scene and a roof of three submeshes
  whose renderer has three material slots, a blue one, an empty one and a red one: the empty
  slot's submesh stops no ray either, and its material images draw it white (as
  Default-Material), where a submesh past the slots is left out of them.
- `SkyRealtime`, `SkyRealtimeBaked`: `Sky` with Realtime Global Illumination on as well, the
  environment's light kept in the realtime lightmaps (the Lighting Settings' default,
  [LightingSettings.realtimeEnvironmentLighting](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightingSettings-realtimeEnvironmentLighting.html))
  or in the baked ones: the bake's sky is black in the first, as in `Sky` in the second.

The scenes of light probes ([Light Probes](https://docs.unity3d.com/6000.0/Documentation/Manual/LightProbes.html),
[Light Probe Group](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LightProbeGroup.html):
the bake stores per probe the light at its point as L2 spherical harmonics; the pages give no
rule for where a probe is baked, for the groups' order or for the probes' samples) are the
ground, cube and sphere with 16 Direct Samples and no sky but an ambient colour:

- `Probes`: a baked sun, a baked point light and a mixed spot light; six probes of one group (one inside the sphere: its rays meet back faces), 64
  Indirect and Environment Samples, two bounces (Min Bounces 2).
- `ProbesMore`: the Subtractive Lighting Mode, 24 Indirect Samples (a probe takes 128), three
  bounces and two groups, one moved. A probe in the mixed spot
  light's cone takes none of its direct light: in this mode as in the others a mixed light's
  direct light is realtime for the objects probes light (the Lighting Mode page: "Real-time
  for dynamic GameObjects, baked for static GameObjects").
- `ProbesGrid`: a baked sun, 16 samples and one bounce; three groups whose order in the hierarchy differs from their names' and their
  objects' order in the scene file, one moved, one turned and scaled, with points between
  hundredths of a metre on both sides of zero; and a probe 8 m above the renderers under a
  baked point light that reaches it but not them.
- `ProbesSky`, `ProbesSkyEven`: the gradient sky (three colours: the probes' sky light is
  not the same every way), with directions picked by its brightness and without, in the
  Shadowmask Lighting Mode with a probe in the mixed spot light's cone (none of its direct
  light either); beside the probes' group a disabled group and a group of an inactive object,
  whose probes the bake leaves out.
- `ProbesReach`: five baked point lights over the renderers and a sixth 10 m away whose range
  reaches a probe beside it and no renderer: it is in the light grid's region only if the box
  takes the probes in, and the lightmaps' paths, which pick among more lights than they test,
  pick otherwise with it (the Editor's own bake input replayed: exact with it, 528 texels off
  by more than 1e-2 without).

What these show: the groups are baked in their objects' order in the scene file (their file
IDs'), each by itself (a probe's sample numbers are turned by its place in its group); a probe
is baked at its world point cut to whole hundredths of a metre towards zero; the light grid's
box takes the probes in with the renderers, for the lightmaps' paths too; a probe takes four
times the samples a texel takes, of its paths and of the sky, and its paths may end by chance
from their first surface on whatever the Min Bounces.

The scenes above them have the default sky (`Bare`, `Charts`, `Samples`, `Shapes`: the default
skybox, with the default Lighting Settings' picked directions) or none (a black sky: one picked
direction that brings nothing).

## Capture and comparison

`Assets/Editor/BuffersRecord.cs` opens each scene and bakes it. In the Editor
(`bake_buffers.py native`, batch mode on the GPU runner, with its `Data/Tools` folder on the
library path) the bake goes through the Editor's internal `UnityEditor.LightBaking` API by
reflection, in the two steps `Lightmapping.Bake` takes itself: `InputExtraction.ExtractFromScene`
makes the scene's bake input and lightmap requests, and `LightBaker.Bake` with the CPU backend
bakes them in the Editor's process and writes each lightmap's buffers unprocessed (`.r2d` files:
32-bit float RGBA, rows from the first row of the lightmap). The reference keeps per scene
(`Reference/unity-6000.0.71f1/<scene>/`):

- `lights.json`, the bake input's lights by their names in its order (each field as the Editor
  serializes it) and its Lighting Mode;
- `scene.json`, a digest of the bake input and requests (`editor_bake.py` reads them): the
  bake's result, what the Editor logged while it made the input, the instances in the bake's
  order, and each lightmap's size, samples per texel side and instances;
- `occupancy<N>.r2d` (1 in every channel of a used texel), `chartIndex<N>.int` (each texel's
  chart, -1 for none) and `normal<N>.r2d` (per sample, at the lightmap's size times its samples
  per side: the interpolated world normal, and the instance's place in the lightmap plus the
  triangle's number in its submesh; (0, 0, 0, -1000000) without a surface) and
  `irradianceDirect<N>.r2d` (per texel the direct light's red, green and blue, and 1 for a used
  texel), `irradianceIndirect<N>.r2d` (the same of the indirect light) and `validity<N>.r2d`
  (per texel the part of its rays that met a back face first) and
  `irradianceEnvironment<N>.r2d` (per texel the light its samples take straight from the sky),
  `directionalityDirect<N>.r2d` and `directionalityIndirect<N>.r2d` (where the direct light,
  and the indirect and sky's light, come from: the mean of their directions weighed by the
  light's luminance, and the mean of the texel's samples' cosines with it, each mapped from
  -1..1 to 0..1; 0.5 without light or without directional lightmaps) and `shadowmask<N>.r2d` (per channel the part of the texel's samples the channel's mixed light
  reaches that see it, in the Shadowmask Lighting Mode; zeros otherwise) as the Editor wrote them;
- `sky.json`, the bake input's sky: the side of its six faces, their colour when all texels
  are the same, else the faces in `sky.f32.z` (zlib of 32-bit float RGB, faces towards +x, -x,
  +y, -y, +z, -z, rows as a cube map stores them); the number of directions picked by its
  brightness and its summed brightness; `skyDirections.f32` has the picked directions and
  their light (3 + 3 floats each);
- `materials.json`, each instance's albedo and emission images as the bake input has them
  (size and texels, rows from the first), in the bake's order.
- `lightmap<N>.exr`, the lightmap the Editor's whole bake (`Lightmapping.Bake`, run after the
  buffers' capture) makes of them, as it writes it beside the scene (`Lightmap-N_comp_light.exr`:
  red, green, blue and alpha as half floats, rows from the top). Without filtering (the scenes'
  Lighting Settings), each used texel holds its direct, indirect and sky's light summed; a texel
  more of whose rays met back faces than the Lightmap Parameters' Backface Tolerance allows
  takes the mean of the light of its eight neighbours that hold their own, ring by ring (a ring's
  texels then hold light for the next); the texels around the charts take their neighbours'
  light the same way, as many rings as the Lighting Settings' padding (alpha 1; the texels
  further out have alpha 0); and the seams of a renderer's UVs are stitched (the texels along
  them and beside them change), which Unbound does not do.
- `probes.json`, the bake's light probes: their number, their points as the bake has them and
  the samples it gives them (null without probes); `radianceDirect0.shl2` and
  `radianceIndirect0.shl2`, per probe the nine coefficients of the direct light and of the
  light from other surfaces and the sky (red, green, blue and a fourth number each, coefficient
  by coefficient, after their count), and `validity0.float`, per probe the part of its rays
  that met a back face first. The Editor bakes each group's probes as a request of its own;
  the reference joins them in the order of the points.

Two captures make the reference (`bake_buffers.py publish`): their files byte-identical, but for
the buffers of light, which the Editor sums in an order that varies from bake to bake (repeats
differ in the last bits, up to 4e-7 here): those have the same used texels and values within
2e-6 (times the value above 1), the indirect light's within 1e-5 (sums of up to 512 samples of
several bounces: 3e-6 is the worst seen, in `BounceMany`) and so the sky's light, and the
first capture's is kept. The lightmaps the whole bakes write repeat within 1e-3 (times the
value above 1: half floats of bakes of their own; the worst seen is 5.2e-4, `Lamps`). The light
probes' coefficients are compared the same way (the worst
repeat is 8e-7, of the indirect light).
Publishing adds what is new and refuses to change what the reference has.

Unbound's bake (`Lightmapping.Bake`, `bake_buffers.py engine`: `unbound test --graphics`, a
session with a graphics device, which the materials' images need) writes the same buffers of its own
where `UNBOUND_BAKED_GI_BUFFERS` says and its layout to
`LibraryUnbound/BakedGI/<scene GUID>/atlas.json`. `bake_buffers.py check` compares exactly: the
bake's result, the logged messages (of Unbound's, those that are not its own notes of what its
bake does not make yet), the instances and their order, the lightmaps' sizes, samples and
instances, which texels are used, every texel's chart, which samples have a surface and each
sample's number, the lights and their order, each light's kind and mode and whether it casts
shadows. The normals are unit vectors compared within 1e-5 per component (the Editor turns them
into world space in an order of operations Unbound does not repeat). Of each light the numbers
Unbound's bake takes (colour, indirect colour, position, orientation, range, cone angle) are
compared within 2e-6 relative. A texel's direct light is compared within 1e-4 (times the value
above 1): last-bit differences of 32-bit arithmetic are multiplied by a narrow spot cone's falloff
(the worst here is 2.5e-5, in `Lamps`); the used texels are compared exactly. A texel's indirect
light is compared within 1e-3 likewise (Unbound sends the Editor's rays; with the Editor's own
bake input the worst texel is within 3e-4), its validity within 1e-6 (a count over its rays),
and every texel of every instance's material images within 1e-6, with the images' sizes. The
sky's light on a texel is compared as its indirect light is. The sky's faces are compared
within 1e-4 (times the value above 1): a drawn sky's come from the graphics device, which rounds
a triangle's interpolated colours its own way, and the ambient intensity multiplies their
gamma-encoded values (the worst seen is 1.8e-5 at intensity 1 and 4.8e-5 at 2.5); the number of
picked directions exactly, each direction, its light and the summed brightness within 1e-4
likewise. The light probes' points are compared within 1e-6 and their samples exactly, their
direct light within 1e-4 and their indirect light within 1e-3 (each times the value above 1),
their validity within 1e-6. Of the lightmap, which texels hold light (alpha) exactly, and their
light within 1e-3 as the indirect light's (times the value above 1), but for the texels whose
light in the Editor's lightmap is not what the rules above make of its own buffers
(`bake_buffers.py` `composite`): the stitching of seams changes those (along the seams of the
primitives' UVs, at chart borders and inside charts, as at the sphere's poles), which Unbound
does not do; they are counted (`lightmaps: N texels the Editor stitched`) and not compared. The rules are
the reference's own: a texel whose rays met back faces at most a tenth of the time keeps its
light (1740 such texels here, all but 37 of them, those by seams, as the rules make them), and
the scene without seams (`Charts`) has no texel the rules do not make. A bake that keeps no
light (`SkyRealtime`: no lights, the sky's light in the realtime lightmaps) writes no lightmap
(Unbound's buffers have one of zeros; not compared). A lightmap made of buffers with a named
residual differs where they do (the same residuals).
The Editor's lightmapper process now and then ends without writing a lightmap: the recorder
runs the whole bake again (up to three times), and a capture still without one is not
published.

`residuals.json` names the known differences, each with the exact text the comparison gives it
(another text, or none, fails the run); the lightmap made of a buffer with a residual differs
where the buffer does (and where its light is spread to), under the same name:

- `Spread`'s indirect light: 14 texels, mostly around the crate in the middle region of the
  light grid, differ as if a path's surface had picked another of its region's lights; the
  Editor's own bake input replayed gives the same differences. Not traced.
- One texel of `Roof`'s indirect light and one of `Glow`'s validity: a single ray that meets a
  triangle exactly on its edge falls on the other side of it than in the Editor.
- `Shapes`' sky light: one sky ray of some texels is stopped by a renderer of a less detailed
  LOD level, which the Editor's rays pass (the levels of LOD Groups are not told apart yet).
- One texel of `Probes`' indirect light (the ground's): it differs by about what one of its 64
  paths brings; the Editor's own bake input replayed gives the same difference. Not traced.
- The validity of `ProbesGrid`'s probe inside the cube: 5 of its 64 rays meet the cube's bottom
  and the ground under it at the same distance, and Unbound counts the ground's front face
  first, where the Editor counts the cube's back face for every ray.
- One value of one texel of `SkyGradient`'s indirect light: what one path's sky direction
  reading the neighbouring face of the sky would give (the faces differ in colour); the
  Editor's own bake input replayed gives the same difference.

`bake_buffers.py replay <capture>` is a development check of the baker's core alone: it feeds a
capture's own bake input (kept in the run folder, not in Git: meshes, placements, shadow casters,
lights, Lighting Mode and pushoff) to the `cargo run -p unbound-gi --example baked_gbuffer` tool
and compares its used texels, charts, samples and direct light with the capture's.

The sky Unbound's bake takes is the ambient source as `RenderSettings` has it at the bake: an
ambient colour or gradient as six faces of one colour each (the gradient's sky colour up, its
ground colour down, its equator colour on the four sides), a skybox drawn into six faces of
128 texels a side on the sky mesh (an octahedron split three times and twice more towards the
horizon: the procedural sky computes its colours per vertex, so the mesh shows), its
gamma-encoded values times the ambient intensity; a black sky where the realtime lightmaps
take the environment's light (`LightingSettings.realtimeEnvironmentLighting`).

Not baked by Unbound yet: ambient occlusion; without a graphics device, the indirect light (said). Not baked (said, each once per
scene): area lights, light cookies, soft baked
shadows (a Baked Shadow Angle or Radius: baked hard), lights whose intensity multiplies their
linear colour (GraphicsSettings.lightsUseLinearIntensity), the levels of LOD Groups (their
renderers take and stop light like any other; the Editor's rays tell the levels apart). Not done
and not said: materials that let light through stop it.

Not measured, of where the light comes from: mixed lights in the Shadowmask and Subtractive
modes, ambient occlusion and emission in a directional bake, a project with a render pipeline
asset.

Not measured, of light probes: groups of prefab instances or of several scenes (their order),
negative file IDs, probes beyond the light grid's 20 regions a side, a mixed light baked for
want of a shadowmask channel (taken as a baked light), the probes' occlusion of mixed lights
(`lightProbeOcclusion`, `mixedLightOcclusion`: not compared), a group's Remove Ringing (on
in every group here), Max Bounces 0 (Unbound's probes then take no sky light, as the lightmaps
in `SkyNoBounce`), a drawn skybox, and what the bake makes of the buffers for the scene's
Light Probes.

Not measured, of the sky's light: a skybox other than the procedural one (a cube map, six
images, a panorama: drawn the same way), an ambient source that is not the scene's sky
(`RenderSettings.customReflectionTexture` plays no part), a sky brighter in one place than
100000 draws find (the last draw is taken), gamma colour space projects.

Not measured, of the indirect light: materials that let light through (they stop it), LOD
Groups, a material flagged both emissive and black, shaders without a meta pass (said; the
renderer adds no albedo or emission), meshes without lightmap UVs, textured renderers that have
no lightmap, the light grid's pick among several point and spot lights (see the residuals).

Not measured: Direct Samples that are no power of two or fewer than a texel's samples (Unbound
takes the mean of a texel's samples whatever the count), gamma colour
space projects, light colour temperatures, the light of samples whose normals have no length or whose
triangles have no area (the scenes with such meshes have no lights), the points and the triangles' own normals the texels' samples stand for (the
Editor writes neither; Unbound derives them with the interpolated normals, for the bake's
lighting to come), several open scenes, renderers of prefab instances (their order in the bake),
Anti-aliasing Samples below 1, meshes with UVs that are not numbers or with other vertex formats
the Editor refuses, triangles so thin that they cover no measurable part of any texel, Terrains,
the Progressive GPU lightmapper, macOS.
