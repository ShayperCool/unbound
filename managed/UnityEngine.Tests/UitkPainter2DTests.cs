using System;
using System.Collections.Generic;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// UI Toolkit Painter2D through MeshGenerationContext.painter2D. Each test names its source:
//   [doc]    the Unity 6000.0 Scripting API text of Painter2D, its members, FillRule, LineJoin,
//            LineCap, ArcDirection and MeshGenerationContext.painter2D (installed doc XML of
//            6000.0.71f1), applied as plain geometry (a filled shape covers its area, a stroke of
//            width w reaches w/2 from its path, ClosePath adds a straight line, the two fill rules);
//   [choice] Unbound's choice where those pages say nothing: flattening tolerance (0.05 local
//            units), join and cap geometry (HTML canvas shapes), "clockwise" as clockwise on screen
//            (angles grow clockwise in UI Toolkit's y-down space). These check Unbound's
//            implementation, not Unity behaviour.
// Only geometry is asserted (covered area, bounds, point coverage, vertex colours), never pixels.
// No reference capture covers Painter2D; nothing here is a measured Unity value.
// Red base: 85fcc53e, where Painter2D and MeshGenerationContext.painter2D are generated stubs: the
// getter answers null, so each drawing callback throws NullReferenceException (caught and logged
// by the panel painter) and the element draws nothing; the first assertion that geometry exists
// fails. Detached painter properties answer their defaults there. The file uses only members that
// exist on that base, so it is expected to compile there (unexecuted).

namespace UnityEngine.Tests
{
    public class UitkPainter2DTests
    {
        // Painter2D.UaFlatteningTolerance (Unbound's choice), restated so the file compiles on the
        // red base.
        const float Tolerance = 0.05f;

        sealed class Drawn
        {
            public readonly List<Vector2> Positions = new List<Vector2>();
            public readonly List<Color32> Colors = new List<Color32>();
            public readonly List<int> Indices = new List<int>();
            public int RetainedContentIndices;

            public float Area()
            {
                double area = 0;
                for (int i = 0; i + 2 < Indices.Count; i += 3)
                {
                    var a = Positions[Indices[i]];
                    var b = Positions[Indices[i + 1]];
                    var c = Positions[Indices[i + 2]];
                    area += Math.Abs((b.x - a.x) * (double)(c.y - a.y) - (c.x - a.x) * (double)(b.y - a.y)) * 0.5;
                }
                return (float)area;
            }

            public Rect Bounds()
            {
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (int i in Indices)
                {
                    var p = Positions[i];
                    x0 = Math.Min(x0, p.x);
                    y0 = Math.Min(y0, p.y);
                    x1 = Math.Max(x1, p.x);
                    y1 = Math.Max(y1, p.y);
                }
                return Rect.MinMaxRect(x0, y0, x1, y1);
            }

            public bool Covers(Vector2 p)
            {
                for (int i = 0; i + 2 < Indices.Count; i += 3)
                {
                    var a = Positions[Indices[i]];
                    var b = Positions[Indices[i + 1]];
                    var c = Positions[Indices[i + 2]];
                    float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
                    bool neg = d1 < -1e-4f || d2 < -1e-4f || d3 < -1e-4f;
                    bool pos = d1 > 1e-4f || d2 > 1e-4f || d3 > 1e-4f;
                    if (!(neg && pos)) return true;
                }
                return false;
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Paints a 400×400 panel holding one absolutely positioned element whose
        /// generateVisualContent is <paramref name="draw"/>, and returns the panel's geometry (in
        /// panel space; the element's local space when it sits at the origin).</summary>
        static Drawn Paint(Action<MeshGenerationContext> draw, float left = 0f, float top = 0f, float width = 200f, float height = 200f)
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var root = panel.visualTree;
            root.style.width = 400;
            root.style.height = 400;
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.left = left;
            element.style.top = top;
            element.style.width = width;
            element.style.height = height;
            element.generateVisualContent = draw;
            root.Add(element);
            panel.ApplyStyles();
            panel.ValidateLayout();
            var painter = new UaPainter();
            painter.Paint(panel, 400f);
            var drawn = new Drawn();
            foreach (var p in painter.Positions) drawn.Positions.Add(new Vector2(p.x, p.y));
            drawn.Colors.AddRange(painter.Colors);
            drawn.Indices.AddRange(painter.Indices);
            drawn.RetainedContentIndices = element.uaGeometry != null ? element.uaGeometry.Content.Indices.Count : 0;
            return drawn;
        }

