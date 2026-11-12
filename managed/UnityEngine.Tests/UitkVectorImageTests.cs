using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// VectorImage drawing and SVG importer settings. The rules come from the Unity 6000.0.71f1 Editor
// (tests/projects/uitkvector); these tests check them on hand-made images outside the probe's
// assets: other sizes, aspect ratios and slice widths.

namespace UnityEngine.Tests
{
    public class UitkVectorImageTests
    {
        /// <summary>A w x h image: one quad over the whole image, white, optionally with a gradient
        /// atlas and <paramref name="settings"/> gradient rows, its vertices using
        /// <paramref name="setting"/>.</summary>
        static VectorImage Quad(float w, float h, Color32 tint, int settings = 0, uint setting = 0)
        {
            var vi = ScriptableObject.CreateInstance<VectorImage>();
            vi.vertices = new[]
            {
                new VectorImageVertex { position = new Vector3(0, 0, Vertex.nearZ), tint = tint, settingIndex = setting },
                new VectorImageVertex { position = new Vector3(w, 0, Vertex.nearZ), tint = tint, uv = new Vector2(1, 0), settingIndex = setting },
                new VectorImageVertex { position = new Vector3(w, h, Vertex.nearZ), tint = tint, uv = new Vector2(1, 1), settingIndex = setting },
                new VectorImageVertex { position = new Vector3(0, h, Vertex.nearZ), tint = tint, uv = new Vector2(0, 1), settingIndex = setting },
            };
            vi.indices = new ushort[] { 0, 1, 2, 0, 2, 3 };
            vi.size = new Vector2(w, h);
            if (settings > 0)
            {
                vi.atlas = new Texture2D(4, 4);
                vi.settings = new GradientSettings[settings];
                for (int i = 0; i < settings; i++) vi.settings[i] = new GradientSettings { location = new RectInt(0, i, 4, 1) };
            }
            return vi;
        }

        static UaPainter Paint(float scale, params VisualElement[] elements)
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            panel.scale = scale;
            var root = panel.visualTree;
            root.style.width = 400;
            root.style.height = 400;
            foreach (var e in elements)
            {
                e.style.position = Position.Absolute;
                root.Add(e);
            }
            panel.ApplyStyles();
            panel.ValidateLayout();
            var painter = new UaPainter();
            painter.Paint(panel, 400f * scale);
            return painter;
        }

        static VisualElement Element(float w, float h, Action<IStyle> style)
        {
            var e = new VisualElement();
            e.style.width = w;
            e.style.height = h;
            style(e.style);
            return e;
        }

