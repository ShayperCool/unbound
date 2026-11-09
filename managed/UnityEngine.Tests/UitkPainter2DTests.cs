using System;
using System.Collections.Generic;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// UI Toolkit Painter2D. Expected values come from the Unity 6000.0.71f1 Editor
// (tests/projects/uitkpainter, Reference/unity-6000.0.71f1/painter.jsonl: detached painters saved
// with SaveToVectorImage, positions relative to the content's bounds). Controls outside the probe's
// exact setup check the same rules with other widths, shapes and fill rules; each documented rule
// (miter limit, fill rule, Fill closing sub-paths) gets a pair of inputs where it changes the result.

namespace UnityEngine.Tests
{
    public class UitkPainter2DTests
    {
        static readonly Color Red = new Color(0.9f, 0.2f, 0.1f, 1f);
        static readonly Color Blue = new Color(0.2f, 0.4f, 0.9f, 1f);

        sealed class Saved
        {
            public VectorImageVertex[] Vertices;
            public ushort[] Indices;
            public Vector2 Size;
        }

        static Saved Save(Action<Painter2D> draw)
        {
            using var p = new Painter2D();
            draw(p);
            var image = ScriptableObject.CreateInstance<VectorImage>();
            Assert.True(p.SaveToVectorImage(image));
            return new Saved { Vertices = image.vertices, Indices = image.indices, Size = image.size };
        }

        static void Near(float expected, float actual, float tolerance = 2e-4f) =>
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected} ± {tolerance}, got {actual}");

        static void NearVertex(Saved s, int i, float x, float y, float cx, float cy, float cz, float cw)
        {
            var v = s.Vertices[i];
            Near(x, v.position.x);
            Near(y, v.position.y);
            Near(cx, v.circle.x);
            Near(cy, v.circle.y);
            Near(cz, v.circle.z);
            Near(cw, v.circle.w);
        }

