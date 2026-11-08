# Legacy TextMesh

Measures what a TextMesh generates in Play mode in the Unity 6000.0.71f1 Editor and compares the
engine with it. `Assets/Scripts/TextMeshProbe.cs` adds one TextMesh per case (the documented
properties one at a time: text, fontSize, characterSize, lineSpacing, all nine anchors, the three
alignments with two anchors, tabSize including fractional sizes, offsetZ, the b/i/size/color rich
text tags and richText off, fontStyle, color, empty, whitespace and newline texts, a carriage
return, a glyph the font lacks, null text and font, a script-made custom font, a renderer that
already has a material) and records:

- the hidden mesh the TextMesh generates (found as the one new Mesh object): name, flags, vertex
  count, triangles, positions, colours, normals/tangents/uv2 counts, bounds; per quad the uv size
  in texels of the font texture, and for plain text whether its uvs are the character's own
  (`GetCharacterInfo`; where the atlas packs a glyph is the font's business);
- the renderer's bounds and materials, the font property, and the warnings and errors logged;
- `GetCharacterInfo` of a few requested characters, the custom font's properties, and whether
  the mesh object is kept when the text changes;
- two 256x128 images of a rich-text TextMesh: with the built-in "Font Material" and with an
  unlit colour material (every glyph quad a filled box).

Contract: Unity 6000.0 Scripting API
[TextMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextMesh.html) and Manual
[Text Mesh](https://docs.unity3d.com/6000.0/Documentation/Manual/class-TextMesh.html) document the
properties (characterSize "scales the whole text", anchor "which point of the text shares the
position of the Transform", tabSize "a multiplum of the 'spacebar' character offset", fontSize and
fontStyle for dynamic fonts only). Everything else here is observed in this capture of this
release; the layout rules derived from it are listed in `managed/UnityEngine/Text/TextMesh.cs` and
`TextLayout.GenerateTextMesh`.

```sh
python3 tests/projects/textmesh/run.py record --label native-a   # Editor, GPU display
python3 tests/projects/textmesh/run.py record --label native-b
python3 tests/projects/textmesh/run.py publish native-a native-b  # identical repeats only
python3 tests/projects/textmesh/run.py gate                       # engine vs Reference/
```

Numbers compare with an absolute tolerance of 1e-4. `residuals.json` lists the keys that still
differ, all from the shared legacy glyph rasterizer (UI Text uses it too): some bitmaps are a
pixel taller or narrower in Unity (cap heights, `l`, `W`), Unity stores some glyphs rotated in its
texture, and the space advances 3 pixels at 13 px in Unity and 4 in the engine (tabs and trailing
spaces follow). The layout itself (bounds of text without spaces, the custom-font cases, line
steps, anchors, alignment, tab stops) matches. The images are printed as measurements.