        /// <summary>The bounds of the drawn triangles (UIR also copies an image's unreferenced
        /// vertices: those its triangles had before cutting).</summary>
        static (Vector2 min, Vector2 max) Bounds(UaPainter p)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var i in p.Indices)
            {
                min = Vector2.Min(min, p.Positions[i]);
                max = Vector2.Max(max, p.Positions[i]);
            }
            return (min, max);
        }

        static float[] Drawn(UaPainter p, int axis) =>
            p.Indices.Select(i => (float)Math.Round(p.Positions[i][axis], 4)).Distinct().OrderBy(v => v).ToArray();

        static float Area(UaPainter p)
        {
            float area = 0f;
            for (int i = 0; i + 2 < p.Indices.Count; i += 3)
            {
                Vector2 a = p.Positions[p.Indices[i]], b = p.Positions[p.Indices[i + 1]], c = p.Positions[p.Indices[i + 2]];
                area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) / 2f;
            }
            return area;
        }

        [Fact]
        public void BackgroundScaleToFitCentresAndScaleAndCropStretches()
        {
            var image = Quad(10, 10, Color.white);
            var fit = Paint(1f, Element(40, 20, s => { s.backgroundImage = new StyleBackground(image); s.unityBackgroundScaleMode = ScaleMode.ScaleToFit; }));
            Assert.Equal((new Vector2(10, 0), new Vector2(30, 20)), Bounds(fit));
            // A background's scale-and-crop stretches a vector image over the element.
            var crop = Paint(1f, Element(40, 20, s => { s.backgroundImage = new StyleBackground(image); s.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; }));
            Assert.Equal((new Vector2(0, 0), new Vector2(40, 20)), Bounds(crop));
        }

        [Fact]
        public void ImageScaleAndCropCutsTheImageToTheElement()
        {
            // A 10x30 image cropped into 40x20: scaled by 4 (120 high), its middle 20 shown.
            var image = new Image { vectorImage = Quad(10, 30, Color.white), scaleMode = ScaleMode.ScaleAndCrop };
            image.style.width = 40;
            image.style.height = 20;
            var p = Paint(1f, image);
            Assert.Equal((new Vector2(0, 0), new Vector2(40, 20)), Bounds(p));
            Assert.Equal(800f, Area(p), 3);
            // A uv part is cut out alike: the quad's lower-right quarter, stretched.
            var part = new Image { vectorImage = Quad(10, 10, Color.white), uv = new Rect(0.5f, 0.5f, 0.5f, 0.5f) };
            part.style.width = 30;
            part.style.height = 30;
            var q = Paint(1f, part);
            Assert.Equal((new Vector2(0, 0), new Vector2(30, 30)), Bounds(q));
            Assert.Equal(900f, Area(q), 3);
        }

        /// <summary>A 30x30 image: its left 10 red, the rest blue.</summary>
        static VectorImage Strips()
        {
            var vi = ScriptableObject.CreateInstance<VectorImage>();
            Color32 red = new Color32(255, 0, 0, 255), blue = new Color32(0, 0, 255, 255);
            VectorImageVertex V(float x, float y, Color32 c) => new VectorImageVertex { position = new Vector3(x, y, Vertex.nearZ), tint = c };
            vi.vertices = new[] { V(0, 0, red), V(10, 0, red), V(10, 30, red), V(0, 30, red), V(10, 0, blue), V(30, 0, blue), V(30, 30, blue), V(10, 30, blue) };
            vi.indices = new ushort[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            vi.size = new Vector2(30, 30);
            return vi;
        }

        static (float min, float max) Span(UaPainter p, byte red, int axis)
        {
            var values = p.Indices.Where(i => p.Colors[i].r == red).Select(i => p.Positions[i][axis]).ToArray();
            return (values.Min(), values.Max());
        }

        [Fact]
        public void NineSliceKeepsTheEdgesAndIgnoresTheSliceScale()
        {
            var image = Strips();
            var p = Paint(1f, Element(90, 60, s =>
            {
                s.backgroundImage = new StyleBackground(image);
                s.unitySliceLeft = 10; s.unitySliceTop = 10; s.unitySliceRight = 10; s.unitySliceBottom = 10;
                s.unitySliceScale = 2f;
            }));
            Assert.Equal((0f, 10f), Span(p, 255, 0));
            Assert.Equal((10f, 90f), Span(p, 0, 0));
            Assert.Equal((0f, 60f), Span(p, 255, 1));
            Assert.Equal(5400f, Area(p), 2);
            // The image's slice lines land at the target's: the red strip's top row is split at y 10.
            Assert.Contains(p.Indices, i => p.Colors[i].r == 255 && Mathf.Abs(p.Positions[i].y - 10f) < 1e-4f);
            Assert.Contains(p.Indices, i => p.Colors[i].r == 255 && Mathf.Abs(p.Positions[i].y - 50f) < 1e-4f);
            // Smaller than its slices: the edges shrink together (20 of image across 14 and 12).
            var small = Paint(1f, Element(14, 12, s =>
            {
                s.backgroundImage = new StyleBackground(image);
                s.unitySliceLeft = 10; s.unitySliceTop = 10; s.unitySliceRight = 10; s.unitySliceBottom = 10;
            }));
            Assert.Equal((0f, 7f), Span(small, 255, 0));
            Assert.Equal((7f, 14f), Span(small, 0, 0));
            Assert.Equal(168f, Area(small), 2);
        }

        [Fact]
        public void GradientImagesIndexTheirOwnSettingsRows()
        {
            var first = Quad(10, 10, Color.white, settings: 2, setting: 1);
            var second = Quad(10, 10, Color.white, settings: 3, setting: 1);
            var p = Paint(1f,
                Element(10, 10, s => s.backgroundImage = new StyleBackground(first)),
                Element(10, 10, s => { s.left = 20; s.backgroundImage = new StyleBackground(second); }),
                Element(10, 10, s => { s.left = 40; s.backgroundImage = new StyleBackground(first); }));
            Assert.Equal(new[] { first, second }, p.GradientImages);
            Assert.Equal(5, p.GradientRowCount);
            // UIR's SVG gradient render type; the row as (high, low) bytes: 1, 2 + 1 = 3, 1.
            Assert.All(p.Uvs[3], f => Assert.Equal(4, Mathf.RoundToInt(f.x * 255f)));
            var rows = p.Uvs[5].Select(s => Mathf.RoundToInt(s.x * 255f) * 256 + Mathf.RoundToInt(s.y * 255f)).ToArray();
            Assert.Equal(new[] { 1, 1, 1, 1, 3, 3, 3, 3, 1, 1, 1, 1 }, rows);
        }

        // Editor: gradient-rows (60 copies of a five-gradient image on one panel). Rows are packed
        // as (row >> 8, row & 0xFF); the shader reads high * 255 + low, so row 300 is read as 299.
        [Fact]
        public void GradientRowsPastTheFirst256KeepUnitysPacking()
        {
            var many = Quad(10, 10, Color.white, settings: 300);
            var next = Quad(10, 10, Color.white, settings: 1, setting: 0);
            var p = Paint(1f,
                Element(10, 10, s => s.backgroundImage = new StyleBackground(many)),
                Element(10, 10, s => { s.left = 20; s.backgroundImage = new StyleBackground(next); }));
            Assert.Equal(301, p.GradientRowCount);
            var packed = p.Uvs[5][4];
            Assert.Equal(1, Mathf.RoundToInt(packed.x * 255f));
            Assert.Equal(44, Mathf.RoundToInt(packed.y * 255f));
        }

        [Fact]
        public void TheTintMultipliesVertexColours()
        {
            var image = new Image { vectorImage = Quad(10, 10, new Color32(200, 100, 255, 255)), tintColor = new Color(0.5f, 1f, 0f, 0.25f) };
            image.style.width = 10;
            image.style.height = 10;
            var p = Paint(1f, image);
            // (round(t * 255) + 1) * c >> 8 per channel.
            Assert.All(p.Colors, c => Assert.Equal(new Color32(100, 100, 0, 64), c));
        }

        [Fact]
        public void DrawVectorImageReversesTheWindingOfAMirroredImage()
        {
            var image = Quad(10, 10, Color.white);
            var e = new VisualElement();
            e.style.width = 50;
            e.style.height = 50;
            e.generateVisualContent += ctx => ctx.DrawVectorImage(image, new Vector2(40, 0), Angle.Degrees(0), new Vector2(-2, 1));
            var p = Paint(1f, e);
            Assert.Equal((new Vector2(20, 0), new Vector2(40, 10)), Bounds(p));
            Assert.Equal(new[] { 0, 2, 1, 0, 3, 2 }, p.Indices.ToArray());
        }

        [Fact]
        public void ContentRectsAlignToThePanelsPixels()
        {
            var aligned = new List<Rect>();
            var e = new VisualElement();
            e.style.width = 50.3f;
            e.style.height = 30.7f;
            e.generateVisualContent += ctx => aligned.Add(GUIUtility.AlignRectToDevice(ctx.visualElement.contentRect));
            Paint(1.25f, e);
            // 50.4 x 30.4 points (the layout's at 1.25) are 63 x 38 pixels: unchanged.
            Assert.Equal(50.4f, aligned[0].width, 4);
            Assert.Equal(30.4f, aligned[0].height, 4);
            Assert.Equal(0f, GUIUtility.uaContentPixelsPerPoint);
            // Outside a panel's content generation the grid is the GUI's (one pixel per point here).
            Assert.Equal(new Rect(0, 0, 50, 31), GUIUtility.AlignRectToDevice(new Rect(0, 0, 50.3f, 30.7f)));
        }

        sealed class Logs : ILogHandler
        {
            public readonly List<string> Lines = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        // Editor: the probe's logs (a PanelSettings made in Play mode without a theme). Unity only
        // warns in or entering Play mode.
        [Fact]
        public void PanelSettingsWithoutAThemeWarnInPlayModeOnly()
        {
            var handler = Debug.unityLogger.logHandler;
            bool playing = EditorSession.IsPlaying;
            var logs = new Logs();
            PanelSettings editMode = null, playMode = null;
            Debug.unityLogger.logHandler = logs;
            try
            {
                EditorSession.IsPlaying = false;
                editMode = ScriptableObject.CreateInstance<PanelSettings>();
                Assert.Empty(logs.Lines);
                EditorSession.IsPlaying = true;
                playMode = ScriptableObject.CreateInstance<PanelSettings>();
                Assert.Equal(new[] { "No Theme Style Sheet set to PanelSettings , UI will not render properly" }, logs.Lines);
            }
            finally
            {
                Object.DestroyImmediate(editMode);
                Object.DestroyImmediate(playMode);
                Debug.unityLogger.logHandler = handler;
                EditorSession.IsPlaying = playing;
            }
        }

        [Serializable]
        sealed class Nested
        {
            public string m_SpriteID = "";
            public int TessellationDetail;
        }

        sealed class SettingsImporter : ScriptableObject
        {
            [SerializeField] int m_SvgType;
            [SerializeField] Nested m_SpriteData = new Nested();
            [SerializeField] int plain;

            internal (int, string, int, int) Read() => (m_SvgType, m_SpriteData.m_SpriteID, m_SpriteData.TessellationDetail, plain);
        }

        [Fact]
        public void ImporterSettingsUseTheMetaFieldNames()
        {
            const string meta = @"
ScriptedImporter:
  svgType: 3
  spriteData:
    spriteID: abc
    TessellationDetail: 2
  plain: 7
  m_SvgType: 9
";
            var importer = ScriptableObject.CreateInstance<SettingsImporter>();
            new YamlReader(null, metaNames: true).ReadScriptObject(importer, TestYaml.DocumentBody(meta));
            Assert.Equal((3, "abc", 2, 7), importer.Read());
            // Asset documents keep the field names.
            var asset = ScriptableObject.CreateInstance<SettingsImporter>();
            new YamlReader(null).ReadScriptObject(asset, TestYaml.DocumentBody(meta));
            Assert.Equal((9, "", 0, 7), asset.Read());
        }
    }
}