        static void Zigzag(Painter2D p)
        {
            p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(40, 10)); p.LineTo(new Vector2(48, 62));
        }

        static Action<Painter2D> Stroke(float width, LineJoin join, LineCap cap, Action<Painter2D> path, float miter = -1f) => p =>
        {
            p.lineWidth = width; p.lineJoin = join; p.lineCap = cap; p.strokeColor = Red;
            if (miter >= 0f) p.miterLimit = miter;
            p.BeginPath(); path(p); p.Stroke();
        };

        static Action<Painter2D> Fill(FillRule rule, Action<Painter2D> path) => p =>
        {
            p.fillColor = Blue; p.BeginPath(); path(p); p.Fill(rule);
        };

        static void Rect(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1)); p.ClosePath();
        }

        // ------------------------------------------------------------------ strokes

        // Editor: stroke-line-butt. Center, side A, side B at each end, extruded by half the width
        // plus the 2-unit margin; the sides' circles band the coverage to the line's width.
        [Fact]
        public void StraightStrokeMatchesTheEditor()
        {
            var s = Save(Stroke(8, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 12)); p.LineTo(new Vector2(70, 50)); }));
            Near(64.28041f, s.Size.x);
            Near(44.7585526f, s.Size.y);
            Assert.Equal(new ushort[] { 3, 0, 5, 5, 0, 2, 3, 1, 0, 3, 4, 1 }, s.Indices);
            NearVertex(s, 0, 2.14020824f, 3.37927628f, 0, 0.6f, 0, 3);
            NearVertex(s, 1, -1.07010412f, 8.448191f, 0, 0, 0, 0);
            NearVertex(s, 2, 5.35052f, -1.68963814f, 0, 1.2f, 0, 6);
            NearVertex(s, 5, 65.35052f, 36.31036f, 0, 1.2f, 0, 6);
            Assert.Equal(new Color32(230, 51, 26, 255), s.Vertices[0].tint);
            Assert.Equal(1, s.Vertices[0].flags.b);
        }

        // Control: another width on a vertical line: sides at width/2 + 2, circles (0, (w+4)/(w+2),
        // 0, (w+4)/2) on side B, bounds of the line widened by half the width only.
        [Fact]
        public void StrokeExtrusionFollowsTheWidth()
        {
            var s = Save(Stroke(3, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(20, 10)); p.LineTo(new Vector2(20, 90)); }));
            Assert.Equal(6, s.Vertices.Length);
            Near(3f, s.Size.x);
            Near(80f, s.Size.y);
            float center = s.Vertices[0].position.x;
            Near(3.5f, Math.Abs(s.Vertices[1].position.x - center));
            Near(3.5f, Math.Abs(s.Vertices[2].position.x - center));
            Near(7f / 5f, s.Vertices[2].circle.y);
            Near(3.5f, s.Vertices[2].circle.w);
        }

        // Scripting API: miterLimit limits the ratio of the miter length to the stroke width before
        // the miter becomes a bevel. Same corner, default limit (miter, Editor
        // stroke-miter-limit-default) and limit 2 (bevel, stroke-miter-limit-2).
        [Fact]
        public void MiterLimitTurnsTheSameCornerIntoABevel()
        {
            var miter = Save(Stroke(6, LineJoin.Miter, LineCap.Butt, Zigzag));
            var bevel = Save(Stroke(6, LineJoin.Miter, LineCap.Butt, Zigzag, 2));
            Assert.Equal(18, miter.Vertices.Length);
            Assert.Equal(18, bevel.Vertices.Length);
            NearVertex(miter, 14, 37.4547958f, -5.54019356f, -1.40378571f, -0.8984229f, UaVector.UnusedArc, UaVector.UnusedArc);
            NearVertex(miter, 16, 39.46867f, 7.550003f, 1.64728546f, -0.2534288f, UaVector.UnusedArc, UaVector.UnusedArc);
            NearVertex(bevel, 13, 30.3154564f, -1.07810783f, 0, 1.66666675f, UaVector.UnusedArc, UaVector.UnusedArc);
            NearVertex(bevel, 14, 35.1596222f, -1.37626076f, 0, 2.88754463f, UaVector.UnusedArc, UaVector.UnusedArc);
            Near(60.7664642f, miter.Size.y);
            Near(54.0733337f, bevel.Size.y);
        }

        // Editor: stroke-arc-full-roundcap. A whole circle starts at angle 0 whatever its start angle
        // (90° here) and has no caps; 90° to 450° spans exactly 2π as Mono computes the angles.
        [Fact]
        public void WholeCircleStartsAtZeroWithoutCaps()
        {
            var s = Save(Stroke(6, LineJoin.Miter, LineCap.Round, p => p.Arc(new Vector2(40, 36), 26, 90, 450)));
            Assert.Equal(28, s.Vertices.Length);
            Assert.Equal(78, s.Indices.Length);
            Near(58f, s.Size.x);
            NearVertex(s, 0, 62.6f, 29f, 1.1586207f, 0f, 1.4608696f, 0f);
            Assert.Equal(6.2831855f, Angle.Degrees(450).ToRadians() - Angle.Degrees(90).ToRadians());
        }

        // Editor: stroke-quadratic-closed. ClosePath returns to the point the sub-path's first
        // segment recorded; a QuadraticCurveTo records the origin, so the closing line ends there.
        [Fact]
        public void ClosedQuadraticPathClosesToTheOrigin()
        {
            var open = Save(Stroke(3, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.QuadraticCurveTo(new Vector2(40, -20), new Vector2(72, 60)); }));
            var closed = Save(Stroke(3, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.QuadraticCurveTo(new Vector2(40, -20), new Vector2(72, 60)); p.ClosePath(); }));
            Assert.Equal(50, closed.Vertices.Length);
            Near(76.49903f, closed.Size.x);
            Near(66.05386f, closed.Size.y);
            Assert.True(open.Size.y < 50f);
            var cubic = Save(Stroke(3, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.BezierCurveTo(new Vector2(29, 7), new Vector2(51, 7), new Vector2(72, 60)); p.ClosePath(); }));
            Assert.True(cubic.Size.y < 60f, $"a cubic closes to its start, got {cubic.Size}");
        }

        // ------------------------------------------------------------------ fills

        // Editor: fill-triangle and fill-open. Fill closes every sub-path, but only a drawn closing
        // side is anti-aliased: the closed triangle has three faded edges (21 vertices), the open one
        // two (14).
        [Fact]
        public void OnlyDrawnSidesAreAntiAliased()
        {
            var closed = Save(Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(10, 60)); p.LineTo(new Vector2(40, 8)); p.LineTo(new Vector2(70, 60)); p.ClosePath(); }));
            var open = Save(Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(10, 60)); p.LineTo(new Vector2(40, 8)); p.LineTo(new Vector2(70, 60)); }));
            Assert.Equal(21, closed.Vertices.Length);
            Assert.Equal(14, open.Vertices.Length);
            NearVertex(closed, 2, 30f, 34.6666679f, 0, 0, UaVector.UnusedArc, UaVector.UnusedArc);
            NearVertex(closed, 5, 60f, 54f, 1, 2, UaVector.UnusedArc, UaVector.UnusedArc);
        }

        // Scripting API: the fill rule decides what nested sub-paths cover. Same-winding square
        // inside a square: NonZero fills the hole (84 vertices), OddEven leaves it (56).
        [Fact]
        public void FillRuleDecidesTheHole()
        {
            Action<Painter2D> holes = p => { Rect(p, 8, 8, 72, 64); Rect(p, 24, 20, 56, 52); };
            Assert.Equal(84, Save(Fill(FillRule.NonZero, holes)).Vertices.Length);
            Assert.Equal(56, Save(Fill(FillRule.OddEven, holes)).Vertices.Length);
        }

        // Editor: fill-star-nonzero, fill-bowtie. Triangles with two corners at the same boundary
        // point (every triangle at a pentagram's crossings) are dropped; a bowtie keeps its two
        // triangles, anti-aliased on their drawn sides only.
        [Fact]
        public void SelfIntersectingFillsDropTrianglesAtCrossings()
        {
            Action<Painter2D> star = p =>
            {
                p.MoveTo(new Vector2(40, 6)); p.LineTo(new Vector2(57.625f, 60.25f)); p.LineTo(new Vector2(11.5f, 26.75f));
                p.LineTo(new Vector2(68.5f, 26.75f)); p.LineTo(new Vector2(22.375f, 60.25f)); p.ClosePath();
            };
            Assert.Empty(Save(Fill(FillRule.NonZero, star)).Vertices);
            Assert.Empty(Save(Fill(FillRule.OddEven, star)).Vertices);
            var bowtie = Save(Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(8, 8)); p.LineTo(new Vector2(72, 64)); p.LineTo(new Vector2(72, 8)); p.LineTo(new Vector2(8, 64)); p.ClosePath(); }));
            Assert.Equal(14, bowtie.Vertices.Length);
            NearVertex(bowtie, 2, 32f, 28f, 0, 0, UaVector.UnusedArc, UaVector.UnusedArc);
        }

        // Editor: fill-circle. A circle fills 8 arc pieces of 45°: plain interior triangles (no arc
        // flag, zero circle) and, beyond each chord, a quad covered inside the circle (circle =
        // offset / radius).
        [Fact]
        public void CircleFillsAreChordsPlusArcQuads()
        {
            var s = Save(Fill(FillRule.NonZero, p => p.Arc(new Vector2(40, 36), 26, 0, 360)));
            Near(52f, s.Size.x);
            Assert.Equal(50, s.Vertices.Length);
            Assert.Equal(16 * 2, Array.FindAll(s.Vertices, v => v.flags.b == 1).Length);
            Assert.Contains(s.Vertices, v => v.flags.b == 0 && v.circle == Vector4.zero);
            foreach (var v in s.Vertices)
            {
                if (v.flags.b == 0) continue;
                var offset = new Vector2(v.position.x - 26f, v.position.y - 26f) / 26f;
                Near(offset.x, v.circle.x, 1e-4f);
                Near(offset.y, v.circle.y, 1e-4f);
            }
        }

        // ------------------------------------------------------------------ the painter's API

        sealed class Logs : ILogHandler
        {
            public readonly List<string> Lines = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        // Editor: detached-without-beginpath. Drawing before BeginPath draws nothing; the report
        // reaches the log callbacks, not the game's ILogHandler.
        [Fact]
        public void DrawingBeforeBeginPathIsReportedNatively()
        {
            var handler = Debug.unityLogger.logHandler;
            var logs = new Logs();
            var received = new List<string>();
            Application.LogCallback callback = (message, trace, type) => received.Add(message);
            Debug.unityLogger.logHandler = logs;
            Application.logMessageReceived += callback;
            try
            {
                var s = Save(p => { p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(10, 10)); p.Stroke(); p.Fill(); });
                Assert.Empty(s.Vertices);
                Assert.True(float.IsNegativeInfinity(s.Size.x));
            }
            finally
            {
                Application.logMessageReceived -= callback;
                Debug.unityLogger.logHandler = handler;
            }
            Assert.Empty(logs.Lines);
            Assert.Equal(new[] { "Calling MoveTo() before BeginPath()", "Calling LineTo() before BeginPath()", "Calling Stroke() before BeginPath()" }, received);
        }

        // Editor: detached-clear-resets. Clear empties the content and resets the state and the path
        // (BeginPath is needed again).
        [Fact]
        public void ClearResetsTheDetachedPainter()
        {
            using var p = new Painter2D();
            p.lineWidth = 5; p.lineJoin = LineJoin.Round; p.miterLimit = 3;
            p.BeginPath(); p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(10, 10)); p.Stroke();
            p.Clear();
            Assert.Equal(1f, p.lineWidth);
            Assert.Equal(LineJoin.Miter, p.lineJoin);
            Assert.Equal(10f, p.miterLimit);
            Assert.Equal(Color.black, p.strokeColor);
            var image = ScriptableObject.CreateInstance<VectorImage>();
            p.LineTo(new Vector2(20, 20));
            p.Stroke();
            p.SaveToVectorImage(image);
            Assert.Empty(image.vertices);
        }

        static UaPainter Paint(Action<MeshGenerationContext> a, Action<MeshGenerationContext> b)
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var root = panel.visualTree;
            root.style.width = 200;
            root.style.height = 200;
            foreach (var draw in new[] { a, b })
            {
                var element = new VisualElement();
                element.style.position = Position.Absolute;
                element.style.width = 100;
                element.style.height = 100;
                element.generateVisualContent = draw;
                root.Add(element);
            }
            panel.ApplyStyles();
            panel.ValidateLayout();
            var painter = new UaPainter();
            painter.Paint(panel, 200f);
            return painter;
        }

        // Editor: attached-0..2, attached-outside-callback, attached-save-and-clear. The context's
        // painter starts each element reset; outside a callback it refuses (with Unity's message);
        // Clear and SaveToVectorImage belong to detached painters.
        [Fact]
        public void TheContextPainterIsResetPerElementAndOnlyDrawsInCallbacks()
        {
            float widthInSecond = 0f;
            Painter2D captured = null;
            var painter = Paint(mgc =>
            {
                var p = mgc.painter2D;
                captured = p;
                p.lineWidth = 7;
                p.BeginPath(); p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(50, 0)); p.Stroke();
            }, mgc =>
            {
                widthInSecond = mgc.painter2D.lineWidth;
                mgc.painter2D.MoveTo(new Vector2(0, 0));
            });
            Assert.Equal(1f, widthInSecond);
            // The stroke reached the panel with its arc data (flags.z = 1/255, circles in uv6).
            Assert.Contains(painter.Uvs[3], f => f.z > 0f);
            var handler = Debug.unityLogger.logHandler;
            var logs = new Logs();
            Debug.unityLogger.logHandler = logs;
            try
            {
                captured.BeginPath();
                captured.Clear();
                Assert.False(captured.SaveToVectorImage(ScriptableObject.CreateInstance<VectorImage>()));
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
            Assert.Equal(new[]
            {
                "Cannot issue vector graphics commands outside of generateVisualContent callback",
                "Clear() cannot be called on a Painter2D associated with a MeshGenerationContext. You should create your own instance of Painter2D instead.",
                "SaveToVectorImage cannot be called on a Painter2D associated with a MeshGenerationContext. You should create your own instance of Painter2D instead.",
            }, logs.Lines);
        }

        [Fact]
        public void PropertiesKeepTheirValues()
        {
            using var p = new Painter2D();
            p.lineWidth = 3.5f;
            p.strokeColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            p.fillColor = new Color(0.5f, 0.6f, 0.7f, 0.8f);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.miterLimit = 4f;
            Assert.Equal(3.5f, p.lineWidth);
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), p.strokeColor);
            Assert.Equal(new Color(0.5f, 0.6f, 0.7f, 0.8f), p.fillColor);
            Assert.Equal(LineJoin.Round, p.lineJoin);
            Assert.Equal(LineCap.Round, p.lineCap);
            Assert.Equal(4f, p.miterLimit);
        }

        // Tessellating the same path again allocates nothing once the buffers are warm.
        [Fact]
        public void RepeatedTessellationDoesNotAllocate()
        {
            var path = new UaVectorPath();
            path.MoveTo(new Vector2(8, 60));
            path.BezierCurveTo(new Vector2(20, -10), new Vector2(60, 90), new Vector2(72, 10));
            path.LineTo(new Vector2(72, 60));
            path.Arc(new Vector2(40, 60), 20, 0, MathF.PI, ArcDirection.Clockwise);
            path.ClosePath();
            var style = new UaVectorStyle { LineWidth = 4, StrokeColor = Red, FillColor = Blue, Join = LineJoin.Round, Cap = LineCap.Round, MiterLimit = 10 };
            var stroke = new UaVectorStroke();
            var fill = new UaVectorFill();
            var mesh = new UaVectorMesh();
            for (int i = 0; i < 3; i++)
            {
                mesh.Clear();
                stroke.Stroke(path, style, mesh);
                fill.Fill(path, Blue, FillRule.NonZero, mesh);
            }
            Assert.True(mesh.VertexCount > 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            mesh.Clear();
            stroke.Stroke(path, style, mesh);
            fill.Fill(path, Blue, FillRule.NonZero, mesh);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
