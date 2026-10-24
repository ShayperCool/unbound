using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

/// <summary>
/// Records what TextCore's text engine does with the probe's fonts, the way UI Toolkit drives it:
/// the font asset UI Toolkit makes of a Font (TextSettings.GetCachedFontAsset), its face info, the
/// glyphs its atlas gets and their kerning, preferred sizes (TextGenerator.GetPreferredValues) and
/// generated text (TextGenerator.GenerateText: characters, lines and the mesh vertices) for words,
/// sentences, wrapped paragraphs, alignments, overflow modes, spacing and rich text, at UI font
/// sizes in normal, bold and italic. The generator is internal: it is called by reflection.
/// Writes JSON lines to <c>TEXTCORE_PROBE_OUT</c>, then quits. Floats print exactly (G9).
/// </summary>
public class TextCoreProbe : MonoBehaviour
{
    public Font[] fonts;
    public string[] fontHashes;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    readonly StringBuilder m_Out = new StringBuilder();
    Assembly m_Text;
    TextSettings m_Settings;
    object m_Generator;
    object m_TextInfo;
    int m_CaseId;

    static readonly string[] s_SweepTexts =
    {
        "UI Toolkit probe",
        "Layout, styles, text & events",
        "AVATAR Wave To Yoyo",
        "The quick brown fox jumps over the lazy dog.",
        "0123456789 ,.;:!?()",
    };

    const string Paragraph = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

