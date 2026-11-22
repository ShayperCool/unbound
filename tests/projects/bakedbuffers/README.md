# Baked lightmap buffers probe

Unity 6000.0.71f1, built-in render pipeline, Progressive CPU lightmapper. What a bake in Edit
mode knows of each baked lightmap before it lights it: which texels are used, the chart of each,
the surface normal at each of a texel's samples, and the order the bake gives its renderers.
Unity documentation (6000.0):
[Lightmapping.Bake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Lightmapping.Bake.html),
[Lightmap Parameters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LightmapParameters.html)
(Anti-aliasing Samples: "the number of times to supersample a texel to reduce aliasing"; the
ranges the page gives for 2× and 4× are not the release's, see `Samples` below),
[Lightmap UVs](https://docs.unity3d.com/6000.0/Documentation/Manual/LightingGiUvs.html) (charts),
[Mesh Renderer](https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshRenderer.html).
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

## Capture and comparison

`Assets/Editor/BuffersRecord.cs` opens each scene and bakes it. In the Editor
(`bake_buffers.py native`, batch mode on the GPU runner, with its `Data/Tools` folder on the
library path) the bake goes through the Editor's internal `UnityEditor.LightBaking` API by
reflection, in the two steps `Lightmapping.Bake` takes itself: `InputExtraction.ExtractFromScene`
makes the scene's bake input and lightmap requests, and `LightBaker.Bake` with the CPU backend
bakes them in the Editor's process and writes each lightmap's buffers unprocessed (`.r2d` files:
32-bit float RGBA, rows from the first row of the lightmap). The reference keeps per scene
(`Reference/unity-6000.0.71f1/<scene>/`):

- `scene.json`, a digest of the bake input and requests (`editor_bake.py` reads them): the
  bake's result, what the Editor logged while it made the input, the instances in the bake's
  order, and each lightmap's size, samples per texel side and instances;
- `occupancy<N>.r2d` (1 in every channel of a used texel), `chartIndex<N>.int` (each texel's
  chart, -1 for none) and `normal<N>.r2d` (per sample, at the lightmap's size times its samples
  per side: the interpolated world normal, and the instance's place in the lightmap plus the
  triangle's number in its submesh; (0, 0, 0, -1000000) without a surface) as the Editor wrote
  them.

Two captures whose reference folders are byte-identical make the reference
(`bake_buffers.py publish`).

Unbound's bake (`Lightmapping.Bake`, `bake_buffers.py engine`) writes the same buffers of its own
where `UNBOUND_BAKED_GI_BUFFERS` says and its layout to
`Library/Unbound/BakedGI/<scene GUID>/atlas.json`. `bake_buffers.py check` compares exactly: the
bake's result, the logged messages (of Unbound's, those that are not its own notes of what its
bake does not make yet), the instances and their order, the lightmaps' sizes, samples and
instances, which texels are used, every texel's chart, which samples have a surface and each
sample's number. The normals are unit vectors compared within 1e-5 per component (the Editor
turns them into world space in an order of operations Unbound does not repeat).

`bake_buffers.py replay <capture>` is a development check of the rasterizer alone: it feeds a
capture's own bake input (kept in the run folder, not in Git) to the
`cargo run -p unbound-gi --example baked_gbuffer` tool and compares its texels with the capture's.

Not measured: several open scenes, renderers of prefab instances (their order in the bake),
Anti-aliasing Samples below 1, meshes with UVs that are not numbers or with other vertex formats
the Editor refuses, triangles so thin that they cover no measurable part of any texel, Terrains,
the Progressive GPU lightmapper, macOS.
