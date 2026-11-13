# Serialized Sprite assets

Unity 6000.0.71f1 authors two ClassId213 `.asset` Sprites referencing a TextureImporter
Texture2D by GUID and fileID. Rectangle has a subrectangle, off-centre pivot, 24 pixels
per unit and four different borders. Polygon is a clone of an imported Tight Sprite
with seven vertices. The scene holds a Sprite reference and two SpriteRenderers;
Resources loads both assets and the original imported Sprite. The capture includes
API values, exact geometry/index order, physics shapes and a 160x96 render.

Saved layouts (`layouts`, rendered by a second camera into a 192x128 `layoutImage`):
clones of two SpriteAtlas members packed in the Editor (tight packing and rotation
allowed: the atlas and its packed render data are saved beside the Sprite's own), a
skinned clone (two bones, bind poses, blend weights and indices), a clone with colour,
normal and tangent channels, clones of a downscaled texture's Sprite and of a Sprite
with a `_NormalMap` secondary texture, a copy of Rectangle with vertex data version 2
(what earlier Editors saved), and a Sprite saved in the scene whose texture was never
an asset (a null texture reference). The capture records bones, bind poses, the
channels `HasVertexAttribute` reports and their values, and secondary textures.

The gate assembles committed `Authored/` inputs into a private cache project and runs
only the engine. It never starts the Editor. Outputs and all differing values stay
under `$XDG_CACHE_HOME/unbound/validation/runs/spriteassets/runs/`.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/spriteassets/run.py author --label author
python3 tests/projects/spriteassets/run.py native --label native-a
python3 tests/projects/spriteassets/run.py native --label native-b
python3 tests/projects/spriteassets/run.py publish native-a native-b
UNBOUND=/path/to/this/checkouts/unbound python3 tests/projects/spriteassets/run.py check
```

`author` is for deliberate fixture changes only. It uses `tests/authored_project.py
record`; Native repeats use `assemble`, load the saved assets in fresh Editors and
must agree byte for byte before one reference is published. Numeric comparisons use
1e-5 relative to max(1, abs(reference)); image channels use 2/255. All missing, extra
and failing values are retained in `differences.json`.

| Source | Contract or observation | Reader decision |
| --- | --- | --- |
| [6000.0 Sprite API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite.html) | Documented: rect and pivot use pixels; pixelsPerUnit scales to world units; border, texture, bounds, vertices, triangles and UVs expose the Sprite's data. | Preserve authored data and referenced texture identity. |
| [6000.0 textureRect](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite-textureRect.html) | Documented: tightly packed atlas sprites throw on textureRect. This is an atlas limitation, not every Tight mesh. | Ordinary un-atlased Tight meshes are supported; atlas representations are rejected. |
| [6000.0 SpriteRenderer](https://docs.unity3d.com/6000.0/Documentation/Manual/class-SpriteRenderer.html) | Documented: the Sprite property selects the rendered graphic; Simple mode scales it uniformly. | Compare a render using committed scene references. |
| [6000.0 serialized file format](https://docs.unity3d.com/6000.0/Documentation/Manual/FormatDescription.html), [asset metadata](https://docs.unity3d.com/6000.0/Documentation/Manual/AssetMetadata.html) | Documented: documents identify objects; asset metadata preserves identities and references. Internal Sprite layout is not an API guarantee. | Resolve the authored fileID and GUID through the normal resolver. |
| Native repeats, 6000.0.71f1 | Observed: RD v3, vertex data v3, rect/submesh v2, Float32 positions in aligned streams, UInt16 triangle indices, normalized stored pivot, physics points in units. Stored UV channels contain zeros unless SetVertexAttribute wrote the sprite's own (settingsRaw bit 7, then read back as stored); uvTransform maps positions to texture pixels. | Use serialized mesh positions/indices and the UV transform, with strict version/layout checks. Share the binary built-in Sprite mesh decoder. |
| Native repeats, 6000.0.71f1 | Observed: Sprite.Create plus OverrideGeometry does not persist its overridden runtime mesh in this asset form. Cloning the imported Tight Sprite does persist its mesh. | Author through the Editor; capture saved assets in independent sessions. No claim about other releases or representations. |
| Native repeats, 6000.0.71f1 | Observed: an atlas member's clone keeps the atlas and its packed m_AtlasRD (settingsRaw 65/1) but reports `packed` false and the texture, rect, UVs and mesh of m_RD (the reference records Edit mode with the packer off; a separate Play-mode capture with atlases always enabled, outside this reference, gave the same). Saved channels are exactly those reported: colours (four bytes) multiply the render; normals, tangents (floats) and blend weights (four floats with four 32-bit indices) are kept; texture coordinates stay stored as zeros. A downscaled texture's clone saves `downscaleMultiplier` 1. A null texture gives zero UVs and draws nothing. | Read m_RD only; keep the saved channels, bones, bind poses and secondary textures; vertex data versions 2 and 3. |

Later layouts (`laterLayouts`, rendered by a third camera into a 288x64 `laterLayoutImage`):
a copy of Rectangle keeping three scriptable objects (`AddScriptableObject`; one deleted after
the save reads back as null), a clone with texture coordinates 1 to 7 (`SetVertexAttribute`
of Vector2 values), a clone with its own texture coordinates 0 (saved with settingsRaw bit 7,
read back as stored), clones of a 0.5 variant atlas member and of a split-alpha atlas member
packed for Android (both keep the downscale and the alpha texture in m_AtlasRD only), and
`StoredUv`, a text copy of Rectangle with nonzero stored UVs and bit 7 clear (the Editor
ignores them and derives the UVs from uvTransform). `scriptableObjectApi` records the list
API on a copy of Rectangle: a repeated object is not added again (documented for
`AddScriptableObject`), an object the list holds is not set again (also at its own index),
indices past the end and no object, a destroyed entry, and a copy edited after Instantiate.

Packed or rotated m_RD, split-alpha or downscaled m_RD (the Editor keeps those only in
m_AtlasRD), legacy Sprite Packer atlas names (the 6000 Editor's legacy packer modes leave a
tagged sprite unpacked, also in Play mode), other vertex channels or formats and unfamiliar
render-data representations still fail with an error naming the asset and fileID and the
reason; the Editor produced none of them here. They produce no default Sprite. No
release differences were established here; an actual release difference must use Compat.
The doctor's layout check uses the same decoder, without resolving texture dimensions.
