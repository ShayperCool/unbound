using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using Xunit;
using Xunit.Abstractions;
using TextGenerationSettings = UnityEngine.TextCore.Text.TextGenerationSettings;
using TextGenerator = UnityEngine.TextCore.Text.TextGenerator;
using TextInfo = UnityEngine.TextCore.Text.TextInfo;
using TextAlignment = UnityEngine.TextCore.Text.TextAlignment;

namespace Unbound.Tests
{
    /// <summary>
    /// TextCore's text generator against the Unity Editor (tests/projects/textcore records it, see its
    /// README): font assets are rebuilt from the glyphs, characters and kerning pairs Unity's dynamic
    /// font assets got (so the font engine is not involved), then every recorded case is measured
    /// and generated the way the probe did, and preferred sizes, characters, lines and mesh
    /// vertices must be the same floats.
    /// </summary>
    public class TextCoreTextTests
    {
        readonly ITestOutputHelper m_Output;

        public TextCoreTextTests(ITestOutputHelper output) => m_Output = output;

        static string ReferencePath([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.Combine(Path.GetDirectoryName(source), "..", "..", "tests", "projects", "textcore", "Reference", "unity-6000.0.71f1.jsonl.gz");

        sealed class Reference
        {
            internal readonly Dictionary<int, JsonElement> Fonts = new Dictionary<int, JsonElement>();
            internal readonly Dictionary<int, JsonElement> Glyphs = new Dictionary<int, JsonElement>();
            internal readonly List<JsonElement> Cases = new List<JsonElement>();
        }

        static Reference s_Reference;

        static Reference Load()
        {
            if (s_Reference != null) return s_Reference;
            var reference = new Reference();
            using var file = File.OpenRead(ReferencePath());
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var e = JsonDocument.Parse(line).RootElement;
                switch (e.GetProperty("type").GetString())
                {
                    case "font": reference.Fonts[e.GetProperty("font").GetInt32()] = e; break;
                    case "glyphs": reference.Glyphs[e.GetProperty("font").GetInt32()] = e; break;
                    case "case": reference.Cases.Add(e); break;
                }
            }
            return s_Reference = reference;
        }

        static float F(JsonElement e) =>
            e.ValueKind == JsonValueKind.String ? float.Parse(e.GetString(), CultureInfo.InvariantCulture) : (float)e.GetDouble();

        static float F(JsonElement array, int i) => F(array[i]);

        // ---- Font assets from the reference ----

        static readonly Dictionary<int, FontAsset> s_Assets = new Dictionary<int, FontAsset>();
        static TextSettings s_Settings;

        static FontAsset Asset(Reference reference, int font)
        {
            if (s_Assets.TryGetValue(font, out var cached)) return cached;
            var info = reference.Fonts[font];
            var glyphs = reference.Glyphs[font];
            var face = info.GetProperty("faceInfo");
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            asset.name = info.GetProperty("name").GetString();
            asset.version = "1.1.0";
            asset.faceInfo = new FaceInfo
            {
                familyName = face.GetProperty("familyName").GetString(),
                styleName = face.GetProperty("styleName").GetString(),
                faceIndex = face.GetProperty("faceIndex").GetInt32(),
                pointSize = F(face.GetProperty("pointSize")),
                scale = F(face.GetProperty("scale")),
                unitsPerEM = face.GetProperty("unitsPerEM").GetInt32(),
                lineHeight = F(face.GetProperty("lineHeight")),
                ascentLine = F(face.GetProperty("ascentLine")),
                capLine = F(face.GetProperty("capLine")),
                meanLine = F(face.GetProperty("meanLine")),
                baseline = F(face.GetProperty("baseline")),
                descentLine = F(face.GetProperty("descentLine")),
                superscriptOffset = F(face.GetProperty("superscriptOffset")),
                superscriptSize = F(face.GetProperty("superscriptSize")),
                subscriptOffset = F(face.GetProperty("subscriptOffset")),
                subscriptSize = F(face.GetProperty("subscriptSize")),
                underlineOffset = F(face.GetProperty("underlineOffset")),
                underlineThickness = F(face.GetProperty("underlineThickness")),
                strikethroughOffset = F(face.GetProperty("strikethroughOffset")),
                strikethroughThickness = F(face.GetProperty("strikethroughThickness")),
                tabWidth = F(face.GetProperty("tabWidth")),
            };
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.atlasWidth = info.GetProperty("atlasWidth").GetInt32();
            asset.atlasHeight = info.GetProperty("atlasHeight").GetInt32();
            asset.atlasPadding = info.GetProperty("atlasPadding").GetInt32();
            asset.atlasRenderMode = (GlyphRenderMode)info.GetProperty("renderMode").GetInt32();
            var styles = info.GetProperty("styles");
            asset.regularStyleWeight = F(styles, 0);
            asset.regularStyleSpacing = F(styles, 1);
            asset.boldStyleWeight = F(styles, 2);
            asset.boldStyleSpacing = F(styles, 3);
            asset.italicStyleSlant = (byte)styles[4].GetInt32();
            asset.tabMultiple = (byte)styles[5].GetInt32();
            var atlas = new Texture2D(1, 1, TextureFormat.Alpha8, false);
            asset.atlasTextures = new[] { atlas };
            var material = new Material(TextShaderUtilities.ShaderRef_MobileSDF);
            material.SetTexture(TextShaderUtilities.ID_MainTex, atlas);
            material.SetFloat(TextShaderUtilities.ID_GradientScale, asset.atlasPadding + 1);
            asset.material = material;

            var table = new Dictionary<uint, Glyph>();
            foreach (var g in glyphs.GetProperty("glyphs").EnumerateArray())
            {
                var glyph = new Glyph((uint)g[0].GetInt32(), new GlyphMetrics(F(g, 1), F(g, 2), F(g, 3), F(g, 4), F(g, 5)),
                    new GlyphRect(g[6].GetInt32(), g[7].GetInt32(), g[8].GetInt32(), g[9].GetInt32()), F(g, 11), g[10].GetInt32());
                asset.glyphTable.Add(glyph);
                table[glyph.index] = glyph;
            }
            var characters = new Dictionary<uint, Character>();
            foreach (var c in glyphs.GetProperty("characters").EnumerateArray())
            {
                var character = new Character((uint)c[0].GetInt32(), asset, table[(uint)c[1].GetInt32()]);
                asset.characterTable.Add(character);
                characters[character.unicode] = character;
            }
            foreach (var p in glyphs.GetProperty("pairs").EnumerateArray())
            {
                var record = new GlyphPairAdjustmentRecord(
                    new GlyphAdjustmentRecord((uint)p[0].GetInt32(), new GlyphValueRecord(F(p, 2), F(p, 3), F(p, 4), F(p, 5))),
                    new GlyphAdjustmentRecord((uint)p[1].GetInt32(), new GlyphValueRecord(F(p, 6), F(p, 7), F(p, 8), F(p, 9))));
                record.featureLookupFlags = (FontFeatureLookupFlags)p[10].GetInt32();
                asset.fontFeatureTable.glyphPairAdjustmentRecords.Add(record);
            }
            asset.ReadFontAssetDefinition();
            // The lookup as Unity's ended up (synthesized control characters with the face's glyphs).
            var lookup = asset.characterLookupTable;
            lookup.Clear();
            foreach (var entry in glyphs.GetProperty("lookup").EnumerateArray())
            {
                uint key = (uint)entry[0].GetInt64();
                uint unicode = (uint)entry[1].GetInt64();
                uint glyphIndex = (uint)entry[2].GetInt64();
                if (!characters.TryGetValue(unicode, out var character) || character.glyphIndex != glyphIndex)
                {
                    var glyph = table.TryGetValue(glyphIndex, out var known) && glyphIndex != 0
                        ? known
                        : new Glyph(glyphIndex, new GlyphMetrics(F(entry, 3), F(entry, 4), F(entry, 5), F(entry, 6), F(entry, 7)),
                            new GlyphRect(entry[8].GetInt32(), entry[9].GetInt32(), entry[10].GetInt32(), entry[11].GetInt32()), F(entry, 13), entry[12].GetInt32());
                    character = new Character(unicode, asset, glyph);
                }
                lookup[key] = character;
            }
            s_Assets[font] = asset;
            return asset;
        }

        static TextGenerationSettings Settings(FontAsset asset, JsonElement c)
        {
            var settings = new TextGenerationSettings
            {
                screenRect = new Rect(0f, 0f, F(c.GetProperty("width")), F(c.GetProperty("height"))),
                fontAsset = asset,
                textSettings = s_Settings ??= ScriptableObject.CreateInstance<TextSettings>(),
                fontSize = F(c.GetProperty("size")),
                fontStyle = c.GetProperty("style").GetString() switch
                {
                    "Bold" => FontStyles.Bold,
                    "Italic" => FontStyles.Italic,
                    "BoldItalic" => FontStyles.Bold | FontStyles.Italic,
                    _ => FontStyles.Normal,
                },
                textAlignment = Enum.Parse<TextAlignment>(c.GetProperty("align").GetString()),
                textWrappingMode = Enum.Parse<TextWrappingMode>(c.GetProperty("wrap").GetString()),
                overflowMode = Enum.Parse<TextOverflowMode>(c.GetProperty("overflow").GetString()),
                richText = c.GetProperty("richText").GetBoolean(),
                characterSpacing = F(c.GetProperty("characterSpacing")),
                wordSpacing = F(c.GetProperty("wordSpacing")),
                paragraphSpacing = F(c.GetProperty("paragraphSpacing")),
                extraPadding = F(c.GetProperty("extraPadding")),
                shouldConvertToLinearSpace = false,
                isIMGUI = false,
            };
            var color = c.GetProperty("color");
            settings.color = new Color(F(color, 0), F(color, 1), F(color, 2), F(color, 3));
            settings.text = c.GetProperty("text").GetString();
            return settings;
        }

        // ---- Comparison ----

        sealed class Mismatches
        {
            readonly List<string> m_Lines = new List<string>();
            internal int Count;
            internal int Compared;
            internal float MaxError;

            internal void Float(string what, float expected, float actual)
            {
                Compared++;
                if (expected.Equals(actual)) return;
                float error = Math.Abs(expected - actual);
                if (!float.IsNaN(error) && error > MaxError) MaxError = error;
                Add($"{what}: unity {expected.ToString("R", CultureInfo.InvariantCulture)} unbound {actual.ToString("R", CultureInfo.InvariantCulture)}");
            }

            internal void Int(string what, long expected, long actual)
            {
                Compared++;
                if (expected != actual) Add($"{what}: unity {expected} unbound {actual}");
            }

            internal void Note(string line)
            {
                if (m_Lines.Count < 80) m_Lines.Add(line);
            }

            internal void Add(string line)
            {
                Count++;
                if (m_Lines.Count < 60) m_Lines.Add(line);
            }

            public override string ToString() => $"{Count} of {Compared} values differ (max {MaxError})\n" + string.Join("\n", m_Lines);
        }

        static string Name(JsonElement c) =>
            $"#{c.GetProperty("id").GetInt32()} font {c.GetProperty("font").GetInt32()} {c.GetProperty("size").GetRawText()}px {c.GetProperty("style").GetString()} " +
            $"{c.GetProperty("wrap").GetString()} {c.GetProperty("align").GetString()} {c.GetProperty("overflow").GetString()} w={c.GetProperty("width").GetRawText()} \"{Short(c.GetProperty("text").GetString())}\"";

        static string Short(string s) => s.Length > 24 ? s.Substring(0, 24) + "…" : s;

        /// <summary>Runs every case as the probe did (one generator and text info for all, a warm
        /// measure, the measure, then generation) and compares what <paramref name="compare"/> checks.</summary>
        void RunCases(Func<JsonElement, bool> filter, Action<JsonElement, Vector2, TextInfo, TextGenerator, Mismatches> compare)
        {
            var reference = Load();
            var generator = new TextGenerator();
            var textInfo = new TextInfo();
            var mismatches = new Mismatches();
            int cases = 0;
            foreach (var c in reference.Cases)
            {
                int font = c.GetProperty("font").GetInt32();
                if (!reference.Glyphs.ContainsKey(font)) continue;
                var settings = Settings(Asset(reference, font), c);
                generator.GetPreferredValues(settings, textInfo);
                var preferred = generator.GetPreferredValues(settings, textInfo);
                generator.GenerateText(settings, textInfo);
                if (!filter(c)) continue;
                cases++;
                int before = mismatches.Count;
                compare(c, preferred, textInfo, generator, mismatches);
                if (mismatches.Count > before) mismatches.Note("   in " + Name(c));
            }
            m_Output.WriteLine($"{cases} cases, {mismatches}");
            Assert.True(mismatches.Count == 0, mismatches.ToString());
        }

        [Fact]
        public void PreferredSizesMatchUnity() =>
            RunCases(_ => true, (c, preferred, _, _, m) =>
            {
                var expected = c.GetProperty("preferred");
                m.Float("preferred width", F(expected, 0), preferred.x);
                m.Float("preferred height", F(expected, 1), preferred.y);
            });

        [Fact]
        public void CharactersAndLinesMatchUnity() =>
            RunCases(_ => true, (c, _, info, generator, m) =>
            {
                m.Int("truncated", c.GetProperty("truncated").GetBoolean() ? 1 : 0, generator.isTextTruncated ? 1 : 0);
                m.Int("characterCount", c.GetProperty("characterCount").GetInt32(), info.characterCount);
                m.Int("lineCount", c.GetProperty("lineCount").GetInt32(), info.lineCount);
                var chars = c.GetProperty("chars");
                for (int i = 0; i < Math.Min(chars.GetArrayLength(), info.characterCount); i++)
                {
                    var e = chars[i];
                    var a = info.textElementInfo[i];
                    string at = $"char {i} '{(char)e[0].GetInt64()}'";
                    m.Int(at + " character", e[0].GetInt64(), a.character);
                    m.Int(at + " index", e[1].GetInt32(), a.index);
                    m.Int(at + " stringLength", e[2].GetInt32(), a.stringLength);
                    m.Int(at + " line", e[3].GetInt32(), a.lineNumber);
                    m.Int(at + " visible", e[4].GetInt32(), a.isVisible ? 1 : 0);
                    m.Float(at + " origin", F(e, 5), a.origin);
                    m.Float(at + " xAdvance", F(e, 6), a.xAdvance);
                    m.Float(at + " ascender", F(e, 7), a.ascender);
                    m.Float(at + " descender", F(e, 8), a.descender);
                    m.Float(at + " baseLine", F(e, 9), a.baseLine);
                    m.Float(at + " bottomLeft.x", F(e, 10), a.bottomLeft.x);
                    m.Float(at + " bottomLeft.y", F(e, 11), a.bottomLeft.y);
                    m.Float(at + " topRight.x", F(e, 12), a.topRight.x);
                    m.Float(at + " topRight.y", F(e, 13), a.topRight.y);
                    m.Float(at + " topLeft.x", F(e, 14), a.topLeft.x);
                    m.Float(at + " bottomRight.x", F(e, 15), a.bottomRight.x);
                    m.Float(at + " scale", F(e, 16), a.scale);
                    m.Float(at + " pointSize", F(e, 17), a.pointSize);
                    m.Int(at + " style", e[18].GetInt32(), (int)a.style);
                    m.Int(at + " material", e[19].GetInt32(), a.materialReferenceIndex);
                }
                var lines = c.GetProperty("lines");
                for (int i = 0; i < Math.Min(lines.GetArrayLength(), info.lineCount); i++)
                {
                    var e = lines[i];
                    var l = info.lineInfo[i];
                    string at = $"line {i}";
                    m.Int(at + " characterCount", e[0].GetInt32(), l.characterCount);
                    m.Int(at + " visibleCharacterCount", e[1].GetInt32(), l.visibleCharacterCount);
                    m.Int(at + " spaceCount", e[2].GetInt32(), l.spaceCount);
                    m.Int(at + " wordCount", e[3].GetInt32(), l.wordCount);
                    m.Int(at + " firstCharacterIndex", e[4].GetInt32(), l.firstCharacterIndex);
                    m.Int(at + " firstVisibleCharacterIndex", e[5].GetInt32(), l.firstVisibleCharacterIndex);
                    m.Int(at + " lastCharacterIndex", e[6].GetInt32(), l.lastCharacterIndex);
                    m.Int(at + " lastVisibleCharacterIndex", e[7].GetInt32(), l.lastVisibleCharacterIndex);
                    m.Float(at + " length", F(e, 8), l.length);
                    m.Float(at + " lineHeight", F(e, 9), l.lineHeight);
                    m.Float(at + " ascender", F(e, 10), l.ascender);
                    m.Float(at + " baseline", F(e, 11), l.baseline);
                    m.Float(at + " descender", F(e, 12), l.descender);
                    m.Float(at + " maxAdvance", F(e, 13), l.maxAdvance);
                    m.Float(at + " width", F(e, 14), l.width);
                    m.Int(at + " alignment", e[15].GetInt32(), (int)l.alignment);
                    m.Float(at + " extents min x", F(e, 16), l.lineExtents.min.x);
                    m.Float(at + " extents min y", F(e, 17), l.lineExtents.min.y);
                    m.Float(at + " extents max x", F(e, 18), l.lineExtents.max.x);
                    m.Float(at + " extents max y", F(e, 19), l.lineExtents.max.y);
                }
            });

        [Fact]
        public void MeshVerticesMatchUnity() =>
            RunCases(c => c.TryGetProperty("meshes", out _), (c, _, info, _, m) =>
            {
                var meshes = c.GetProperty("meshes");
                m.Int("materialCount", c.GetProperty("materialCount").GetInt32(), info.materialCount);
                for (int k = 0; k < Math.Min(meshes.GetArrayLength(), info.materialCount); k++)
                {
                    var mesh = meshes[k];
                    var actual = info.meshInfo[k];
                    m.Int($"mesh {k} vertexCount", mesh.GetProperty("vertexCount").GetInt32(), actual.vertexCount);
                    m.Int($"mesh {k} renderMode", mesh.GetProperty("renderMode").GetInt32(), (int)actual.glyphRenderMode);
                    var vertices = mesh.GetProperty("vertices");
                    int count = Math.Min(mesh.GetProperty("vertexCount").GetInt32(), actual.vertexCount);
                    for (int v = 0; v < count; v++)
                    {
                        var a = actual.vertexData[v];
                        int o = v * 11;
                        string at = $"mesh {k} vertex {v}";
                        m.Float(at + " x", F(vertices, o), a.position.x);
                        m.Float(at + " y", F(vertices, o + 1), a.position.y);
                        m.Float(at + " z", F(vertices, o + 2), a.position.z);
                        m.Int(at + " color", ((long)vertices[o + 3].GetInt32() << 24) | ((long)vertices[o + 4].GetInt32() << 16) | ((long)vertices[o + 5].GetInt32() << 8) | (long)vertices[o + 6].GetInt32(),
                            ((long)a.color.r << 24) | ((long)a.color.g << 16) | ((long)a.color.b << 8) | a.color.a);
                        m.Float(at + " uv0.x", F(vertices, o + 7), a.uv0.x);
                        m.Float(at + " uv0.y", F(vertices, o + 8), a.uv0.y);
                        m.Float(at + " uv2.x", F(vertices, o + 9), a.uv2.x);
                        m.Float(at + " uv2.y", F(vertices, o + 10), a.uv2.y);
                    }
                }
            });
    }
}
