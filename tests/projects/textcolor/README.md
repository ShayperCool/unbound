# Colour glyphs and legacy font cases

Measures what the Unity 6000.0.71f1 Editor does with a colour emoji font and with a few legacy
dynamic-font cases, and compares the engine with it. `Assets/Resources/Fonts` holds three glyphs
of Noto Color Emoji (bitmap-only: one 109 ppem colour strike, renamed "Emoji Probe") and a
subset of Liberation Sans imported twice, with character spacing 0 and 6 (`FONT-LICENSE.txt`).
`ColorRunner.Record` enters Play mode, where `TextColorProbe` (from the first frame's `Start`)
records:

- the font engine with the emoji face at 12, 24, 64, 109, 109.5 and 150 points: the load's result,
  the face info, and the smiling face's glyph loaded with and without colour and unscaled;
- a font asset per render mode (COLOR, COLOR_HINTED, SMOOTH, SDFAA; 24 points, padding 4,
  256 x 256): which emoji fit, the atlas format, the face's point size, each glyph and its atlas
  texels (RGBA for colour atlases);
- the emoji font as a legacy dynamic font: a heart at 32 and 64 pixels, its character info and the
  font texture around it;
- the two spacing fonts: character info (their texture packing) and a TextGenerator's layout;
- the sizes a canvas with scale factor 1.5 requests for a Text of size 20, and its vertices;
- the built-in legacy font driven past its largest texture (800 characters at size 300 in batches
  of 100): the texture size and rebuild count after each batch, which characters are held, and the
  errors logged;
- a UI Toolkit panel (`emoji-panel.png`) drawing the three emoji with a COLOR font asset, an SDFAA
  one and the Font itself (atlases large enough for all three, so no OS fallback font is used);
- the logs of the run.

Documented (Unity 6000.0 Scripting API: [FontEngine](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.LowLevel.FontEngine.html),
[GlyphRenderMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.LowLevel.GlyphRenderMode.html),
[Font.RequestCharactersInTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font.RequestCharactersInTexture.html),
[Font.textureRebuilt](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font-textureRebuilt.html)):
the API. Observed in this capture only: a bitmap-only face takes its strike's size whatever is
asked (face info and font assets report 109 points); colour atlases hold the colour bitmaps with
their colours premultiplied by alpha; UI Toolkit draws text of a non-Alpha8 atlas as a textured
mesh; a legacy font's glyph on a bitmap-only face has no pixels and its unscaled advance; the
importer's character spacing widens the texture packing, not the advances; a canvas requests its
text at the font size times the scale factor only; a full 4096 x 4096 legacy texture is neither
grown nor reset (no rebuild event), and each request that does not fit logs an error.

```sh
python3 tests/projects/textcolor/run.py record --label native-a
python3 tests/projects/textcolor/run.py record --label native-b
python3 tests/projects/textcolor/run.py publish native-a native-b
python3 tests/projects/textcolor/run.py gate
```

Numbers compare within 1e-4. `residuals.json` lists the open differences outside these cases
(a TextGenerator called directly, the packing order after it, and an OS fallback face the engine
cannot reload by name), each printed on every run. Measured on the NVIDIA driver only.
