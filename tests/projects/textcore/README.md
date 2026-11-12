# TextCore probe

TextCore's text engine as UI Toolkit drives it, recorded by the same script in the Editor and in
Unbound (`Assets/Scripts/TextCoreProbe.cs`, calling the internal generator by reflection): the font
asset UI Toolkit makes of a Font (`TextSettings.GetCachedFontAsset`: dynamic, 90 points, padding 9,
SDFAA, 1024 x 1024), its face info, glyphs, atlas rects and kerning pairs, a few glyphs' atlas
pixels, a new font asset's first measure (before the kerning of its new glyphs is read), and 372
cases of `TextGenerator.GetPreferredValues` and `GenerateText` (characters, lines and mesh vertices):
words and sentences at 11 to 22 points in normal, bold and italic, wrapped paragraphs, whitespace
modes, every vertical and horizontal alignment kind, overflow modes, letter, word and paragraph
spacing, rich text tags, tabs, line breaks, escapes and accented text. Then the font engine outside
UI Toolkit's setting, for Liberation Sans: a font asset per `GlyphRenderMode` (smooth, raster,
SDF, SDF8 to SDF32, SDFAA, color; hinted and not; 24 points, padding 4) with its glyphs' metrics,
atlas rects and the atlas pixels around them; the face info at 13, 24, 24.5 and 37 points; and
glyph loads with each `GlyphLoadFlags` (default, no hinting, no scale, compute metrics, render,
no bitmap). Last, a face without 'H', 'x', 'X' or a space (`Assets/SubsetFonts/TextCoreNoCaps.ttf`,
Liberation Sans' "Aaog" renamed; `FONT-LICENSE.txt`): its face info from the font engine and from
a font asset made from it.

Fonts: Liberation Sans (from the OS) and Inter (from the Editor's resources), copied into
`Assets/Fonts` by the setup (they are not committed), and UI Toolkit's default runtime font, the
Editor resource `UIPackageResources/Fonts/NotInter/NotInter-Regular.ttf` (a Font referenced as
`{fileID: 4290535168887279389, guid: 0000000000000000d000000000000000}`). The reference keeps
each font's SHA-256.

Make the project and record Unity's reference:

    export DISPLAY=:0 XAUTHORITY=$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)
    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/textcore \
        -executeMethod ProbeSetup.Create -quit -logFile /tmp/textcore-setup.log
    TEXTCORE_PROBE_OUT=/tmp/textcore-unity.jsonl ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode \
        -projectPath tests/projects/textcore -executeMethod ProbeRunner.Record -logFile /tmp/textcore.log
    gzip -9 -c /tmp/textcore-unity.jsonl > tests/projects/textcore/Reference/unity-6000.0.71f1.jsonl.gz

The managed tests (`managed/UnityEngine.Tests/TextCoreTextTests.cs`) rebuild the font assets from the
reference's glyphs, characters and pairs and require every case's preferred size, characters,
lines and vertices to be the same floats. Run Unbound (font engine included) and compare, with the
two fonts copied in: `fonts.py` takes them from the OS and the Editor's resources (or from
`TEXTCORE_LIBERATION_SANS` and `TEXTCORE_INTER`) and refuses a file whose SHA-256 is not the
reference's. Without them the engine skips both fonts and no case is compared.

    python3 tests/projects/textcore/fonts.py tests/projects/textcore tests/projects/textcore/Reference/unity-6000.0.71f1.jsonl.gz
    TEXTCORE_PROBE_OUT=/tmp/textcore-unbound.jsonl unbound run -p tests/projects/textcore --headless 5 --no-watch
    python3 tests/projects/textcore/compare.py tests/projects/textcore/Reference/unity-6000.0.71f1.jsonl.gz /tmp/textcore-unbound.jsonl

`tools/agents/suite.py --only textcore` does the same on a copy of the project. With the fonts,
all 372 cases and the font engine controls match (616859 values; the atlas pixels of the controls
exactly). On macOS set
`UNBOUND_PROBE_UNITY` to the Editor's executable (or `TEXTCORE_INTER`) and
`TEXTCORE_LIBERATION_SANS`: neither default path exists there.

Not compared: Unity's kerning pair records carry uninitialized lookup flags, which differ from run
to run. Rows added to the reference are appended to the committed ones, which stay as recorded.
NotInter is not loaded without the Editor resources. Measured on the NVIDIA driver; the font
engine itself runs on the CPU.
