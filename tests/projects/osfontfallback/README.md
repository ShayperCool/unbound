# OS font fallback on macOS

Measures which macOS faces fill in characters a font lacks, with the Unity 6000.0.71f1 macOS
arm64 Editor in batch mode (no graphics device) on macOS 26 with its own system fonts. The
reference was recorded on macOS: two independent captures were byte-identical. The values depend on
the installed system fonts, so recording and the engine comparison run on macOS only: on another
host `run.py gate` reports that it was not run and why, and `run.py check-reference` (a fast
check on every host) only verifies that the committed reference is complete and holds every key
`residuals.json` lists.

Observations, for the built-in LegacyRuntime font and a project font (Liberation Sans, `Sans`):

- `osfallbacks`: TextCore's OS fallback family list (`Font.GetOSFallbacks`). On macOS it is the
  common families of the Linux list (Arial, Arial Unicode MS, Helvetica Neue, Microsoft Sans Serif,
  Microsoft YaHei, Malgun Gothic, Gulim, MS Gothic, Nirmala UI), then Apple Color Emoji and
  LastResort.
- `legacy/<font>/<size>/<char>`: `Font.GetCharacterInfo` metrics at 16 and 32 pixels for Latin,
  mathematical and technical signs, CJK, Indic, Hebrew, Arabic, Thai, Hangul and scripts no fallback family has (Khmer,
  Ethiopic, Mongolian, Tibetan, Sinhala, Myanmar, Cherokee, Canadian syllabics, Yi, Thaana, Ogham,
  Runic, Tifinagh, N'Ko, Vai) and U+FFFE. A character no fallback face has gets an empty glyph (no
  advance, a 2 x 2 padded rectangle), as U+FFFE does; ⏰, ☕ and ⭐ do too, so Apple Color Emoji does
  not serve legacy text. Astral characters (CJK extension B, Adlam, Deseret, emoji) are not
  requested: the legacy font system takes UTF-16 code units.
- `textcore/<font>`: `FontAsset.CreateFontAsset(font)`: the asset's face, its fallback table and,
  per character (astral ones included), the first asset of the chain having it with its glyph
  metrics. The built-in LegacyRuntime font has no font data in the Editor, so its asset is the first
  installed family of the OS list (Arial) with every installed one as its fallback table (Arial,
  Arial Unicode MS, Helvetica Neue, Microsoft Sans Serif, Apple Color Emoji at its 96-pixel strike).
  LastResort is not among them: the macOS face's family is `.LastResort`. Khmer, Ethiopic,
  Mongolian and CJK extension B are in none of them.
- `enginefont/<family>`: the face TextCore's style-less system font lookup gives a family. A
  family with a Regular face gives that face (Arial is Arial.ttf, Helvetica Neue face 0). Families
  without one (Avenir, Futura, Hiragino Sans, Al Bayan) give a face in the Editor's own listing
  order, which no documented rule fixes; they are listed in `residuals.json`. Installed faces are
  named as FreeType names them (`Al Nile`, not its Arabic Windows name).

Unbound lists the faces of `/System/Library/Fonts`, `/Library/Fonts` and `~/Library/Fonts` on
macOS, collections included, whatever fontconfig is installed. The Editor's list also holds the
Editor's own interface fonts (Inter, Roboto Mono), which Unbound does not list. Unmeasured:
`Font.GetOSInstalledFontNames` (the Editor lists full face names such as `Arial Bold` and hidden
`.`-names, and leaves out LastResort and Apple Color Emoji), other macOS versions, bold and italic
requests, alpha pixels (batch mode has no graphics device), and TextCore's OS list on Linux.

```sh
python3 tests/projects/osfontfallback/run.py record --label mac-a
python3 tests/projects/osfontfallback/run.py record --label mac-b
python3 tests/projects/osfontfallback/run.py publish mac-a mac-b
python3 tests/projects/osfontfallback/run.py gate
python3 tests/projects/osfontfallback/run.py check-reference
```

`FONT-LICENSE.txt` covers the supplied font.
