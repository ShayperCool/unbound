# Legacy dynamic glyph coverage

Measures Unity 6000.0.71f1 `Font.GetCharacterInfo`, `Font.characterInfo` and oriented alpha
pixels read back from the font texture. The reference covers eight font/mode fixtures, ten
sizes (8–96 px), four styles, spaces, narrow/wide letters, descenders and accented glyphs.
Liberation Sans and Serif exercise real family faces; Styled is intrinsically bold italic.
Synthetic renames the Liberation Sans family to RasterTest so installed family styles cannot
replace it; SyntheticHinted and SyntheticRaster are the same face imported as Hinted Smooth and
Hinted Raster, so synthetic bold and italic are covered in every rendering mode. `FONT-LICENSE.txt` covers the supplied fonts. Hinted, Raster and OS use the same
font bytes with different importer rendering modes.

The built-in LegacyRuntime font holds no font data in the Editor: its ascent and line height
come from the resource face, its glyphs from OS faces. A character a font's own face lacks
(and every LegacyRuntime character) is looked up per character in the font's fallback fonts,
the OS faces of its font names, then the OS fallback families in order: Arial, Arial Unicode
MS, Helvetica Neue, Microsoft Sans Serif, Microsoft YaHei, Malgun Gothic, Gulim, MS Gothic,
Nirmala UI, the Linux families (FreeSans, Droid Sans, DejaVu Sans, Liberation Sans, WenQuanYi
Micro Hei, HYSong, NanumGothic, TakaoGothic, Noto Sans CJK JP/KR/SC/TC, Droid Sans Fallback,
Noto Color Emoji) and LastResort. A fallback face renders in the requesting font's rendering
mode. A bold or italic request uses the installed face of the same family with that style when
there is one (an intrinsic style is never synthesized again). The `fallback/*` keys cover
characters missing from Liberation Sans (∀ in FreeSans, ⌘ in DejaVu Sans, 直 in Noto Sans CJK
JP) for LegacyRuntime, a smooth and a hinted project font, normal and bold. The release
compatibility profile selects this fallback policy; TextCore's glyph-index and distance-field
paths keep their own face and renderer.

`Font.HasCharacter` of a dynamic font answers true for every character above U+001F, whether a
face has it or not (`fallback/*/has/*`; U+FFFE is in no installed face).

On macOS the same chain was measured with the Unity 6000.0.71f1 macOS arm64 Editor in batch
mode on macOS 26's own fonts (not a reference: the Linux faces the reference needs are not
installed there): Unbound's 9672 glyph metrics and raw values match the Editor's, the fallback
characters included. Arial Unicode MS, one of the common families, has all three of them there,
so this does not reach the platform part of the list. tests/projects/osfontfallback, recorded on
macOS, covers that part: ⌚ comes from Arial Unicode MS, while ⏰, ☕, ⭐ and characters only other
macOS faces have (Khmer, Ethiopic, Mongolian; CJK extension B is outside UTF-16 code units) get
an empty glyph in both, as a character no face has does. The macOS LastResort face is named
`.LastResort`, so the list's LastResort finds no face. Unbound lists the macOS font directories'
faces itself, named as FreeType names them; after that change all 19,422 metric values of this
probe still match the macOS Editor's on macOS 26. The alpha bytes come from a
screen read-back that neither the batch-mode Editor without a graphics device nor a headless
Unbound run has, so they were not compared. A Mac with the Linux families installed is
unmeasured.

The reference depends on the OS fonts of the recording host (Debian/Ubuntu packages
fonts-freefont-ttf, fonts-dejavu-core, fonts-liberation, fonts-noto-cjk). `run.py record` and
`run.py gate` refuse to run, naming the missing faces, when one of them is not installed.

| Observation | Matching configuration |
| --- | --- |
| Size | 26.6 character size, truncated after multiplying by 64; 72 dpi |
| Smooth / OS mode | FreeType `FT_LOAD_NO_HINTING`, normal grayscale rendering |
| Hinted smooth | Default load flags, normal grayscale rendering |
| Hinted raster | Monochrome target and monochrome rendering |
| Italic synthesis | Horizontal shear 0.25, only without an intrinsic italic face |
| Bold synthesis | Bitmap emboldening by max(size / 55, 1), unchanged advance; logical top moves by that strength |
| Metrics | Bitmap bearings and dimensions, original horizontal advance; existing CharacterInfo rounding and one-pixel padding |
| Other bitmap layouts | Converted to 8 bits per pixel; fewer than 256 gray levels stretched to 0..255 |

The implementation uses the bundled FreeType 2.13.2 in freetype-sys 0.23.0 with the settings
above; all measured glyphs match byte for byte. Other releases, colour glyphs and atlas packing
parity remain unmeasured. The reference compares glyph coverage after applying the public UV
corners, independently of packing position and rotation. Full raw atlas PNGs stay in the run
cache outside the canonical capture. No alpha adjustments or per-character corrections apply.

The Unity 6000.0 documentation for
[RequestCharactersInTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font.RequestCharactersInTexture.html)
and [GetCharacterInfo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font.GetCharacterInfo.html)
requires the requested size/style to be present in the texture. The `request/before` and
`request/after` control uses a fresh size/character pair and observes absence followed by
presence. Each requested glyph is also checked against its `characterInfo` array entry.
These guarantees are distinct from the measured raster settings above. Texture rebuild
retention is outside this change.

```sh
python3 tests/projects/glyphraster/run.py record --label native-a
python3 tests/projects/glyphraster/run.py record --label native-b
python3 tests/projects/glyphraster/run.py publish native-a native-b
python3 tests/projects/glyphraster/run.py gate
```

Publishing requires byte-identical independent captures. Every metric, raw fractional value
and oriented alpha byte compares exactly (zero tolerance). Regression checks also run
`textmesh`, `uicolors` in both colour spaces and `uitextmetrics`.