    void Start()
    {
        var output = Environment.GetEnvironmentVariable("TEXTCORE_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.temporaryCachePath, "textcore-probe.jsonl");
        try
        {
            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            try
            {
                Run();
            }
            catch (Exception e)
            {
                Line("error", "\"message\":" + Str(e.ToString()));
                Debug.LogException(e);
            }
            File.WriteAllText(output, m_Out.ToString());
            Debug.Log("textcore probe written to " + output);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Run()
    {
        m_Text = typeof(FontAsset).Assembly;
        m_Settings = ScriptableObject.CreateInstance<TextSettings>();
        m_Generator = Activator.CreateInstance(TextType("TextGenerator"), true);
        m_TextInfo = Activator.CreateInstance(TextType("TextInfo"), true);

        ColdKerning();

        for (int f = 0; f < fonts.Length; f++)
        {
            var font = fonts[f];
            if (font == null)
            {
                Line("font", "\"font\":" + f + ",\"missing\":true");
                continue;
            }
            var asset = (FontAsset)Call(m_Settings, "GetCachedFontAsset", font);
            RecordFont(f, font, asset);
            if (asset == null) continue;

            int[] sizes = { 11, 14, 16, 22 };
            string[] styles = { "Normal", "Bold", "Italic" };
            foreach (var text in s_SweepTexts)
                foreach (var size in sizes)
                    foreach (var style in styles)
                        Case(f, asset, new Spec { text = text, size = size, style = style }, vertices: false);

            foreach (var spec in DetailedCases())
                Case(f, asset, spec, vertices: f != 1);

            RecordGlyphs(f, asset);
        }
    }

    // ---- Cases ----

    class Spec
    {
        public string text;
        public float size = 16;
        public string style = "Normal";
        public float width = float.NaN;
        public float height = float.NaN;
        public string wrap = "Normal";
        public string align = "TopLeft";
        public string overflow = "Overflow";
        public bool richText = true;
        public float characterSpacing, wordSpacing, paragraphSpacing;
        public float extraPadding = 6;
        public Color color = Color.white;
    }

    static IEnumerable<Spec> DetailedCases()
    {
        foreach (var size in new float[] { 12, 18 })
        {
            foreach (var width in new float[] { 80, 150, 300 })
                yield return new Spec { text = Paragraph, size = size, width = width, height = 400 };
            yield return new Spec { text = Paragraph, size = size, width = 150, height = 400, wrap = "NoWrap" };
            yield return new Spec { text = "  two  spaces   and trailing  ", size = size, width = 150, height = 100, wrap = "PreserveWhitespace" };
            yield return new Spec { text = "  two  spaces   and trailing  ", size = size, width = 150, height = 100, wrap = "PreserveWhitespaceNoWrap" };
            yield return new Spec { text = "  two  spaces   and trailing  ", size = size, width = 150, height = 100 };
        }
        foreach (var align in new[] { "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight",
                     "TopJustified", "TopFlush", "TopGeoAligned", "BaselineLeft", "MidlineCenter", "CaplineRight", "MiddleJustified" })
            yield return new Spec { text = "Aligned text that wraps over a few lines here.", size = 14, width = 160, height = 120, align = align };
        foreach (var style in new[] { "Bold", "Italic", "BoldItalic" })
            yield return new Spec { text = "Synthesized " + style + " style, AVAWAY.", size = 16, width = 400, height = 50, style = style };
        foreach (var overflow in new[] { "Ellipsis", "Masking", "Truncate", "Overflow" })
        {
            yield return new Spec { text = "Overflowing label text that does not fit", size = 14, width = 120, height = 30, wrap = "NoWrap", overflow = overflow };
            yield return new Spec { text = Paragraph, size = 14, width = 150, height = 40, overflow = overflow };
        }
        yield return new Spec { text = "Spacing between letters", size = 14, characterSpacing = 5 };
        yield return new Spec { text = "Spacing between the words", size = 14, wordSpacing = 12 };
        yield return new Spec { text = "First paragraph\nSecond paragraph\nThird", size = 14, paragraphSpacing = 9 };
        yield return new Spec { text = "Negative letter spacing", size = 14, characterSpacing = -3 };
        yield return new Spec { text = "Plain <b>bold</b> <i>italic</i> <color=#FF0000>red</color> <size=150%>big</size> <size=8>small</size>", size = 14, width = 500, height = 100 };
        yield return new Spec { text = "line<br>break <noparse><b>raw</b></noparse> after", size = 14, width = 500, height = 100 };
        yield return new Spec { text = "Rich <b>bold</b> off", size = 14, richText = false };
        yield return new Spec { text = "<u>under</u> <s>strike</s> <alpha=#80>half</alpha> <cspace=3>spaced</cspace> end", size = 14, width = 500, height = 100 };
        yield return new Spec { text = "<align=center>centered</align>\n<line-height=150%>tall\nlines</line-height>\nnormal", size = 14, width = 300, height = 200 };
        yield return new Spec { text = "<uppercase>up</uppercase> <lowercase>DOWN</lowercase> <smallcaps>Small Caps</smallcaps>", size = 14, width = 500, height = 100 };
        yield return new Spec { text = "x<sup>2</sup> H<sub>2</sub>O <nobr>no break here</nobr> <color=green>named</color>", size = 14, width = 120, height = 100 };
        yield return new Spec { text = "Tab\tseparated\tvalues", size = 14 };
        yield return new Spec { text = "Line one\nLine two\n\nLine four", size = 14 };
        yield return new Spec { text = "Escaped \\n newline and \\t tab", size = 14 };
        yield return new Spec { text = "Ünïcödé àccénts ñ ß €", size = 16 };
        yield return new Spec { text = "W", size = 22 };
        yield return new Spec { text = " ", size = 16 };
        yield return new Spec { text = "", size = 16 };
        yield return new Spec { text = "Colored", size = 16, color = new Color(0.2f, 0.4f, 0.8f, 0.5f) };
        yield return new Spec { text = "Padding 0", size = 16, extraPadding = 0 };
        yield return new Spec { text = "Size 13.5", size = 13.5f };
        yield return new Spec { text = "UI Toolkit probe", size = 22, style = "Bold", width = 300, height = 40, align = "MiddleCenter" };
        yield return new Spec { text = "Layout, styles, text & events", size = 11, width = 300, height = 40 };
    }

    object Settings(FontAsset asset, Spec spec)
    {
        var s = Activator.CreateInstance(TextType("TextGenerationSettings"), true);
        SetProp(s, "text", spec.text);
        Set(s, "screenRect", new Rect(0, 0, spec.width, spec.height));
        Set(s, "fontAsset", asset);
        Set(s, "textSettings", m_Settings);
        Set(s, "fontSize", spec.size);
        Set(s, "fontStyle", Style(spec.style));
        Set(s, "textAlignment", Enum.Parse(TextType("TextAlignment"), spec.align));
        Set(s, "textWrappingMode", Enum.Parse(TextType("TextWrappingMode"), spec.wrap));
        Set(s, "overflowMode", Enum.Parse(TextType("TextOverflowMode"), spec.overflow));
        Set(s, "richText", spec.richText);
        Set(s, "characterSpacing", spec.characterSpacing);
        Set(s, "wordSpacing", spec.wordSpacing);
        Set(s, "paragraphSpacing", spec.paragraphSpacing);
        Set(s, "extraPadding", spec.extraPadding);
        Set(s, "color", spec.color);
        Set(s, "shouldConvertToLinearSpace", false);
        Set(s, "isIMGUI", false);
        return s;
    }

    static FontStyles Style(string style) => style switch
    {
        "Bold" => FontStyles.Bold,
        "Italic" => FontStyles.Italic,
        "BoldItalic" => FontStyles.Bold | FontStyles.Italic,
        _ => FontStyles.Normal,
    };

    void Case(int font, FontAsset asset, Spec spec, bool vertices)
    {
        var settings = Settings(asset, spec);
        // Warm: the glyphs are in the atlas and their kerning is read, as after UI Toolkit's first
        // frame (font features are read in FontAsset.UpdateFontAssetsInUpdateQueue).
        Call(m_Generator, "GetPreferredValues", settings, m_TextInfo);
        CallStatic(typeof(FontAsset), "UpdateFontAssetsInUpdateQueue");
        var preferred = (Vector2)Call(m_Generator, "GetPreferredValues", settings, m_TextInfo);
        Call(m_Generator, "GenerateText", settings, m_TextInfo);
        var sb = new StringBuilder();
        sb.Append("\"id\":").Append(m_CaseId++).Append(",\"font\":").Append(font)
            .Append(",\"text\":").Append(Str(spec.text)).Append(",\"size\":").Append(F(spec.size))
            .Append(",\"style\":").Append(Str(spec.style)).Append(",\"width\":").Append(F(spec.width)).Append(",\"height\":").Append(F(spec.height))
            .Append(",\"wrap\":").Append(Str(spec.wrap)).Append(",\"align\":").Append(Str(spec.align)).Append(",\"overflow\":").Append(Str(spec.overflow))
            .Append(",\"richText\":").Append(spec.richText ? "true" : "false")
            .Append(",\"characterSpacing\":").Append(F(spec.characterSpacing)).Append(",\"wordSpacing\":").Append(F(spec.wordSpacing))
            .Append(",\"paragraphSpacing\":").Append(F(spec.paragraphSpacing)).Append(",\"extraPadding\":").Append(F(spec.extraPadding))
            .Append(",\"color\":[").Append(F(spec.color.r)).Append(',').Append(F(spec.color.g)).Append(',').Append(F(spec.color.b)).Append(',').Append(F(spec.color.a)).Append(']')
            .Append(",\"preferred\":[").Append(F(preferred.x)).Append(',').Append(F(preferred.y)).Append(']');
        AppendTextInfo(sb, asset, vertices);
        Line("case", sb.ToString());
    }

    void AppendTextInfo(StringBuilder sb, FontAsset asset, bool vertices)
    {
        var info = m_TextInfo;
        int characterCount = (int)Get(info, "characterCount");
        int lineCount = (int)Get(info, "lineCount");
        int materialCount = (int)Get(info, "materialCount");
        sb.Append(",\"truncated\":").Append((bool)GetProp(m_Generator, "isTextTruncated") ? "true" : "false");
        sb.Append(",\"characterCount\":").Append(characterCount).Append(",\"lineCount\":").Append(lineCount).Append(",\"materialCount\":").Append(materialCount);
        // [character, index, stringLength, lineNumber, visible, origin, xAdvance, ascender, descender, baseLine,
        //  bottomLeft.x, bottomLeft.y, topRight.x, topRight.y, topLeft.x, bottomRight.x, scale, pointSize, style, materialReferenceIndex]
        sb.Append(",\"chars\":[");
        var elements = (Array)Get(info, "textElementInfo");
        for (int i = 0; i < characterCount; i++)
        {
            var e = elements.GetValue(i);
            if (i > 0) sb.Append(',');
            var bl = (Vector3)Get(e, "bottomLeft");
            var tr = (Vector3)Get(e, "topRight");
            var tl = (Vector3)Get(e, "topLeft");
            var br = (Vector3)Get(e, "bottomRight");
            sb.Append('[').Append((uint)Get(e, "character")).Append(',').Append((int)Get(e, "index")).Append(',').Append((int)Get(e, "stringLength"))
                .Append(',').Append((int)Get(e, "lineNumber")).Append(',').Append((bool)Get(e, "isVisible") ? 1 : 0)
                .Append(',').Append(F((float)Get(e, "origin"))).Append(',').Append(F((float)Get(e, "xAdvance")))
                .Append(',').Append(F((float)Get(e, "ascender"))).Append(',').Append(F((float)Get(e, "descender"))).Append(',').Append(F((float)Get(e, "baseLine")))
                .Append(',').Append(F(bl.x)).Append(',').Append(F(bl.y)).Append(',').Append(F(tr.x)).Append(',').Append(F(tr.y))
                .Append(',').Append(F(tl.x)).Append(',').Append(F(br.x))
                .Append(',').Append(F((float)Get(e, "scale"))).Append(',').Append(F((float)Get(e, "pointSize")))
                .Append(',').Append((int)(FontStyles)Get(e, "style")).Append(',').Append((int)Get(e, "materialReferenceIndex")).Append(']');
        }
        // [characterCount, visibleCharacterCount, spaceCount, wordCount, firstCharacterIndex, firstVisibleCharacterIndex,
        //  lastCharacterIndex, lastVisibleCharacterIndex, length, lineHeight, ascender, baseline, descender, maxAdvance,
        //  width, alignment, extents min x, min y, max x, max y]
        sb.Append("],\"lines\":[");
        var lines = (Array)Get(info, "lineInfo");
        for (int i = 0; i < lineCount; i++)
        {
            var l = lines.GetValue(i);
            if (i > 0) sb.Append(',');
            var extents = Get(l, "lineExtents");
            var min = (Vector2)Get(extents, "min");
            var max = (Vector2)Get(extents, "max");
            sb.Append('[').Append((int)Get(l, "characterCount")).Append(',').Append((int)Get(l, "visibleCharacterCount")).Append(',').Append((int)Get(l, "spaceCount"))
                .Append(',').Append((int)Get(l, "wordCount")).Append(',').Append((int)Get(l, "firstCharacterIndex")).Append(',').Append((int)Get(l, "firstVisibleCharacterIndex"))
                .Append(',').Append((int)Get(l, "lastCharacterIndex")).Append(',').Append((int)Get(l, "lastVisibleCharacterIndex"))
                .Append(',').Append(F((float)Get(l, "length"))).Append(',').Append(F((float)Get(l, "lineHeight"))).Append(',').Append(F((float)Get(l, "ascender")))
                .Append(',').Append(F((float)Get(l, "baseline"))).Append(',').Append(F((float)Get(l, "descender"))).Append(',').Append(F((float)Get(l, "maxAdvance")))
                .Append(',').Append(F((float)Get(l, "width"))).Append(',').Append(Convert.ToInt32(Get(l, "alignment")))
                .Append(',').Append(F(min.x)).Append(',').Append(F(min.y)).Append(',').Append(F(max.x)).Append(',').Append(F(max.y)).Append(']');
        }
        sb.Append(']');
        if (!vertices) return;
        // Per mesh: the atlas texture it samples and its vertices
        // [x, y, z, r, g, b, a, uv0.x, uv0.y, uv2.x, uv2.y].
        sb.Append(",\"meshes\":[");
        var meshes = (Array)Get(info, "meshInfo");
        for (int m = 0; m < materialCount && m < meshes.Length; m++)
        {
            var mesh = meshes.GetValue(m);
            if (m > 0) sb.Append(',');
            int count = (int)Get(mesh, "vertexCount");
            var material = (Material)Get(mesh, "material");
            int atlas = -1;
            if (material != null)
                for (int a = 0; a < asset.atlasTextures.Length; a++)
                    if (asset.atlasTextures[a] != null && material.mainTexture == asset.atlasTextures[a]) atlas = a;
            sb.Append("{\"atlas\":").Append(atlas).Append(",\"renderMode\":").Append((int)(GlyphRenderMode)Get(mesh, "glyphRenderMode"))
                .Append(",\"vertexCount\":").Append(count).Append(",\"vertices\":[");
            var data = (Array)Get(mesh, "vertexData");
            for (int v = 0; v < count; v++)
            {
                var vertex = data.GetValue(v);
                var p = (Vector3)Get(vertex, "position");
                var c = (Color32)Get(vertex, "color");
                var uv0 = (Vector2)Get(vertex, "uv0");
                var uv2 = (Vector2)Get(vertex, "uv2");
                if (v > 0) sb.Append(',');
                sb.Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(',')
                    .Append(c.r).Append(',').Append(c.g).Append(',').Append(c.b).Append(',').Append(c.a).Append(',')
                    .Append(F(uv0.x)).Append(',').Append(F(uv0.y)).Append(',').Append(F(uv2.x)).Append(',').Append(F(uv2.y));
            }
            sb.Append("]}");
        }
        sb.Append(']');
    }

    /// <summary>A new font asset's first measure, before the font features of its new glyphs are
    /// read, then generation (which reads them first), then the measure again.</summary>
    void ColdKerning()
    {
        if (fonts.Length == 0 || fonts[0] == null) return;
        var asset = FontAsset.CreateFontAsset(fonts[0], 90, 9, GlyphRenderMode.DEFAULT, 1024, 1024);
        var spec = new Spec { text = "AVATAR Wave To Yoyo", size = 22 };
        var settings = Settings(asset, spec);
        var first = (Vector2)Call(m_Generator, "GetPreferredValues", settings, m_TextInfo);
        int pairsBefore = Pairs(asset).Count;
        Call(m_Generator, "GenerateText", settings, m_TextInfo);
        int pairsAfter = Pairs(asset).Count;
        var sb = new StringBuilder();
        AppendTextInfo(sb, asset, vertices: false);
        var second = (Vector2)Call(m_Generator, "GetPreferredValues", settings, m_TextInfo);
        Line("cold", "\"font\":0,\"text\":" + Str(spec.text) + ",\"size\":22,\"first\":[" + F(first.x) + "," + F(first.y) + "],\"second\":[" + F(second.x) + "," + F(second.y) +
            "],\"pairsBefore\":" + pairsBefore + ",\"pairsAfter\":" + pairsAfter + ",\"glyphs\":" + asset.glyphTable.Count + sb);
    }

    // ---- Font assets ----

    void RecordFont(int index, Font font, FontAsset asset)
    {
        var sb = new StringBuilder();
        sb.Append("\"font\":").Append(index).Append(",\"name\":").Append(Str(font.name))
            .Append(",\"sha256\":").Append(Str(fontHashes != null && index < fontHashes.Length ? fontHashes[index] : ""));
        if (asset == null)
        {
            Line("font", sb.Append(",\"asset\":null").ToString());
            return;
        }
        var face = asset.faceInfo;
        sb.Append(",\"assetName\":").Append(Str(asset.name)).Append(",\"version\":").Append(Str(asset.version))
            .Append(",\"faceInfo\":{\"familyName\":").Append(Str(face.familyName)).Append(",\"styleName\":").Append(Str(face.styleName))
            .Append(",\"faceIndex\":").Append((int)Get(face, "faceIndex")).Append(",\"pointSize\":").Append(F(face.pointSize)).Append(",\"scale\":").Append(F(face.scale))
            .Append(",\"unitsPerEM\":").Append((int)Get(face, "unitsPerEM")).Append(",\"lineHeight\":").Append(F(face.lineHeight))
            .Append(",\"ascentLine\":").Append(F(face.ascentLine)).Append(",\"capLine\":").Append(F(face.capLine)).Append(",\"meanLine\":").Append(F(face.meanLine))
            .Append(",\"baseline\":").Append(F(face.baseline)).Append(",\"descentLine\":").Append(F(face.descentLine))
            .Append(",\"superscriptOffset\":").Append(F(face.superscriptOffset)).Append(",\"superscriptSize\":").Append(F(face.superscriptSize))
            .Append(",\"subscriptOffset\":").Append(F(face.subscriptOffset)).Append(",\"subscriptSize\":").Append(F(face.subscriptSize))
            .Append(",\"underlineOffset\":").Append(F(face.underlineOffset)).Append(",\"underlineThickness\":").Append(F(face.underlineThickness))
            .Append(",\"strikethroughOffset\":").Append(F(face.strikethroughOffset)).Append(",\"strikethroughThickness\":").Append(F(face.strikethroughThickness))
            .Append(",\"tabWidth\":").Append(F(face.tabWidth)).Append('}')
            .Append(",\"atlasWidth\":").Append(asset.atlasWidth).Append(",\"atlasHeight\":").Append(asset.atlasHeight).Append(",\"atlasPadding\":").Append(asset.atlasPadding)
            .Append(",\"renderMode\":").Append((int)asset.atlasRenderMode).Append(",\"populationMode\":").Append((int)asset.atlasPopulationMode)
            .Append(",\"multiAtlas\":").Append(asset.isMultiAtlasTexturesEnabled ? "true" : "false")
            .Append(",\"atlasFormat\":").Append(Str(asset.atlasTexture != null ? asset.atlasTexture.format.ToString() : ""))
            .Append(",\"atlasSize\":[").Append(asset.atlasTexture != null ? asset.atlasTexture.width : 0).Append(',').Append(asset.atlasTexture != null ? asset.atlasTexture.height : 0).Append(']')
            .Append(",\"shader\":").Append(Str(asset.material != null && asset.material.shader != null ? asset.material.shader.name : ""))
            .Append(",\"material\":").Append(Str(asset.material != null ? asset.material.name : ""))
            .Append(",\"gradientScale\":").Append(F(asset.material != null && asset.material.HasProperty("_GradientScale") ? asset.material.GetFloat("_GradientScale") : -1))
            .Append(",\"styles\":[").Append(F(asset.regularStyleWeight)).Append(',').Append(F(asset.regularStyleSpacing)).Append(',').Append(F(asset.boldStyleWeight))
            .Append(',').Append(F(asset.boldStyleSpacing)).Append(',').Append(asset.italicStyleSlant).Append(',').Append(asset.tabMultiple).Append(']')
            .Append(",\"characters\":").Append(asset.characterTable.Count).Append(",\"glyphs\":").Append(asset.glyphTable.Count);
        sb.Append(",\"lookup\":[");
        bool first = true;
        foreach (var pair in asset.characterLookupTable)
        {
            if (!first) sb.Append(',');
            first = false;
            var g = pair.Value.glyph;
            sb.Append('[').Append(pair.Key).Append(',').Append(pair.Value.glyphIndex).Append(',').Append(F(g.metrics.horizontalAdvance)).Append(',').Append(F(g.metrics.width)).Append(']');
        }
        sb.Append(']');
        Line("font", sb.ToString());
    }

    void RecordGlyphs(int index, FontAsset asset)
    {
        // [index, width, height, bearingX, bearingY, advance, rect x, y, width, height, atlasIndex, scale]
        var sb = new StringBuilder();
        sb.Append("\"font\":").Append(index).Append(",\"atlasCount\":").Append(asset.atlasTextureCount).Append(",\"glyphs\":[");
        for (int i = 0; i < asset.glyphTable.Count; i++)
        {
            var g = asset.glyphTable[i];
            var m = g.metrics;
            var r = g.glyphRect;
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(g.index).Append(',').Append(F(m.width)).Append(',').Append(F(m.height)).Append(',').Append(F(m.horizontalBearingX))
                .Append(',').Append(F(m.horizontalBearingY)).Append(',').Append(F(m.horizontalAdvance))
                .Append(',').Append(r.x).Append(',').Append(r.y).Append(',').Append(r.width).Append(',').Append(r.height)
                .Append(',').Append(g.atlasIndex).Append(',').Append(F(g.scale)).Append(']');
        }
        sb.Append("],\"characters\":[");
        for (int i = 0; i < asset.characterTable.Count; i++)
        {
            var c = asset.characterTable[i];
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(c.unicode).Append(',').Append(c.glyphIndex).Append(']');
        }
        // The pairs between glyphs of the atlas (the font's other pairs are read as well, when
        // their first glyph is added: only counted):
        // [first glyph, second glyph, first xPlacement, yPlacement, xAdvance, yAdvance, second ..., flags]
        var pairs = Pairs(asset);
        sb.Append("],\"pairCount\":").Append(pairs.Count).Append(",\"pairs\":[");
        bool firstPair = true;
        for (int i = 0; i < pairs.Count; i++)
        {
            var p = pairs[i];
            if (!asset.glyphLookupTable.ContainsKey(p.firstAdjustmentRecord.glyphIndex) || !asset.glyphLookupTable.ContainsKey(p.secondAdjustmentRecord.glyphIndex)) continue;
            var a = p.firstAdjustmentRecord.glyphValueRecord;
            var b = p.secondAdjustmentRecord.glyphValueRecord;
            if (!firstPair) sb.Append(',');
            firstPair = false;
            sb.Append('[').Append(p.firstAdjustmentRecord.glyphIndex).Append(',').Append(p.secondAdjustmentRecord.glyphIndex)
                .Append(',').Append(F(a.xPlacement)).Append(',').Append(F(a.yPlacement)).Append(',').Append(F(a.xAdvance)).Append(',').Append(F(a.yAdvance))
                .Append(',').Append(F(b.xPlacement)).Append(',').Append(F(b.yPlacement)).Append(',').Append(F(b.xAdvance)).Append(',').Append(F(b.yAdvance))
                .Append(',').Append((int)p.featureLookupFlags).Append(']');
        }
        sb.Append("],\"freeRects\":").Append(Rects(Get(asset, "freeGlyphRects") as List<GlyphRect>))
            .Append(",\"usedRects\":").Append(Rects(Get(asset, "usedGlyphRects") as List<GlyphRect>));
        // The character lookup (keys of styled lookups carry weight and italic bits; the
        // synthesized control characters have glyphs outside the table):
        // [key, unicode, glyph, width, height, bearingX, bearingY, advance, rect x, y, width, height, atlasIndex, glyph scale, scale]
        sb.Append(",\"lookup\":[");
        bool firstEntry = true;
        foreach (var pair in asset.characterLookupTable)
        {
            var c = pair.Value;
            var g = c.glyph;
            if (!firstEntry) sb.Append(',');
            firstEntry = false;
            sb.Append('[').Append(pair.Key).Append(',').Append(c.unicode).Append(',').Append(c.glyphIndex);
            if (g == null) sb.Append(",null");
            else
                sb.Append(',').Append(F(g.metrics.width)).Append(',').Append(F(g.metrics.height)).Append(',').Append(F(g.metrics.horizontalBearingX))
                    .Append(',').Append(F(g.metrics.horizontalBearingY)).Append(',').Append(F(g.metrics.horizontalAdvance))
                    .Append(',').Append(g.glyphRect.x).Append(',').Append(g.glyphRect.y).Append(',').Append(g.glyphRect.width).Append(',').Append(g.glyphRect.height)
                    .Append(',').Append(g.atlasIndex).Append(',').Append(F(g.scale)).Append(',').Append(F(c.scale));
            sb.Append(']');
        }
        sb.Append(']');
        Line("glyphs", sb.ToString());

        // A few glyphs' atlas pixels (alpha, rows bottom first) with the padding around them.
        foreach (var c in "AgW&")
        {
            if (!asset.characterLookupTable.TryGetValue(c, out var character)) continue;
            var g = character.glyph;
            var texture = asset.atlasTextures[g.atlasIndex];
            int p = asset.atlasPadding;
            int x = Math.Max(0, g.glyphRect.x - p), y = Math.Max(0, g.glyphRect.y - p);
            int w = Math.Min(texture.width - x, g.glyphRect.width + 2 * p), h = Math.Min(texture.height - y, g.glyphRect.height + 2 * p);
            try
            {
                var pixels = texture.GetPixels32();
                var alpha = new byte[w * h];
                for (int row = 0; row < h; row++)
                    for (int col = 0; col < w; col++)
                        alpha[row * w + col] = pixels[(y + row) * texture.width + x + col].a;
                Line("atlas", "\"font\":" + index + ",\"char\":" + (int)c + ",\"glyph\":" + g.index + ",\"x\":" + x + ",\"y\":" + y + ",\"w\":" + w + ",\"h\":" + h +
                    ",\"alpha\":" + Str(Convert.ToBase64String(alpha)));
            }
            catch (Exception e)
            {
                Line("atlas", "\"font\":" + index + ",\"char\":" + (int)c + ",\"error\":" + Str(e.Message));
            }
        }
    }

    static List<GlyphPairAdjustmentRecord> Pairs(FontAsset asset) => (List<GlyphPairAdjustmentRecord>)Get(asset.fontFeatureTable, "glyphPairAdjustmentRecords");

    static string Rects(List<GlyphRect> rects)
    {
        if (rects == null) return "null";
        var sb = new StringBuilder("[");
        for (int i = 0; i < rects.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(rects[i].x).Append(',').Append(rects[i].y).Append(',').Append(rects[i].width).Append(',').Append(rects[i].height).Append(']');
        }
        return sb.Append(']').ToString();
    }

    // ---- Reflection ----

    Type TextType(string name) => m_Text.GetType("UnityEngine.TextCore.Text." + name, true);

    static object Get(object target, string name)
    {
        var type = target.GetType();
        for (var t = type; t != null; t = t.BaseType)
        {
            var field = t.GetField(name, Any);
            if (field != null) return field.GetValue(target);
            var property = t.GetProperty(name, Any);
            if (property != null) return property.GetValue(target);
        }
        throw new MissingMemberException(type.FullName, name);
    }

    static object GetProp(object target, string name) => Get(target, name);

    static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Any);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }

    static void SetProp(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, Any);
        if (property == null) throw new MissingMemberException(target.GetType().FullName, name);
        property.SetValue(target, value);
    }

    static object Call(object target, string name, params object[] args)
    {
        foreach (var method in target.GetType().GetMethods(Any))
            if (method.Name == name && method.GetParameters().Length == args.Length)
                return method.Invoke(target, args);
        throw new MissingMethodException(target.GetType().FullName, name);
    }

    static object CallStatic(Type type, string name, params object[] args)
    {
        foreach (var method in type.GetMethods(Any))
            if (method.Name == name && method.IsStatic && method.GetParameters().Length == args.Length)
                return method.Invoke(null, args);
        throw new MissingMethodException(type.FullName, name);
    }

    // ---- Output ----

    void Line(string type, string body) => m_Out.Append("{\"type\":\"").Append(type).Append("\",").Append(body).Append("}\n");

    static string F(float v)
    {
        if (float.IsNaN(v)) return "\"NaN\"";
        if (float.IsInfinity(v)) return v > 0 ? "\"Infinity\"" : "\"-Infinity\"";
        return v.ToString("G9", Inv);
    }

    static string Str(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32 || c > 126) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
