using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// The legacy TextMesh (Unity 6000.0 Scripting API, TextMesh and its properties): its text is
    /// laid out into one quad per character, which the MeshRenderer draws instead of a
    /// MeshFilter's mesh. The rules tested are those measured in the Unity 6000.0.71f1 Editor
    /// (tests/projects/textmesh: a font pixel is characterSize / 10 units, unrounded metrics,
    /// tab stops, empty quads, bounds), applied to a font of the tests' own. The box font (<see cref="BoxGlyphSource"/>) at its size 10
    /// advances 5 pixels per glyph; ink is 5 x 7 on the baseline, ascent 8, descent 2, and every
    /// quad takes the atlas' one-pixel padding on each side. characterSize 10 makes one pixel one
    /// unit, so the expected positions below are the layout's pixel positions.
    /// </summary>
    [Collection("Project release")]
    public sealed class TextMeshTests : IDisposable
    {
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<string> m_Reports = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        public TextMeshTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            // Unbound's own diagnostics skip Application.logMessageReceived: capture at the handler.
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Reports);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
        }

        public void Dispose()
        {
            try
            {
                foreach (var go in m_Made)
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                m_Logger.logHandler = m_PreviousHandler;
                m_Logger.logEnabled = m_PreviousLogEnabled;
                m_Logger.filterLogType = m_PreviousFilter;
            }
        }

        static Font BoxFont() => new Font(new BoxGlyphSource(), 10, "Box");

        TextMesh Make(string text, Font font, float characterSize = 10f)
        {
            var go = new GameObject("text mesh");
            m_Made.Add(go);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.characterSize = characterSize;
            mesh.text = text;
            return mesh;
        }

        static Mesh Drawn(TextMesh text) => text.GetComponent<MeshRenderer>().DrawGeometry(out _);

        static (Vector2 min, Vector2 max) Quad(Vector3[] vertices, int quad)
        {
            var corners = vertices.Skip(quad * 4).Take(4).ToArray();
            return (new Vector2(corners.Min(v => v.x), corners.Min(v => v.y)), new Vector2(corners.Max(v => v.x), corners.Max(v => v.y)));
        }

        // Red at the stub: setters are ignored and getters return defaults (text null, font null, ...).
        [Fact]
        public void DocumentedPropertiesRoundTrip()
        {
            var go = new GameObject("text mesh");
            m_Made.Add(go);
            var text = go.AddComponent<TextMesh>();
            var font = BoxFont();
            text.text = "line one\nline two";
            text.font = font;
            text.fontSize = 24;
            text.fontStyle = FontStyle.BoldAndItalic;
            text.offsetZ = -0.25f;
            text.alignment = TextAlignment.Right;
            text.anchor = TextAnchor.LowerCenter;
            text.characterSize = 0.05f;
            text.lineSpacing = 1.5f;
            text.tabSize = 2f;
            text.richText = false;
            text.color = new Color(0.25f, 0.5f, 0.75f, 0.5f);
            Assert.Equal("line one\nline two", text.text);
            Assert.Same(font, text.font);
            Assert.Equal(24, text.fontSize);
            Assert.Equal(FontStyle.BoldAndItalic, text.fontStyle);
            Assert.Equal(-0.25f, text.offsetZ);
            Assert.Equal(TextAlignment.Right, text.alignment);
            Assert.Equal(TextAnchor.LowerCenter, text.anchor);
            Assert.Equal(0.05f, text.characterSize);
            Assert.Equal(1.5f, text.lineSpacing);
            Assert.Equal(2f, text.tabSize);
            Assert.False(text.richText);
            Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 0.5f), text.color);
            // RequireComponent, Unity's public metadata.
            Assert.NotNull(go.GetComponent<MeshRenderer>());
        }

        // Measured (tests/projects/textmesh): one quad per character except whitespace and
        // newlines, triangles (1, 2, 0) and (2, 3, 0) of each quad, an unnamed hidden mesh.
        [Fact]
        public void GeneratesOneQuadPerVisibleCharacter()
        {
            var text = Make("AB C\nD", BoxFont());
            var mesh = Drawn(text);
            Assert.NotNull(mesh);
            // Space and newline advance without quads: A, B, C, D.
            Assert.Equal(16, mesh.vertexCount);
            var triangles = mesh.triangles;
            Assert.Equal(24, triangles.Length);
            Assert.Equal(new[] { 1, 2, 0, 2, 3, 0, 5, 6, 4, 6, 7, 4 }, triangles.Take(12));
            Assert.Equal(16, mesh.colors32.Length);
            Assert.Equal(16, mesh.uv.Length);
            Assert.Empty(mesh.normals);
            Assert.All(mesh.colors32, c => Assert.Equal(new Color32(255, 255, 255, 255), c));
            Assert.Equal("", mesh.name);
            Assert.Equal(HideFlags.HideAndDontSave, mesh.hideFlags);
        }

        [Fact]
        public void ChangingAPropertyRegeneratesTheMesh()
        {
            var text = Make("A", BoxFont());
            var first = Drawn(text);
            Assert.Equal(4, first.vertexCount);
            text.text = "AB";
            Assert.Equal(8, Drawn(text).vertexCount);
            text.color = Color.red;
            Assert.All(Drawn(text).colors32, c => Assert.Equal(new Color32(255, 0, 0, 255), c));
        }

        // Rebuilt only on change: drawing unchanged text again allocates nothing.
        [Fact]
        public void UnchangedTextIsNotRegenerated()
        {
            var text = Make("AB\nCD", BoxFont());
            var mesh = text.RenderMesh();
            var vertices = mesh.vertices;
            text.RenderMesh();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) Assert.Same(mesh, text.RenderMesh());
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
            Assert.Equal(vertices, mesh.vertices);
        }

        // "anchor: Which point of the text shares the position of the Transform." Measured: the
        // bounds are the text block (advances wide, ascent minus descent high), not the quads.
        // One line "AB" is 10 pixels wide; the box font's ascent is 8 and its descent 2.
        [Theory]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Left, 0f, -10f, 10f, 0f)]
        [InlineData(TextAnchor.MiddleCenter, TextAlignment.Center, -5f, -5f, 5f, 5f)]
        [InlineData(TextAnchor.LowerRight, TextAlignment.Right, -10f, 0f, 0f, 10f)]
        public void AnchorPutsItsPointOfTheTextOnTheTransform(TextAnchor anchor, TextAlignment alignment, float minX, float minY, float maxX, float maxY)
        {
            var text = Make("AB", BoxFont());
            text.anchor = anchor;
            text.alignment = alignment;
            var bounds = Drawn(text).bounds;
            Assert.Equal(new Vector3(minX, minY, 0f), bounds.min);
            Assert.Equal(new Vector3(maxX, maxY, 0f), bounds.max);
            Assert.Equal(bounds, text.GetComponent<MeshRenderer>().localBounds);
        }

        // "alignment: How lines of text are aligned (Left, Right, Center)": within the block, which
        // the anchor places, unrounded (measured). Lines "AB" (10 pixels) and "A" (5); quads 0, 1
        // are line one's A, B and quad 2 is line two's A, one line (10 pixels) lower.
        [Theory]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Left, -1f, -1f)]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Right, -1f, 4f)]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Center, -1f, 1.5f)]
        [InlineData(TextAnchor.UpperRight, TextAlignment.Left, -11f, -11f)]
        [InlineData(TextAnchor.UpperRight, TextAlignment.Right, -11f, -6f)]
        [InlineData(TextAnchor.UpperCenter, TextAlignment.Left, -6f, -6f)]
        public void AlignmentAlignsLinesWithinTheAnchoredBlock(TextAnchor anchor, TextAlignment alignment, float firstLineLeft, float secondLineLeft)
        {
            var text = Make("AB\nA", BoxFont());
            text.anchor = anchor;
            text.alignment = alignment;
            var vertices = Drawn(text).vertices;
            Assert.Equal(12, vertices.Length);
            Assert.Equal(firstLineLeft, Quad(vertices, 0).min.x);
            Assert.Equal(secondLineLeft, Quad(vertices, 2).min.x);
            Assert.Equal(Quad(vertices, 0).max.y - 10f, Quad(vertices, 2).max.y);
        }

        // Measured: lines step by the font's line spacing times lineSpacing, unrounded; the block
        // is those steps plus the last line's ascent minus descent; the first baseline is the
        // ascent below the top (quad tops: ink 7 above the baseline, plus one pixel of padding).
        [Fact]
        public void LinesStepByTheUnroundedLineSpacing()
        {
            var text = Make("A\nA\nA", BoxFont());
            text.lineSpacing = 1.25f;
            var mesh = Drawn(text);
            Assert.Equal(0f - 8f + 7f + 1f, Quad(mesh.vertices, 0).max.y);
            Assert.Equal(-12.5f, Quad(mesh.vertices, 1).max.y);
            Assert.Equal(-25f, Quad(mesh.vertices, 2).max.y);
            Assert.Equal(new Vector3(0f, -35f, 0f), mesh.bounds.min);
        }

        // "characterSize: The size of each character (This scales the whole text)"; offsetZ moves
        // the text along z, unscaled (measured).
        [Fact]
        public void CharacterSizeScalesTheWholeText()
        {
            var text = Make("AB\nA", BoxFont(), characterSize: 1f);
            text.anchor = TextAnchor.MiddleCenter;
            var small = Drawn(text).vertices;
            text.characterSize = 2f;
            var large = Drawn(text).vertices;
            Assert.Equal(small.Length, large.Length);
            Assert.NotEmpty(small);
            for (int i = 0; i < small.Length; i++)
                Assert.Equal(small[i] * 2f, large[i]);
            text.offsetZ = 0.5f;
            Assert.All(Drawn(text).vertices, v => Assert.Equal(0.5f, v.z));
            Assert.Equal(0.5f, Drawn(text).bounds.center.z);
            Assert.Equal(0f, Drawn(text).bounds.size.z);
        }

        // "tabSize: ... a multiplum of the 'spacebar' character offset". Measured: a tab moves the
        // pen to the next multiple, strictly after it, of the whole part of tabSize times the space
        // advance (5 pixels here); 0 goes back to the line's start.
        [Theory]
        [InlineData("A\tB", 2f, 10f)]
        [InlineData("A\tB", 2.9f, 10f)]
        [InlineData("AB\tC", 2f, 20f)]
        [InlineData("\tB", 3f, 15f)]
        [InlineData("AB\tC", 0f, 0f)]
        public void TabsAdvanceToTheNextTabStop(string line, float tabSize, float lastPen)
        {
            var text = Make(line, BoxFont());
            text.tabSize = tabSize;
            var vertices = Drawn(text).vertices;
            Assert.Equal(lastPen - 1f, Quad(vertices, vertices.Length / 4 - 1).min.x);
        }

        // Measured: a character the font lacks keeps an empty quad (the atlas' padding around an
        // empty cell) at the pen and does not advance; so does a carriage return (a point).
        [Fact]
        public void MissingGlyphsAndCarriageReturnsKeepEmptyQuads()
        {
            var font = BoxFont();
            var mesh = Drawn(Make("A☃B", font));
            Assert.Equal(12, mesh.vertexCount);
            var (min, max) = Quad(mesh.vertices, 1);
            Assert.Equal(new Vector2(4f, -9f), min);
            Assert.Equal(new Vector2(6f, -7f), max);
            Assert.Equal(4f, Quad(mesh.vertices, 2).min.x);
            var texture = font.material.mainTexture;
            var uv = mesh.uv.Skip(4).Take(4).ToArray();
            Assert.Equal(2f, (uv.Max(p => p.x) - uv.Min(p => p.x)) * texture.width, 3);
            Assert.Equal(2f, (uv.Max(p => p.y) - uv.Min(p => p.y)) * texture.height, 3);

            var returned = Drawn(Make("A\r\nB", font));
            Assert.Equal(12, returned.vertexCount);
            Assert.All(returned.vertices.Skip(4).Take(4), v => Assert.Equal(new Vector3(5f, -8f, 0f), v));
            Assert.All(returned.uv.Skip(4).Take(4), p => Assert.Equal(Vector2.zero, p));
        }

        // Measured: new Font(name) is an empty custom font (not dynamic, ascent 0, lines 0.1 pixels
        // apart) filled through characterInfo; a character it lacks is a point at the pen; its own
        // uvs go to the quad's corners as given.
        [Fact]
        public void ScriptMadeCustomFontsLayOutTheirCharacterRects()
        {
            var font = new Font("Probe custom");
            Assert.False(font.dynamic);
            var a = new CharacterInfo { index = 'A', advance = 9, minX = 1, maxX = 8, minY = -2, maxY = 10 };
            a.uvTopLeft = new Vector2(0f, 1f); a.uvTopRight = new Vector2(0.5f, 1f);
            a.uvBottomRight = new Vector2(0.5f, 0f); a.uvBottomLeft = new Vector2(0f, 0f);
            font.characterInfo = new[] { a };
            var text = Make("AxA\nA", font);
            var mesh = Drawn(text);
            Assert.Equal(16, mesh.vertexCount);
            Assert.Equal((new Vector2(1f, -2f), new Vector2(8f, 10f)), Quad(mesh.vertices, 0));
            Assert.All(mesh.vertices.Skip(4).Take(4), v => Assert.Equal(new Vector3(9f, 0f, 0f), v));
            Assert.Equal(10f, Quad(mesh.vertices, 2).min.x);
            Assert.Equal(-2f - Font.NewFontLineSpacing, Quad(mesh.vertices, 3).min.y, 4);
            Assert.Equal(new[] { a.uvTopLeft, a.uvTopRight, a.uvBottomRight, a.uvBottomLeft }, mesh.uv.Take(4));
            Assert.Equal(Font.NewFontLineSpacing, mesh.bounds.size.y, 4);
        }

        // Measured: a size (or style) override on a font that is not dynamic warns at each layout,
        // not when the properties are set.
        [Fact]
        public void OverridesOnACustomFontWarnAtLayout()
        {
            string version = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                const string warning = "Warning: Font size and style overrides are only supported for dynamic fonts.";
                var text = Make("A", new Font("Probe custom"));
                m_Reports.Clear();
                text.fontSize = 30;
                text.fontStyle = FontStyle.Bold;
                Assert.Empty(m_Reports);
                Drawn(text);
                Assert.Equal(new[] { warning }, m_Reports);
                Drawn(text);
                Assert.Single(m_Reports);
                text.text = "AA";
                Drawn(text);
                Assert.Equal(2, m_Reports.Count);
                m_Reports.Clear();
                text.font = BoxFont();
                Drawn(text);
                Assert.Empty(m_Reports);
            }
            finally { ProjectInfo.UnityVersion = version; }
        }

        // The MeshRenderer draws the TextMesh's mesh, not its MeshFilter's.
        [Fact]
        public void MeshRendererDrawsTheTextMeshInsteadOfTheMeshFilter()
        {
            var text = Make("A", BoxFont());
            var other = new Mesh { vertices = new[] { new Vector3(-50f, -50f, -50f), new Vector3(50f, 0f, 0f), new Vector3(0f, 50f, 0f) }, triangles = new[] { 0, 1, 2 } };
            text.gameObject.AddComponent<MeshFilter>().sharedMesh = other;
            var renderer = text.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("GUI/Text Shader") ?? Shader.Create("GUI/Text Shader", 3000));
            renderer.sharedMaterial = material;
            var mesh = renderer.DrawGeometry(out _);
            Assert.NotNull(mesh);
            Assert.NotSame(other, mesh);
            Assert.Equal(4, mesh.vertexCount);
            Assert.Equal(new Vector3(0f, -10f, 0f), renderer.localBounds.min);
            Assert.Equal(new Vector3(5f, 0f, 0f), renderer.localBounds.max);
            Assert.Same(material, renderer.sharedMaterial);
        }

        // Measured: AddComponent gives a renderer without a material the font's material, keeps
        // one the renderer has, and a later font change changes no material.
        [Fact]
        public void AddedTextMeshesTakeTheFontMaterialOnlyWhenTheRendererHasNone()
        {
            var go = new GameObject("preset");
            m_Made.Add(go);
            var preset = new Material(Shader.Create("Probe/Preset", 2000));
            go.AddComponent<MeshRenderer>().sharedMaterial = preset;
            var text = go.AddComponent<TextMesh>();
            Assert.Same(preset, go.GetComponent<MeshRenderer>().sharedMaterial);
            text.font = BoxFont();
            Assert.Same(preset, go.GetComponent<MeshRenderer>().sharedMaterial);
        }

        // Loud, not silent: a TextMesh whose MeshRenderer has no material is reported once per
        // regeneration through the stub report.
        [Fact]
        public void AMissingMaterialIsReported()
        {
            static int Hits() => Stubs.HitReport().FirstOrDefault(h => h.Key == "TextMesh without a material").Value;
            int before = Hits();
            var text = Make("AB", BoxFont());
            text.GetComponent<MeshRenderer>().sharedMaterials = new Material[1];
            Assert.NotNull(Drawn(text));
            Assert.Equal(before + 1, Hits());
        }

        // Measured: a TextMesh without a font (none set, or set to null) uses the built-in
        // LegacyRuntime.ttf, which its font property answers.
        [Fact]
        public void WithoutAFontTheBuiltInFontIsUsed()
        {
            var text = Make("AB", null);
            Assert.Same(Font.GetDefault(), text.font);
            text.font = BoxFont();
            Assert.Equal(8, text.GetComponent<MeshRenderer>().DrawGeometry(out _).vertexCount);
            text.font = null;
            Assert.Same(Font.GetDefault(), text.font);
        }

        // A TextMesh as Unity 6000.0's shipped data serializes it (the URP package samples'
        // Shaders/Lit/Prefabs/*Podium.prefab: serializedVersion 3 and these field names); the
        // values here are the test's own. The packed rgba has red in its low byte (Unbound's
        // choice, unmeasured).
        const string SerializedTextMesh = """
            --- !u!1 &1
            GameObject:
              m_Name: label
              m_IsActive: 1
              m_Component:
              - component: {fileID: 2}
              - component: {fileID: 3}
              - component: {fileID: 4}
            --- !u!4 &2
            Transform:
              m_GameObject: {fileID: 1}
              m_LocalPosition: {x: 0, y: 0, z: 0}
              m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
              m_LocalScale: {x: 1, y: 1, z: 1}
            --- !u!23 &3
            MeshRenderer:
              m_GameObject: {fileID: 1}
              m_Enabled: 1
            --- !u!102 &4
            TextMesh:
              serializedVersion: VERSION
              m_GameObject: {fileID: 1}
              m_Text: Hi
              m_OffsetZ: 0.5
              m_CharacterSize: 0.05
              m_LineSpacing: 1.25
              m_Anchor: 4
              m_Alignment: 1
              m_TabSize: 3
              m_FontSize: 50
              m_FontStyle: 2
              m_RichText: 0
              m_Font: {fileID: 0}
              m_Color:
                serializedVersion: 2
                rgba: 1144201745
            """;

        TextMesh Load(int version)
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(SerializedTextMesh.Replace("VERSION", version.ToString())),
                "textmesh.unity", null, ObjectGraphBuilder.Kind.Scene, null);
            if (file.Objects.TryGetValue(1, out var go)) m_Made.Add((GameObject)go);
            Assert.True(file.Objects.TryGetValue(4, out var component), "the TextMesh document was not loaded");
            return Assert.IsType<TextMesh>(component);
        }

        // Red at the stub: class 102 is not registered, so the document is skipped.
        [Fact]
        public void SerializedTextMeshLoadsItsFields()
        {
            var text = Load(3);
            Assert.Equal("Hi", text.text);
            Assert.Equal(0.5f, text.offsetZ);
            Assert.Equal(0.05f, text.characterSize);
            Assert.Equal(1.25f, text.lineSpacing);
            Assert.Equal(TextAnchor.MiddleCenter, text.anchor);
            Assert.Equal(TextAlignment.Center, text.alignment);
            Assert.Equal(3f, text.tabSize);
            Assert.Equal(50, text.fontSize);
            Assert.Equal(FontStyle.Italic, text.fontStyle);
            Assert.False(text.richText);
            Assert.Same(Font.GetDefault(), text.font);
            Assert.Equal((Color)new Color32(0x11, 0x22, 0x33, 0x44), text.color);
            Assert.Same(text, text.gameObject.GetComponent<TextMesh>());
            Assert.DoesNotContain(m_Reports, r => r.Contains("TextMesh serializedVersion"));
        }

        // Field names are verified for serializedVersion 3 only: another version warns loudly.
        // Red at the stub: the only report is that class 102 is not supported (and nothing loads).
        [Fact]
        public void UnverifiedSerializedVersionWarns()
        {
            var text = Load(2);
            Assert.Contains(m_Reports, r => r.StartsWith("Warning: ", StringComparison.Ordinal)
                && r.Contains("TextMesh serializedVersion 2") && r.Contains("authored values may not load"));
            Assert.Equal("Hi", text.text);
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Reports;

            internal CaptureHandler(ILogHandler inner, List<string> reports)
            {
                m_Inner = inner;
                m_Reports = reports;
            }

            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    m_Reports.Add($"{type}: {message}");
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                if (exception != null) m_Reports.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }
    }
}
