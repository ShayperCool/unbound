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

        // Editor: stroke-arcto-radius-zero, fill-arcto-radius-zero. An arc-to of radius 0 draws no
        // arc and leaves the segments around it unjoined; the fill is the square of its lines.
        [Fact]
        public void ArcToOfRadiusZeroDrawsNoArc()
        {
            Action<Painter2D> path = p => { p.MoveTo(new Vector2(10, 10)); p.ArcTo(new Vector2(60, 10), new Vector2(60, 60), 0); p.LineTo(new Vector2(60, 60)); p.LineTo(new Vector2(10, 60)); };
            var stroke = Save(Stroke(4, LineJoin.Miter, LineCap.Butt, path));
            Assert.Equal(24, stroke.Vertices.Length);
            Assert.All(stroke.Vertices, v => Assert.True(float.IsFinite(v.position.x) && float.IsFinite(v.circle.x)));
            var fill = Save(Fill(FillRule.NonZero, p => { path(p); p.ClosePath(); }));
            Assert.Equal(28, fill.Vertices.Length);
            Near(50f, fill.Size.x);
        }

        // Editor: fill-plus. Two crossing rectangles: the triangles at their crossings are dropped
        // too (28 vertices: the four arms' faded edges).
        [Fact]
        public void CrossingSubPathsDropTheirCrossingTriangles()
        {
            var s = Save(Fill(FillRule.NonZero, p => { Rect(p, 10, 26, 70, 46); Rect(p, 30, 6, 50, 66); }));
            Assert.Equal(28, s.Vertices.Length);
            Near(60f, s.Size.x);
        }

        // Control: two combs of 100 teeth crossing each other (40000 crossings) fill in about a
        // second, most of it libtess2's; matching each crossing against every vertex took ten.
        [Fact]
        public void FillsWithManyCrossingsStayFast()
        {
            const int Teeth = 100;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var s = Save(Fill(FillRule.OddEven, p =>
            {
                p.MoveTo(new Vector2(0, 0));
                for (int i = 0; i < Teeth; i++)
                {
                    p.LineTo(new Vector2(i * 4 + 1, 0)); p.LineTo(new Vector2(i * 4 + 1, 900)); p.LineTo(new Vector2(i * 4 + 3, 900)); p.LineTo(new Vector2(i * 4 + 3, 0));
                }
                p.ClosePath();
                p.MoveTo(new Vector2(-10, -10));
                for (int i = 0; i < Teeth; i++)
                {
                    p.LineTo(new Vector2(-10, i * 4 + 1)); p.LineTo(new Vector2(900, i * 4 + 1)); p.LineTo(new Vector2(900, i * 4 + 3)); p.LineTo(new Vector2(-10, i * 4 + 3));
                }
                p.ClosePath();
            }));
            Assert.True(watch.Elapsed.TotalSeconds < 5, $"took {watch.Elapsed}");
            Assert.NotEmpty(s.Vertices);
        }

        // Control: a short real arc (0.1°, ends 0.07 apart) draws no ring but still gets its round
        // caps, along its chord.
        [Fact]
        public void TinyArcsStillCap()
        {
            var s = Save(Stroke(6, LineJoin.Miter, LineCap.Round, p => p.Arc(new Vector2(40, 40), 40, -90, -89.9f)));
            Assert.Equal(10, s.Vertices.Length);
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

        // Editor: allocate-stroke-order (panel image). A callback's meshes are drawn in call order:
        // an allocated quad, a painter stroke, another allocated quad.
        // ---- stroke gradients (tests/projects/uitkpainter, gradient cases) ----

        static Gradient TwoKeys(Color from, Color to)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static readonly Color Black = new Color32(0, 0, 0, 255), White = new Color32(240, 240, 240, 255);

        static Saved GradientLine(Gradient g, float length = 80f, Action<Painter2D> before = null) => Save(p =>
        {
            p.lineWidth = 4; p.strokeColor = Red; p.strokeGradient = g;
            before?.Invoke(p);
            p.BeginPath(); p.MoveTo(new Vector2(0, 10)); p.LineTo(new Vector2(length, 10)); p.Stroke();
        });

        // Documented: a gradient overrides strokeColor, null falls back on it. A horizontal line
        // of another length than the probe's: rows of three vertices at every eighth.
        [Fact]
        public void AGradientColoursAStraightStrokeInEighths()
        {
            var s = GradientLine(TwoKeys(Black, White));
            Assert.Equal(27, s.Vertices.Length);
            Assert.Equal(96, s.Indices.Length);
            for (int row = 0; row < 9; row++)
            {
                for (int k = 0; k < 3; k++) Assert.Equal((byte)(30 * row), s.Vertices[row * 3 + k].tint.r);
                // Vertices are saved relative to the bounds, which start at the line's start.
                Near(row * 10f, s.Vertices[row * 3].position.x);
            }
            var solid = GradientLine(TwoKeys(Black, White), before: p => p.strokeGradient = null);
            Assert.Equal(6, solid.Vertices.Length);
            Assert.Equal(UaVector.Tint(Red), solid.Vertices[0].tint);
        }

        [Fact]
        public void TheLastOfColourAndGradientSetIsUsed()
        {
            var s = GradientLine(TwoKeys(Black, White), before: p => p.strokeColor = Blue);
            Assert.Equal(6, s.Vertices.Length);
            Assert.Equal(UaVector.Tint(Blue), s.Vertices[0].tint);
            // A transparent stroke colour draws nothing, also under a gradient.
            using var painter = new Painter2D();
            painter.strokeColor = new Color(1, 0, 0, 0);
            painter.strokeGradient = TwoKeys(Black, White);
            painter.lineWidth = 4;
            painter.BeginPath(); painter.MoveTo(Vector2.zero); painter.LineTo(new Vector2(10, 0)); painter.Stroke();
            var image = ScriptableObject.CreateInstance<VectorImage>();
            painter.SaveToVectorImage(image);
            Assert.True(image.vertices == null || image.vertices.Length == 0);
        }

        [Fact]
        public void TheGradientIsTakenWhenSetAndReadBackAsACopy()
        {
            using var p = new Painter2D();
            Assert.Equal(Color.white, p.strokeGradient.colorKeys[0].color);
            var g = TwoKeys(Black, White);
            p.strokeGradient = g;
            g.SetKeys(new[] { new GradientColorKey(Red, 0.5f) }, new[] { new GradientAlphaKey(1f, 0f) });
            var back = p.strokeGradient;
            Assert.NotSame(g, back);
            Assert.Equal(2, back.colorKeys.Length);
            // Null and Clear turn the gradient off; the getter still answers the last one.
            p.strokeGradient = null;
            Assert.Equal(2, p.strokeGradient.colorKeys.Length);
            p.strokeGradient = g;
            p.Clear();
            Assert.Single(p.strokeGradient.colorKeys);
            p.lineWidth = 4; p.strokeColor = Blue;
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(10, 0)); p.Stroke();
            var image = ScriptableObject.CreateInstance<VectorImage>();
            p.SaveToVectorImage(image);
            Assert.Equal(6, image.vertices.Length);
        }

        [Fact]
        public void RowsFallOnGradientKeysAndFixedKeysGiveFlatSteps()
        {
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(new[] { new GradientColorKey(Black, 0.3f), new GradientColorKey(White, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var s = GradientLine(g, 100f);
            // Rows at 0, 12.5, 25, then the key at 30 (its 16-bit time), and on in eighths from there.
            Near(25f, s.Vertices[6].position.x);
            Near(100f * (19661f / 65535f), s.Vertices[9].position.x, 1e-3f);
            Near(100f * (19661f / 65535f) + 12.5f, s.Vertices[12].position.x, 1e-3f);
            Assert.Equal(0, s.Vertices[6].tint.r);
            Assert.Equal(240, s.Vertices[12].tint.r);
        }

        // The gradient runs over the whole path: a second sub-path continues where the first ended,
        // and joins and caps take the colour of the segment end they sit on.
        [Fact]
        public void TheGradientRunsAlongAllSubPaths()
        {
            var s = Save(p =>
            {
                p.lineWidth = 4; p.lineCap = LineCap.Round; p.strokeGradient = TwoKeys(Black, White);
                p.BeginPath();
                p.MoveTo(new Vector2(0, 10)); p.LineTo(new Vector2(40, 10));
                p.MoveTo(new Vector2(0, 30)); p.LineTo(new Vector2(40, 30));
                p.Stroke();
            });
            // Per sub-path: 9 rows, then the start cap and the end cap (5 vertices each).
            Assert.Equal(2 * (27 + 10), s.Vertices.Length);
            Assert.Equal(0, s.Vertices[0].tint.r);
            Assert.Equal(120, s.Vertices[24].tint.r);
            Assert.Equal(0, s.Vertices[27].tint.r);   // first sub-path's start cap
            Assert.Equal(120, s.Vertices[32].tint.r); // its end cap
            Assert.Equal(120, s.Vertices[37].tint.r); // the second sub-path starts at half
            Assert.Equal(240, s.Vertices[37 + 24].tint.r);
        }

        [Fact]
        public void ArcsAndCurvesChangeColourAlongTheirLength()
        {
            var arc = Save(p =>
            {
                p.lineWidth = 4; p.strokeGradient = TwoKeys(Black, White);
                p.BeginPath(); p.Arc(new Vector2(50, 50), 40, 0, 180); p.Stroke();
            });
            // Ring steps of at most 30°: the colour grows from step to step by the angle turned,
            // the same on both sides of the ring, from the first key to the last.
            Assert.Equal(0, arc.Vertices[0].tint.r);
            Assert.InRange(arc.Vertices[2].tint.r, 39, 41);
            for (int i = 2; i < arc.Vertices.Length; i += 2)
            {
                Assert.True(arc.Vertices[i].tint.r >= arc.Vertices[i - 2].tint.r);
                Assert.InRange(arc.Vertices[i + 1].tint.r, arc.Vertices[i].tint.r - 1, arc.Vertices[i].tint.r + 1);
            }
            Assert.InRange(arc.Vertices[arc.Vertices.Length - 1].tint.r, 239, 240);
            var curve = Save(p =>
            {
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Black, 0f), new GradientColorKey(White, 0.5f), new GradientColorKey(Black, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                p.lineWidth = 4; p.strokeGradient = g;
                p.BeginPath(); p.MoveTo(new Vector2(0, 40)); p.BezierCurveTo(new Vector2(20, 0), new Vector2(60, 0), new Vector2(80, 40)); p.Stroke();
            });
            // The pieces are cut at the middle key: some piece end shows its colour in full, and
            // the curve starts and ends black.
            byte max = 0;
            foreach (var v in curve.Vertices) max = Math.Max(max, v.tint.r);
            Assert.InRange(max, 239, 240);
            Assert.Equal(0, curve.Vertices[0].tint.r);
            Assert.InRange(curve.Vertices[curve.Vertices.Length - 2].tint.r, 0, 1);
        }

        [Fact]
        public void ALinearGradientIsShownInSrgb()
        {
            var g = TwoKeys(Black, new Color32(128, 128, 128, 255));
            g.colorSpace = ColorSpace.Linear;
            var s = GradientLine(g);
            // The end key 128/255 read as linear: 0.50196 -> sRGB 0.7354 -> 188.
            Assert.Equal(188, s.Vertices[24].tint.r);
            g.colorSpace = ColorSpace.Gamma;
            Assert.Equal(128, GradientLine(g).Vertices[24].tint.r);
        }

        [Fact]
        public void CallbackMeshesKeepCallOrder()
        {
            var painter = Paint(mgc =>
            {
                Quad(mgc, new Color32(40, 160, 60, 255), 0f);
                var p = mgc.painter2D;
                p.strokeColor = Red; p.BeginPath(); p.MoveTo(new Vector2(0, 15)); p.LineTo(new Vector2(80, 15)); p.Stroke();
                Quad(mgc, new Color32(40, 60, 200, 255), 50f);
            }, mgc => { });
            var colors = painter.Colors;
            int green = colors.FindIndex(c => c.Equals(new Color32(40, 160, 60, 255)));
            int red = colors.FindIndex(c => c.Equals((Color32)Red));
            int blue = colors.FindIndex(c => c.Equals(new Color32(40, 60, 200, 255)));
            Assert.True(green >= 0 && red > green && blue > red, $"green {green}, red {red}, blue {blue}");
        }

        static void Quad(MeshGenerationContext ctx, Color32 c, float x)
        {
            var mesh = ctx.Allocate(4, 6);
            mesh.SetNextVertex(new Vertex { position = new Vector3(x, 0, Vertex.nearZ), tint = c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x + 20, 0, Vertex.nearZ), tint = c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x + 20, 30, Vertex.nearZ), tint = c });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x, 30, Vertex.nearZ), tint = c });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
        }

        // DrawMesh joins the same order: a painter stroke, then a drawn quad over it.
        [Fact]
        public void DrawMeshKeepsCallOrder()
        {
            var vertices = new Unity.Collections.NativeArray<Vertex>(4, Unity.Collections.Allocator.Temp);
            var indices = new Unity.Collections.NativeArray<ushort>(6, Unity.Collections.Allocator.Temp);
            var blue = new Color32(40, 60, 200, 255);
            vertices[0] = new Vertex { position = new Vector3(50, 0, Vertex.nearZ), tint = blue };
            vertices[1] = new Vertex { position = new Vector3(70, 0, Vertex.nearZ), tint = blue };
            vertices[2] = new Vertex { position = new Vector3(70, 30, Vertex.nearZ), tint = blue };
            vertices[3] = new Vertex { position = new Vector3(50, 30, Vertex.nearZ), tint = blue };
            ushort[] quad = { 0, 1, 2, 0, 2, 3 };
            for (int i = 0; i < 6; i++) indices[i] = quad[i];
            var painter = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.strokeColor = Red; p.BeginPath(); p.MoveTo(new Vector2(0, 15)); p.LineTo(new Vector2(80, 15)); p.Stroke();
                mgc.DrawMesh(vertices, indices);
            }, mgc => { });
            vertices.Dispose();
            indices.Dispose();
            int red = painter.Colors.FindIndex(c => c.Equals((Color32)Red));
            int drawn = painter.Colors.FindIndex(c => c.Equals(blue));
            Assert.True(red >= 0 && drawn > red, $"red {red}, drawn {drawn}");
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
