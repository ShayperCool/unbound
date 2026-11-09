# Legacy dynamic glyph coverage

Measures Unity 6000.0.71f1 `Font.GetCharacterInfo`, `Font.characterInfo` and oriented alpha
pixels read back from the font texture. The reference covers eight font/mode fixtures, ten
sizes (8–96 px), four styles, spaces, narrow/wide letters, descenders and accented glyphs.
Liberation Sans and Serif exercise real family faces; Styled is intrinsically bold italic.
Synthetic renames the Liberation Sans family to RasterTest so installed family styles cannot
replace it. `FONT-LICENSE.txt` covers the supplied fonts. Hinted, Raster and OS use the same
font bytes with different importer rendering modes.

The existing default-font case measures system-backed glyphs while retaining the resource
face's ascent and line-height metrics. The release compatibility profile selects that fallback
policy. Font face selection is cached; existing TextCore glyph-index and distance-field paths
keep their original face and renderer.

| Observation | Matching configuration |
| --- | --- |
| Size | 26.6 character size, truncated after multiplying by 64; 72 dpi |
| Smooth / OS mode | FreeType `FT_LOAD_NO_HINTING`, normal grayscale rendering |
| Hinted smooth | Default load flags, normal grayscale rendering |
| Hinted raster | Monochrome target and monochrome rendering |
| Italic synthesis | Horizontal shear 0.25, only without an intrinsic italic face |
| Bold synthesis | Bitmap emboldening by max(size / 55, 1), unchanged advance; logical top moves by that strength |
| Metrics | Bitmap bearings and dimensions, original horizontal advance; existing CharacterInfo rounding and one-pixel padding |

The implementation uses the bundled FreeType 2.13.2 in freetype-sys 0.23.0. These are measured
settings for this release and fixture set. Other releases, colour glyphs and atlas packing
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