        static void Near(float expected, float actual, float tolerance) =>
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected} ± {tolerance}, got {actual}");

        static void Within(float min, float actual, float max) =>
            Assert.True(actual >= min && actual <= max, $"expected {actual} in [{min}, {max}]");

        static void AssertBounds(Rect expected, Rect actual, float tolerance)
        {
            Near(expected.xMin, actual.xMin, tolerance);
            Near(expected.yMin, actual.yMin, tolerance);
            Near(expected.xMax, actual.xMax, tolerance);
            Near(expected.yMax, actual.yMax, tolerance);
        }

        static void Polygon(Painter2D p, params Vector2[] points)
        {
            p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath();
        }

        // ------------------------------------------------------------------ fills

        // [doc] Fill fills the currently defined path: a closed triangle covers its area and no more.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Fact]
        public void FilledTriangleCoversItsAreaWithinItsVertexBounds()
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.fillColor = Color.white;
                p.BeginPath();
                Polygon(p, new Vector2(10, 10), new Vector2(110, 10), new Vector2(10, 110));
                p.Fill();
            });
            Assert.NotEmpty(d.Indices);
            Near(5000f, d.Area(), 0.05f);
            AssertBounds(Rect.MinMaxRect(10, 10, 110, 110), d.Bounds(), 1e-3f);
            Assert.True(d.Covers(new Vector2(30, 30)));
            Assert.False(d.Covers(new Vector2(80, 80)));
        }

        // [doc] A filled rectangle covers exactly its rectangle.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Fact]
        public void FilledRectangleCoversItsAreaWithinItsBounds()
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                Polygon(p, new Vector2(20, 30), new Vector2(120, 30), new Vector2(120, 80), new Vector2(20, 80));
                p.Fill(FillRule.NonZero);
            });
            Assert.NotEmpty(d.Indices);
            Near(5000f, d.Area(), 0.05f);
            AssertBounds(Rect.MinMaxRect(20, 30, 120, 80), d.Bounds(), 1e-3f);
            Assert.True(d.Covers(new Vector2(70, 55)));
            Assert.False(d.Covers(new Vector2(130, 55)));
        }

        // [doc] FillRule: "non-zero" and "odd-even" winding rules. A pentagram drawn as one
        // self-intersecting sub-path winds twice around its inner pentagon: non-zero fills it,
        // odd-even leaves it empty; both fill the points.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Fact]
        public void SelfIntersectingStarFillsItsCenterOnlyWithNonZero()
        {
            var center = new Vector2(100, 100);
            const float R = 80f;
            var star = new Vector2[5];
            for (int k = 0; k < 5; k++)
            {
                float a = (-90f + 144f * k) * Mathf.Deg2Rad;
                star[k] = center + R * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            Drawn Star(FillRule rule) => Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                Polygon(p, star);
                p.Fill(rule);
            });
            var nonZero = Star(FillRule.NonZero);
            var oddEven = Star(FillRule.OddEven);
            Assert.NotEmpty(nonZero.Indices);
            Assert.NotEmpty(oddEven.Indices);
            Assert.True(nonZero.Covers(center));
            Assert.False(oddEven.Covers(center));
            var tip = new Vector2(100, 40);
            Assert.True(nonZero.Covers(tip));
            Assert.True(oddEven.Covers(tip));
            // The difference is the inner pentagon: radius R·cos72°/cos36°, area 5/2·r²·sin72°.
            float r = R * Mathf.Cos(72f * Mathf.Deg2Rad) / Mathf.Cos(36f * Mathf.Deg2Rad);
            float pentagon = 2.5f * r * r * Mathf.Sin(72f * Mathf.Deg2Rad);
            Near(pentagon, nonZero.Area() - oddEven.Area(), 0.5f);
        }

        // [doc] The fill rules over two sub-paths: an inner square drawn the other way round cuts a
        // hole with non-zero; drawn the same way it is filled with non-zero and a hole with odd-even.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Fact]
        public void NestedSubPathsFollowTheFillRule()
        {
            Drawn Squares(bool innerReversed, FillRule rule) => Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                Polygon(p, new Vector2(20, 20), new Vector2(180, 20), new Vector2(180, 180), new Vector2(20, 180));
                if (innerReversed) Polygon(p, new Vector2(60, 60), new Vector2(60, 140), new Vector2(140, 140), new Vector2(140, 60));
                else Polygon(p, new Vector2(60, 60), new Vector2(140, 60), new Vector2(140, 140), new Vector2(60, 140));
                p.Fill(rule);
            });
            var reversedNonZero = Squares(true, FillRule.NonZero);
            var sameNonZero = Squares(false, FillRule.NonZero);
            var sameOddEven = Squares(false, FillRule.OddEven);
            Assert.NotEmpty(reversedNonZero.Indices);
            Near(19200f, reversedNonZero.Area(), 0.1f);
            Near(25600f, sameNonZero.Area(), 0.1f);
            Near(19200f, sameOddEven.Area(), 0.1f);
            Assert.False(reversedNonZero.Covers(new Vector2(100, 100)));
            Assert.True(sameNonZero.Covers(new Vector2(100, 100)));
            Assert.False(sameOddEven.Covers(new Vector2(100, 100)));
        }

        // ------------------------------------------------------------------ strokes

        // [doc] lineWidth is the width of stroked paths; LineCap.Butt "terminates the path with no
        // tip": a butt line reaches w/2 to each side and stops at its ends.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Theory]
        [InlineData(2f)]
        [InlineData(10f)]
        public void StrokeWidthSetsTheBoundsOfAButtLine(float width)
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.lineWidth = width;
                p.lineCap = LineCap.Butt;
                p.BeginPath();
                p.MoveTo(new Vector2(20, 100));
                p.LineTo(new Vector2(180, 100));
                p.Stroke();
            });
            Assert.NotEmpty(d.Indices);
            AssertBounds(Rect.MinMaxRect(20, 100 - width / 2, 180, 100 + width / 2), d.Bounds(), 1e-3f);
            Near(160f * width, d.Area(), 0.05f);
        }

        // [doc] LineCap.Round "terminates the path with a round tip": the line reaches w/2 past its
        // ends. [choice] The tip is a half disk, as a regular polygon inscribed in it within the
        // flattening tolerance.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Fact]
        public void RoundCapsExtendTheLineByHalfTheWidth()
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.lineWidth = 10f;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.MoveTo(new Vector2(20, 100));
                p.LineTo(new Vector2(180, 100));
                p.Stroke();
            });
            Assert.NotEmpty(d.Indices);
            AssertBounds(Rect.MinMaxRect(15, 95, 185, 105), d.Bounds(), 1e-3f);
            float exact = 1600f + Mathf.PI * 25f;
            Within(exact - 2f * Mathf.PI * 5f * Tolerance - 0.05f, d.Area(), exact + 0.05f);
        }

        // [doc] LineJoin: Miter joins with a sharp corner, Bevel with a beveled corner, Round with a
        // round corner. [choice] Their shapes (HTML canvas): for an L of width 10 the two segment
        // rectangles cover 775, the miter adds the 5×5 outer square, the bevel half of it, the round
        // join a quarter disk. Each corner is drawn once (no overlap counted twice).
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Theory]
        [InlineData(LineJoin.Miter)]
        [InlineData(LineJoin.Bevel)]
        [InlineData(LineJoin.Round)]
        public void LineJoinsShapeTheOuterCorner(LineJoin join)
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.lineWidth = 10f;
                p.lineJoin = join;
                p.BeginPath();
                p.MoveTo(new Vector2(10, 10));
                p.LineTo(new Vector2(50, 10));
                p.LineTo(new Vector2(50, 50));
                p.Stroke();
            });
            Assert.NotEmpty(d.Indices);
            AssertBounds(Rect.MinMaxRect(10, 5, 55, 50), d.Bounds(), 1e-3f);
            switch (join)
            {
                case LineJoin.Miter:
                    Near(800f, d.Area(), 0.05f);
                    Assert.True(d.Covers(new Vector2(54.5f, 5.5f)));
                    break;
                case LineJoin.Bevel:
                    Near(787.5f, d.Area(), 0.05f);
                    Assert.False(d.Covers(new Vector2(54.5f, 5.5f)));
                    Assert.True(d.Covers(new Vector2(51f, 9f)));
                    break;
                default:
                    float exact = 775f + Mathf.PI * 25f / 4f;
                    Within(exact - Mathf.PI / 2f * 5f * Tolerance - 0.05f, d.Area(), exact + 0.05f);
                    Assert.False(d.Covers(new Vector2(54.5f, 5.5f)));
                    Assert.True(d.Covers(new Vector2(53f, 7f)));
                    break;
            }
        }

        // [doc] miterLimit: "the limit on the ratio of the miter length to the stroke width before
        // converting the miter to a bevel". A right angle's ratio is √2: a limit of 1.2 bevels it,
        // 1.5 keeps the miter.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Theory]
        [InlineData(1.2f, 787.5f)]
        [InlineData(1.5f, 800f)]
        public void MiterLimitTurnsAMiterIntoABevel(float limit, float area)
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.lineWidth = 10f;
                p.lineJoin = LineJoin.Miter;
                p.miterLimit = limit;
                p.BeginPath();
                p.MoveTo(new Vector2(10, 10));
                p.LineTo(new Vector2(50, 10));
                p.LineTo(new Vector2(50, 50));
                p.Stroke();
            });
            Assert.NotEmpty(d.Indices);
            Near(area, d.Area(), 0.05f);
        }

        // [doc] ClosePath "closes the current sub-path with a straight line": a closed square's
        // stroke has a fourth side. [choice] Miter joins at every corner, the start too.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Theory]
        [InlineData(false, 1920f)]
        [InlineData(true, 2560f)]
        public void ClosePathAddsTheClosingSide(bool close, float area)
        {
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.lineWidth = 4f;
                p.lineJoin = LineJoin.Miter;
                p.BeginPath();
                p.MoveTo(new Vector2(20, 20));
                p.LineTo(new Vector2(180, 20));
                p.LineTo(new Vector2(180, 180));
                p.LineTo(new Vector2(20, 180));
                if (close) p.ClosePath();
                p.Stroke();
            });
            Assert.NotEmpty(d.Indices);
            Assert.Equal(close, d.Covers(new Vector2(20, 100)));
            Near(area, d.Area(), 0.05f);
        }

        // [choice] The fill/stroke trapezoids grow with the path's edges and crossings, not with
        // their square: a 250-point chart-like polyline (lineWidth 2, Miter joins, Butt caps)
        // stays within 64 vertices per point (16000; Unbound's model of the sweep gives 5336), and
        // every segment's midpoint is covered.
        // Red on 11f00ae4 (slabs across the whole plane at every vertex and crossing height): the
        // same model gives 209508 vertices, so the bound fails. Red on 85fcc53e: no geometry
        // (Assert.NotEmpty fails).
        [Fact]
        public void LongStrokedPolylineStaysLinearInSize()
        {
            const int N = 250;
            var points = new Vector2[N];
            for (int i = 0; i < N; i++)
                points[i] = new Vector2(10f + 380f * i / (N - 1), 200f + 120f * Mathf.Sin(i * 0.37f) + 40f * Mathf.Sin(i * 1.9f));
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.lineWidth = 2f;
                p.BeginPath();
                p.MoveTo(points[0]);
                for (int i = 1; i < N; i++) p.LineTo(points[i]);
                p.Stroke();
            }, width: 400f, height: 400f);
            Assert.NotEmpty(d.Indices);
            Assert.True(d.Positions.Count <= 64 * N, $"{d.Positions.Count} vertices for {N} points");
            for (int i = 0; i + 1 < N; i++)
                Assert.True(d.Covers((points[i] + points[i + 1]) * 0.5f), $"segment {i} midpoint");
        }

        // ------------------------------------------------------------------ arcs and curves

        // [doc] Arc adds an arc around a center from a start to an end angle in an ArcDirection.
        // [choice] Clockwise is clockwise on screen (angles grow clockwise in UI Toolkit's y-down
        // space): a pie from 0° to 90° is the lower-right quarter clockwise, the other three
        // quarters counter-clockwise. The arc is inscribed within the flattening tolerance.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Theory]
        [InlineData(ArcDirection.Clockwise)]
        [InlineData(ArcDirection.CounterClockwise)]
        public void ArcsGoBothWays(ArcDirection direction)
        {
            var center = new Vector2(100, 100);
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                p.MoveTo(center);
                p.Arc(center, 40f, Angle.Degrees(0f), Angle.Degrees(90f), direction);
                p.ClosePath();
                p.Fill();
            });
            Assert.NotEmpty(d.Indices);
            bool cw = direction == ArcDirection.Clockwise;
            float quarters = cw ? 1f : 3f;
            float exact = quarters * Mathf.PI * 1600f / 4f;
            float arcLength = quarters * Mathf.PI * 40f / 2f;
            Within(exact - arcLength * Tolerance - 0.05f, d.Area(), exact + 0.05f);
            AssertBounds(cw ? Rect.MinMaxRect(100, 100, 140, 140) : Rect.MinMaxRect(60, 60, 140, 140), d.Bounds(), 1e-3f);
            Assert.Equal(cw, d.Covers(new Vector2(120, 120)));
            Assert.Equal(!cw, d.Covers(new Vector2(80, 80)));
        }

        // [choice] BezierCurveTo and QuadraticCurveTo become polylines within the flattening
        // tolerance: every vertex of the filled region (curve closed by its chord) lies within the
        // tolerance of the true curve or on the chord, and the covered area is within
        // tolerance × curve length of the exact area.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BezierFlatteningStaysWithinTheTolerance(bool cubic)
        {
            var p0 = new Vector2(20, 150);
            var c1 = new Vector2(40, 20);
            var c2 = new Vector2(160, 20);
            var q = new Vector2(100, 10);
            var p3 = new Vector2(180, 150);
            Vector2 Curve(float t)
            {
                float u = 1f - t;
                return cubic
                    ? u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p3
                    : u * u * p0 + 2f * u * t * q + t * t * p3;
            }
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                p.MoveTo(p0);
                if (cubic) p.BezierCurveTo(c1, c2, p3);
                else p.QuadraticCurveTo(q, p3);
                p.ClosePath();
                p.Fill();
            });
            Assert.NotEmpty(d.Indices);

            const int Dense = 20000;
            var dense = new Vector2[Dense + 1];
            for (int i = 0; i <= Dense; i++) dense[i] = Curve(i / (float)Dense);
            double exactArea = 0, length = 0;
            for (int i = 0; i < Dense; i++)
            {
                exactArea += Cross(dense[i], dense[i + 1]);
                length += (dense[i + 1] - dense[i]).magnitude;
            }
            exactArea += Cross(dense[Dense], dense[0]);
            exactArea = Math.Abs(exactArea) * 0.5;

            foreach (int i in d.Indices)
            {
                var v = d.Positions[i];
                float best = DistanceToSegment(v, p3, p0);
                for (int k = 0; k < Dense && best > Tolerance; k++) best = Math.Min(best, DistanceToSegment(v, dense[k], dense[k + 1]));
                Assert.True(best <= Tolerance + 1e-3f, $"vertex {v} is {best} from the curve");
            }
            Within((float)(exactArea - length * Tolerance) - 0.5f, d.Area(), (float)(exactArea + length * Tolerance) + 0.5f);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).magnitude;
        }

        // ------------------------------------------------------------------ the element's mesh

        // [doc] MeshGenerationContext.painter2D is "the vector painter object used to issue drawing
        // commands" of generateVisualContent: what it fills is the element's content, in its local
        // space (moved with the element), and the same painter answers within one callback.
        // Red on 85fcc53e: painter2D answers null (Assert.NotNull fails).
        [Fact]
        public void Painter2DDrawsIntoTheElementsContent()
        {
            Painter2D first = null, second = null;
            var d = Paint(mgc =>
            {
                first = mgc.painter2D;
                second = mgc.painter2D;
                var p = mgc.painter2D;
                p.BeginPath();
                Polygon(p, new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 50), new Vector2(0, 50));
                p.Fill();
            }, left: 30f, top: 40f, width: 100f, height: 50f);
            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.NotEmpty(d.Indices);
            Assert.True(d.RetainedContentIndices > 0);
            AssertBounds(Rect.MinMaxRect(30, 40, 130, 90), d.Bounds(), 1e-3f);
            Near(5000f, d.Area(), 0.05f);
        }

        // [doc] fillColor is "the color used for fill paths when using Fill", strokeColor "the color
        // of draw paths when using Stroke": each shape's vertices carry its colour.
        // Red on 85fcc53e: no geometry (Assert.NotEmpty fails).
        [Fact]
        public void FillAndStrokeUseTheirColours()
        {
            var fill = new Color(1f, 0f, 0f, 1f);
            var stroke = new Color(0f, 0f, 1f, 0.5f);
            var d = Paint(mgc =>
            {
                var p = mgc.painter2D;
                p.fillColor = fill;
                p.strokeColor = stroke;
                p.lineWidth = 4f;
                p.BeginPath();
                Polygon(p, new Vector2(10, 10), new Vector2(60, 10), new Vector2(60, 60), new Vector2(10, 60));
                p.Fill();
                p.BeginPath();
                p.MoveTo(new Vector2(100, 100));
                p.LineTo(new Vector2(180, 100));
                p.Stroke();
            });
            Assert.NotEmpty(d.Indices);
            Color32 fill32 = fill, stroke32 = stroke;
            bool sawFill = false, sawStroke = false;
            foreach (int i in d.Indices)
            {
                var c = d.Colors[i];
                var v = d.Positions[i];
                if (c.Equals(fill32))
                {
                    sawFill = true;
                    Assert.True(v.x >= 10f - 1e-3f && v.x <= 60f + 1e-3f && v.y >= 10f - 1e-3f && v.y <= 60f + 1e-3f);
                }
                else
                {
                    Assert.Equal(stroke32, c);
                    sawStroke = true;
                    Assert.True(v.x >= 100f - 1e-3f && v.x <= 180f + 1e-3f && v.y >= 98f - 1e-3f && v.y <= 102f + 1e-3f);
                }
            }
            Assert.True(sawFill);
            Assert.True(sawStroke);
        }

        // [doc] The painter's properties keep what is set (Scripting API get/set properties).
        // Red on 85fcc53e: the stub getters answer defaults (the first Assert.Equal fails).
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
    }
}
