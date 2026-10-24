# TextCore probe

TextCore's text engine as UI Toolkit drives it, recorded by the same script in the Editor and in
Unbound (`Assets/Scripts/TextCoreProbe.cs`, calling the internal generator by reflection): the font
asset UI Toolkit makes of a Font (`TextSettings.GetCachedFontAsset`: dynamic, 90 points, padding 9,
SDFAA, 1024 x 1024), its face info, glyphs, atlas rects and kerning pairs, a few glyphs' atlas
pixels, a new font asset's first measure (before the kerning of its new glyphs is read), and 372
cases of `TextGenerator.GetPreferredValues` and `GenerateText` (characters, lines and mesh vertices):
words and sentences at 11 to 22 points in normal, bold and italic, wrapped paragraphs, whitespace
modes, every vertical and horizontal alignment kind, overflow modes, letter, word and paragraph
spacing, rich text tags, tabs, line breaks, escapes and accented text.

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
lines and vertices to be the same floats. Run Unbound (font engine included) and compare (with the
fonts copied in, `ProbeSetup` or `cp /usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf
~/Unity/Hub/Editor/6000.0.71f1/Editor/Data/Resources/Fonts/Inter-Regular.ttf tests/projects/textcore/Assets/Fonts/`):

    TEXTCORE_PROBE_OUT=/tmp/textcore-unbound.jsonl unbound run -p tests/projects/textcore --headless 5 --no-watch
    python3 tests/projects/textcore/compare.py tests/projects/textcore/Reference/unity-6000.0.71f1.jsonl.gz /tmp/textcore-unbound.jsonl

Known differences: Unity's FaceInfo cap and mean lines are the tops of 'H' and 'x' hinted by the
font's TrueType instructions, which Unbound approximates (Inter's cap line is 66 in Unity, 65 in
Unbound, moving synthesized italics and cap-line alignment by a fraction of a pixel); SDF atlas pixels
are an exact distance field where Unity's SDFAA is an anti-aliased distance transform; Unity's kerning
pair records carry uninitialized lookup flags (not compared). NotInter is not loaded without the
Editor resources.
