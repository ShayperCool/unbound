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
- an independent imported bitmap font requested at its exact 109-point strike twice, including
  glyph metrics, UVs, Alpha8 texture dimensions and captured pixels;
- the logs of the run;
- 72 independent generator controls with fresh fonts: updated and unchanged bounds, overflowing
  and bounded layout, upper-left/centre/lower-right anchors, pivots 0/0.5/1, two requested heights,
  two lines with different widths; ten texture-history controls cover text without whitespace,
  reversed glyph order, a size tag, and empty text.

Unity 6000.0 Scripting API sources: [FontEngine](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.LowLevel.FontEngine.html),
[GlyphRenderMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.LowLevel.GlyphRenderMode.html),
[Font.RequestCharactersInTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font.RequestCharactersInTexture.html),
[Font.textureRebuilt](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font-textureRebuilt.html),
[TextGenerationSettings.updateBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextGenerationSettings-updateBounds.html),
[TextGenerationSettings.generationExtents](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextGenerationSettings-generationExtents.html) describe the API.
The latter fields request updated text bounds and a fitting rectangle; the placement and texture
history details below are observations from this 6000.0.71f1 capture.
A bitmap-only face takes its strike's size whatever is
asked (face info and font assets report 109 points); colour atlases hold the colour bitmaps with
their colours premultiplied by alpha; UI Toolkit draws text of a non-Alpha8 atlas as a textured
mesh; a legacy request without an exact bitmap strike has no pixels and its unscaled advance;
a request at the exact 109-point strike has Alpha8 coverage pixels in a 256 x 512 texture; the
importer's character spacing widens the texture packing, not the advances; a canvas requests its
text at the font size times the scale factor only; a full 4096 x 4096 legacy texture is neither
grown nor reset (no rebuild event), and each request that does not fit logs an error. A generator
uses updated bounds on overflowing axes when translating its pivot and aligning its lines.
Consequently the original one-line case, with height 100 and pivot 0, has a top of 36 instead
of 100. With unchanged bounds the requested extents remain in effect. Bounded stored extents
include a 0.0001 fitting margin, which can decide a half-pixel rounding tie. A generator also
caches the base-size space before requesting the text, including empty text and text containing
only differently sized glyphs. This determines later atlas packing. These mechanisms are enabled
through Compat only for the measured release; other releases retain the prior unmeasured path.

```sh
python3 tests/projects/textcolor/run.py record --label native-a
python3 tests/projects/textcolor/run.py record --label native-b
python3 tests/projects/textcolor/run.py publish native-a native-b
python3 tests/projects/textcolor/run.py gate
```

Numbers compare within 1e-4. The appended generator controls preserve all 19 earlier reference
rows byte for byte. The direct-generator placement and subsequent packing residuals are resolved;
the earlier OS fallback reload warning also no longer occurs. Measured on the NVIDIA driver only.

The exact-strike control uses a separate imported font asset so its atlas starts empty. Two
independent GPU Editor repeats are byte-identical. It adds one observation while preserving
all 102 preceding reference rows and the panel image byte for byte; Unbound already matched
this behavior. COLR vector glyphs, several strikes per face and a full 4096 legacy texture
retaining glyphs from older frames remain unmeasured.
