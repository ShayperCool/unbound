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
  reach.
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
  `irradianceEnvironment<N>.r2d` (per texel the light its samples take straight from the sky)
  as the Editor wrote them;
- `sky.json`, the bake input's sky: the side of its six faces, their colour when all texels
  are the same, else the faces in `sky.f32.z` (zlib of 32-bit float RGB, faces towards +x, -x,
  +y, -y, +z, -z, rows as a cube map stores them); the number of directions picked by its
  brightness and its summed brightness; `skyDirections.f32` has the picked directions and
  their light (3 + 3 floats each);
- `materials.json`, each instance's albedo and emission images as the bake input has them
  (size and texels, rows from the first), in the bake's order.

Two captures make the reference (`bake_buffers.py publish`): their files byte-identical, but for
the buffers of light, which the Editor sums in an order that varies from bake to bake (repeats
differ in the last bits, up to 4e-7 here): those have the same used texels and values within
2e-6 (times the value above 1), the indirect light's within 1e-5 (sums of up to 512 samples of
several bounces: 3e-6 is the worst seen, in `BounceMany`) and so the sky's light, and the
first capture's is kept.
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
likewise.

`residuals.json` names the known differences, each with the exact text the comparison gives it
(another text, or none, fails the run):

- `Lamps` indirect light: several point and spot lights reach a surface; the Editor seems to
  take at most four of them there (fewer at later bounces), picked by their power, Unbound all
  of them. Whether both give the same light on average is not measured.
- One texel of `Roof`'s indirect light and one of `Glow`'s validity: a single ray that meets a
  triangle exactly on its edge falls on the other side of it than in the Editor.
- `Shapes`' sky light: one sky ray of some texels is stopped by a renderer of a less detailed
  LOD level, which the Editor's rays pass (the levels of LOD Groups are not told apart yet).
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
gamma-encoded values times the ambient intensity.

Not baked by Unbound yet: ambient occlusion, directionality and the
shadowmask; without a graphics device, the indirect light (said). Not baked (said, each once per
scene): area lights, light cookies, soft baked
shadows (a Baked Shadow Angle or Radius: baked hard), lights whose intensity multiplies their
linear colour (GraphicsSettings.lightsUseLinearIntensity), the levels of LOD Groups (their
renderers take and stop light like any other; the Editor's rays tell the levels apart). Not done
and not said: materials that let light through stop it.

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
