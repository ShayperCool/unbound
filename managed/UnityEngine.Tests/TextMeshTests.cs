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
    /// laid out into one quad per visible character, which the MeshRenderer draws instead of a
    /// MeshFilter's mesh. No Reference measures TextMesh: these are expectations per the docs, plus
    /// Unbound's labelled choices (a font pixel is characterSize / 10 units; the legacy
    /// generator's metrics and padding). The box font (<see cref="BoxGlyphSource"/>) at its size 10
    /// advances 5 pixels per glyph; ink is 5 x 7 on the baseline, ascent 8, descent 2, and every
    /// quad takes the atlas' one-pixel padding on each side. characterSize 10 makes one pixel one
    /// unit, so the expected positions below are the generator's pixel positions.
    /// Only API that exists before TextMesh is implemented is used, so each case fails on its own
    /// against the stub.
    /// </summary>
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

        // Red at the stub: the MeshRenderer has no MeshFilter, so it draws no mesh (null).
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
            Assert.Equal(new[] { 0, 1, 2, 2, 3, 0 }, triangles.Take(6));
            Assert.Equal(16, mesh.colors32.Length);
            Assert.Equal(16, mesh.uv.Length);
            Assert.All(mesh.colors32, c => Assert.Equal(new Color32(255, 255, 255, 255), c));
        }

        // Red at the stub: no mesh is drawn (null).
        [Fact]
        public void ChangingAPropertyRegeneratesTheMesh()
        {
            var text = Make("A", BoxFont());
            Assert.Equal(4, Drawn(text).vertexCount);
            text.text = "AB";
            Assert.Equal(8, Drawn(text).vertexCount);
            text.color = Color.red;
            Assert.All(Drawn(text).colors32, c => Assert.Equal(new Color32(255, 0, 0, 255), c));
        }

        // "anchor: Which point of the text shares the position of the Transform." One line "AB" is
        // 10 pixels wide; the line spans 8 above and 2 below its baseline.
        // Red at the stub: no mesh is drawn (null).
        [Theory]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Left, -1f, -9f, 11f, 0f)]
        [InlineData(TextAnchor.MiddleCenter, TextAlignment.Center, -6f, -4f, 6f, 5f)]
        [InlineData(TextAnchor.LowerRight, TextAlignment.Right, -11f, 1f, 1f, 10f)]
        public void AnchorPutsItsPointOfTheTextOnTheTransform(TextAnchor anchor, TextAlignment alignment, float minX, float minY, float maxX, float maxY)
        {
            var text = Make("AB", BoxFont());
            text.anchor = anchor;
            text.alignment = alignment;
            var bounds = Drawn(text).bounds;
            Assert.Equal(new Vector3(minX, minY, 0f), bounds.min);
            Assert.Equal(new Vector3(maxX, maxY, 0f), bounds.max);
        }

        // "alignment: How lines of text are aligned (Left, Right, Center)": within the block, which
        // the anchor places. Lines "AB" (10 pixels) and "A" (5); quads 0, 1 are line one's A, B and
        // quad 2 is line two's A, one line (10 pixels) lower.
        // Red at the stub: no mesh is drawn (null).
        [Theory]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Left, -1f, -1f)]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Right, -1f, 4f)]
        [InlineData(TextAnchor.UpperLeft, TextAlignment.Center, -1f, 1f)]
        [InlineData(TextAnchor.UpperRight, TextAlignment.Left, -11f, -11f)]
        [InlineData(TextAnchor.UpperRight, TextAlignment.Right, -11f, -6f)]
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

        // "characterSize: The size of each character (This scales the whole text)"; offsetZ moves
        // the text along z, unscaled (Unbound's choice).
        // Red at the stub: no mesh is drawn (null).
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
        }

        // "tabSize: How much space will be inserted for a tab '\t' character. This is a multiplum of
        // the 'spacebar' character offset": with tabSize 2, B starts 5 + 2 x 5 pixels after A.
        // Red at the stub: no mesh is drawn (null).
        [Fact]
        public void TabsAdvanceTabSizeSpaces()
        {
            var text = Make("A\tB", BoxFont());
            text.tabSize = 2f;
            var vertices = Drawn(text).vertices;
            Assert.Equal(8, vertices.Length);
            Assert.Equal(-1f, Quad(vertices, 0).min.x);
            Assert.Equal(14f, Quad(vertices, 1).min.x);
        }

        // The MeshRenderer draws the TextMesh's mesh, not its MeshFilter's, and its bounds are the
        // text's. Red at the stub: the renderer draws the MeshFilter's three-vertex mesh.
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
            Assert.Equal(new Vector3(-1f, -9f, 0f), renderer.localBounds.min);
            Assert.Equal(new Vector3(6f, 0f, 0f), renderer.localBounds.max);
            // Drawn with the renderer's own material: Unbound assigns none.
            Assert.Same(material, renderer.sharedMaterial);
        }

        // Loud, not silent: a TextMesh whose MeshRenderer has no material (as one added by script
        // has) is reported once per regeneration through the stub report. Unbound's choice
        // (unmeasured): no material is assigned. Red at 6eb03391: nothing is reported; red at the
        // stub: nothing is drawn.
        [Fact]
        public void AMissingMaterialIsReported()
        {
            static int Hits() => Stubs.HitReport().FirstOrDefault(h => h.Key == "TextMesh without a material").Value;
            int before = Hits();
            var text = Make("AB", BoxFont());
            Assert.NotNull(Drawn(text));
            Assert.Equal(before + 1, Hits());
        }

        // Without a font nothing is drawn, not even a MeshFilter's mesh.
        // Red at the stub: the renderer draws the MeshFilter's mesh.
        [Fact]
        public void NoFontDrawsNothing()
        {
            var text = Make("AB", null);
            var other = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            text.gameObject.AddComponent<MeshFilter>().sharedMesh = other;
            var renderer = text.GetComponent<MeshRenderer>();
            Assert.Null(renderer.DrawGeometry(out _));
            Assert.Equal(default(Bounds), renderer.localBounds);
            text.font = BoxFont();
            Assert.Equal(8, renderer.DrawGeometry(out _).vertexCount);
            text.font = null;
            Assert.Null(renderer.DrawGeometry(out _));
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
            Assert.Null(text.font);
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
