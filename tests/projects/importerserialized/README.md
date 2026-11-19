# SerializedObject over a TextureImporter

What editor code reads and writes through `new SerializedObject(textureImporter)`: the 2D sprite
data provider (`com.unity.2d.sprite` `TextureImporterDataProvider`, which `OnPostprocessSprites`
hooks of `com.unity.2d.animation` and the PSD importer call) reads `m_SpriteSheet`, its sprites and
their name/file-ID table through it.

`Assets/Editor/ImporterSerializedProbe.cs` records, for a Default texture (`Plain`), a Single
sprite (`Single`) and a sheet of three sprites (`Sheet`, sliced by `create` through the sprite data
provider as the Sprite Editor does):

- `value/<texture>/<path>`: every property (strings not entered), its `SerializedPropertyType`,
  type name and value;
- `schema/<texture>/<path>`: the tree with every array emptied and given one new element, with
  name, type, depth, editability, visibility and a new element's value (`schema.py` turns the
  sheet's into `managed/UnityEngine/Editor/Data/importer-types-6000.0.json`);
- `provider/<texture>/...`: what the sprite data provider reads (sprite rects, the name/file-ID
  table, outlines, secondary textures) for the sprite textures;
- `edit/...`: a sprite renamed and the pixels per unit changed through a SerializedObject and
  applied: what the importer and a new SerializedObject read then (nothing is saved).

Measured with the Unity 6000.0.71f1 Linux Editor (two identical GPU captures): the tree is the
importer's native one (`m_MipMapMode`, `m_SpriteSheet.m_Sprites`, ...), flat where the `.meta`
groups fields (`mipmaps`, `bumpmap`), named with `m_` and an upper-case first letter where the
`.meta` has neither; `m_Output` is the import's result (the source's size and alpha, the formats
chosen) and `m_UsedFileIDs` the file IDs it used, neither in the `.meta`. An applied edit reaches
the importer's properties, a new SerializedObject and leaves the importer dirty.

    python3 tests/projects/importerserialized/run.py create --release 6000.0.71f1   # authoring: slice, copy the .meta files back
    python3 tests/projects/importerserialized/run.py editor --release 6000.0.71f1 --label <L>
    python3 tests/projects/importerserialized/run.py publish --release 6000.0.71f1 --label <L>
    python3 tests/projects/importerserialized/schema.py \
        tests/projects/importerserialized/Reference/unity-6000.0.71f1/values.jsonl \
        managed/UnityEngine/Editor/Data/importer-types-6000.0.json
    UNBOUND_CLI=<unbound> python3 tests/projects/importerserialized/run.py unbound --release 6000.0.71f1

Values compare exactly. Unbound on Linux, 2026-11-19: 1723/1738; the 15 differences are named in
`Reference/unity-6000.0.71f1/known-gaps.json` (the used file IDs, two output formats of the Default
texture, the obsolete `spritesheet` getter). Not measured: other importer classes, other releases
(their SerializedObject keeps reflecting over the managed importer).
