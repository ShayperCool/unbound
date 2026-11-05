using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// GUI.DrawTexture's border and corner parameters, checked on the triangles it hands to the IMGUI
    /// renderer. "Documented" cases follow the Unity 6000.0 Scripting API prose for GUI.DrawTexture
    /// and GUI.DrawTextureWithTexCoords: borderWidths are (left, top, right, bottom) and
    /// Vector4.zero draws the full texture; borderRadiuses are (top-left, top-right, bottom-right,
    /// bottom-left) and Vector4.zero leaves corners square; a border is drawn within the rect and
    /// patterned by the texture. Cases marked "Unbound's choice (unmeasured)" check what Unbound
    /// decided where the docs are silent; no Unity reference capture covers them.
    /// </summary>
    public class GuiDrawTextureParameterTests
    {
        static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);

        sealed class Shape
        {
            internal readonly List<Vector2> Positions = new List<Vector2>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();

            internal bool Covers(float x, float y)
            {
                var p = new Vector2(x, y);
                for (int i = 0; i + 2 < Indices.Count; i += 3)
                {
                    var a = Positions[Indices[i]];
                    var b = Positions[Indices[i + 1]];
                    var c = Positions[Indices[i + 2]];
                    float area = Cross(a, b, c);
                    if (Math.Abs(area) < 1e-6f) continue;
                    float d0 = Cross(a, b, p), d1 = Cross(b, c, p), d2 = Cross(c, a, p);
                    bool neg = d0 < -1e-4f || d1 < -1e-4f || d2 < -1e-4f;
                    bool pos = d0 > 1e-4f || d1 > 1e-4f || d2 > 1e-4f;
                    if (!(neg && pos)) return true;
                }
                return false;
            }

            internal double Area()
            {
                double sum = 0;
                for (int i = 0; i + 2 < Indices.Count; i += 3)
                    sum += Math.Abs(Cross(Positions[Indices[i]], Positions[Indices[i + 1]], Positions[Indices[i + 2]])) * 0.5;
                return sum;
            }
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static Shape Build(Rect screen, Vector4 widths, Vector4 radiuses, Rect? source = null)
        {
            var s = new Shape();
            GUI.DrawTextureShape(screen, source ?? Full, widths, radiuses, s.Positions, s.Uvs, s.Indices);
            return s;
        }

        // ------------------------------------------------------------------ documented

        /// <summary>Documented: "If Vector4.zero, the full texture is drawn" and "If Vector4.zero,
        /// corners will not be rounded", so zero widths and radiuses keep the plain quad.
        /// Red: make DrawTextureNeedsShape return true always (first assertion) or false always
        /// (second and third).</summary>
        [Fact]
        public void ZeroWidthsAndRadiusesKeepThePlainQuad()
        {
            Assert.False(GUI.DrawTextureNeedsShape(Vector4.zero, Vector4.zero));
            Assert.True(GUI.DrawTextureNeedsShape(new Vector4(1f, 0f, 0f, 0f), Vector4.zero));
            Assert.True(GUI.DrawTextureNeedsShape(Vector4.zero, new Vector4(0f, 0f, 0f, 1f)));
        }

        /// <summary>Documented order of borderWidths: left, top, right, bottom; the border lies
        /// within the rect and the rest of it is not drawn.
        /// Red: read the widths in another order in DrawTextureShape (e.g. l = y, t = x), or draw
        /// the inside too (always take the filled branch).</summary>
        [Fact]
        public void BorderWidthsAreLeftTopRightBottom()
        {
            var s = Build(new Rect(0f, 0f, 100f, 80f), new Vector4(2f, 4f, 6f, 8f), Vector4.zero);
            Assert.True(s.Covers(1f, 40f));
            Assert.False(s.Covers(3f, 40f));
            Assert.True(s.Covers(50f, 2f));
            Assert.False(s.Covers(50f, 5f));
            Assert.True(s.Covers(97f, 40f));
            Assert.False(s.Covers(93f, 40f));
            Assert.True(s.Covers(50f, 76f));
            Assert.False(s.Covers(50f, 71f));
            Assert.False(s.Covers(50f, 40f));
            // The square ring alone: no overlap, nothing outside it.
            Assert.Equal(100.0 * 80.0 - 92.0 * 68.0, s.Area(), 2);
        }

        /// <summary>Documented: a zero border width draws the full texture, with rounded corners
        /// when a radius is given.
        /// Red: ignore the radiuses in DrawTextureShape (radius[i] = 0) — the corner point is then
        /// covered.</summary>
        [Fact]
        public void ARadiusWithoutBorderRoundsTheFullTexture()
        {
            var s = Build(new Rect(0f, 0f, 100f, 80f), Vector4.zero, new Vector4(10f, 10f, 10f, 10f));
            Assert.True(s.Covers(50f, 40f));
            Assert.True(s.Covers(50f, 1f));
            Assert.True(s.Covers(4f, 4f));
            Assert.False(s.Covers(1f, 1f));
            Assert.False(s.Covers(99f, 79f));
            // Within the chord tolerance of the exact rounded rect area.
            double exact = 100.0 * 80.0 - (4.0 - Math.PI) * 100.0;
            Assert.InRange(s.Area(), exact - 4 * (Math.PI / 2 * 10) * GUI.DrawTextureArcTolerance, exact + 1e-3);
        }

        /// <summary>Documented order of borderRadiuses: top-left, top-right, bottom-right,
        /// bottom-left; a zero radius leaves its corner square.
        /// Red: swap the corner order in DrawTextureShape/DrawTextureContour (e.g. top-left takes
        /// borderRadiuses.z): (3, 3) is then covered.</summary>
        [Fact]
        public void RadiusesAreTopLeftTopRightBottomRightBottomLeft()
        {
            var s = Build(new Rect(0f, 0f, 100f, 80f), Vector4.zero, new Vector4(16f, 0f, 8f, 0f));
            // Top-left, radius 16: centre (16, 16).
            Assert.False(s.Covers(3f, 3f));
            Assert.True(s.Covers(6f, 6f));
            // Top-right and bottom-left: square.
            Assert.True(s.Covers(99.5f, 0.5f));
            Assert.True(s.Covers(0.5f, 79.5f));
            // Bottom-right, radius 8: centre (92, 72).
            Assert.True(s.Covers(97f, 77f));
            Assert.False(s.Covers(98.5f, 78.5f));
        }

        // ------------------------------------------------------------------ Unbound's choices (unmeasured)

        /// <summary>Unbound's choice (unmeasured): negative widths and radiuses count as zero.
        /// Red: compare the raw vectors with Vector4.zero in DrawTextureNeedsShape.</summary>
        [Fact]
        public void NegativeWidthsAndRadiusesCountAsZero()
        {
            Assert.False(GUI.DrawTextureNeedsShape(new Vector4(-1f, -2f, -3f, -4f), new Vector4(-5f, 0f, 0f, 0f)));
            var s = Build(new Rect(0f, 0f, 50f, 50f), new Vector4(-3f, 5f, 0f, 0f), Vector4.zero);
            // Only the top border: the left one is not drawn.
            Assert.True(s.Covers(25f, 2f));
            Assert.False(s.Covers(1f, 25f));
        }

        /// <summary>Unbound's choice (unmeasured) for "the texture is used to pattern the border":
        /// the texture is mapped over the whole rect, as the plain quad maps it, and the border shows
        /// the part under it. Red: map each border side with the full source rect, as the strips
        /// drawn before this change did (the inner edge of the left border then reads u = 1).</summary>
        [Fact]
        public void TheBorderShowsTheTextureMappedOverTheWholeRect()
        {
            var screen = new Rect(10f, 20f, 100f, 50f);
            var source = new Rect(0f, 0.25f, 1f, 0.5f);
            var s = Build(screen, new Vector4(5f, 5f, 5f, 5f), Vector4.zero, source);
            Assert.NotEmpty(s.Positions);
            for (int i = 0; i < s.Positions.Count; i++)
            {
                var p = s.Positions[i];
                Assert.Equal(source.xMin + (p.x - screen.xMin) / screen.width * source.width, s.Uvs[i].x, 4);
                Assert.Equal(source.yMax - (p.y - screen.yMin) / screen.height * source.height, s.Uvs[i].y, 4);
            }
            // The rect's corners read the source corners the plain quad uses (top is v = yMax).
            Assert.Equal(new Vector2(0f, 0.75f), GUI.DrawTextureUv(screen, source, new Vector2(10f, 20f)));
            Assert.Equal(new Vector2(1f, 0.25f), GUI.DrawTextureUv(screen, source, new Vector2(110f, 70f)));
            // The left border's inner edge is 5% across the texture.
            Assert.Contains(s.Uvs, uv => Math.Abs(uv.x - 0.05f) < 1e-4f);
        }

        /// <summary>Unbound's choice (unmeasured): radiuses too large for a side shrink together
        /// until they fit (a 40 x 20 rect with radius 100 becomes a capsule of radius 10).
        /// Red: drop the fit factor in DrawTextureShape (vertices then leave the rect).</summary>
        [Fact]
        public void OversizedRadiusesShrinkToFit()
        {
            var s = Build(new Rect(0f, 0f, 40f, 20f), Vector4.zero, new Vector4(100f, 100f, 100f, 100f));
            Assert.All(s.Positions, p =>
            {
                Assert.InRange(p.x, -1e-3f, 40f + 1e-3f);
                Assert.InRange(p.y, -1e-3f, 20f + 1e-3f);
            });
            Assert.True(s.Covers(20f, 0.5f));
            Assert.True(s.Covers(2f, 10f));
            Assert.False(s.Covers(1f, 1f));
            Assert.False(s.Covers(39f, 19f));
        }

        /// <summary>Unbound's choice (unmeasured): a corner's inner edge is an ellipse with the
        /// radius less the adjacent widths (here radius 10, width 4: inner radius 6 about the same
        /// centre). Red: make the inner radiuses zero (square inner corners): (4.5, 4.5) is then
        /// inside the hole.</summary>
        [Fact]
        public void InnerCornersFollowTheRadiusLessTheWidths()
        {
            var s = Build(new Rect(0f, 0f, 100f, 100f), new Vector4(4f, 4f, 4f, 4f), new Vector4(10f, 10f, 10f, 10f));
            Assert.False(s.Covers(1f, 1f));
            Assert.True(s.Covers(4.5f, 4.5f));
            Assert.False(s.Covers(8f, 8f));
            Assert.True(s.Covers(2f, 50f));
            Assert.False(s.Covers(50f, 50f));
        }

        /// <summary>Unbound's choice (unmeasured): when the left and right (or top and bottom)
        /// widths meet, the whole rounded rect is drawn, as a fan from its centre.
        /// Red: remove the "widths meet" test from the filled condition in DrawTextureShape.</summary>
        [Fact]
        public void MeetingBordersDrawTheWholeShape()
        {
            var screen = new Rect(0f, 0f, 20f, 10f);
            var s = Build(screen, new Vector4(15f, 1f, 15f, 1f), Vector4.zero);
            Assert.Equal(new Vector2(10f, 5f), s.Positions[0]);
            Assert.True(s.Covers(10f, 5f));
            Assert.Equal(200.0, s.Area(), 2);
            var tall = Build(screen, new Vector4(1f, 6f, 1f, 6f), Vector4.zero);
            Assert.Equal(new Vector2(10f, 5f), tall.Positions[0]);
            Assert.Equal(200.0, tall.Area(), 2);
        }

        /// <summary>Unbound's choice (unmeasured): a quarter arc is split into chords no farther
        /// than DrawTextureArcTolerance from it, at most DrawTextureMaxArcSegments; square corners
        /// have none. Red: change the tolerance or the cap.</summary>
        [Fact]
        public void ArcSegmentsFollowTheChordTolerance()
        {
            Assert.Equal(0, GUI.DrawTextureArcSegments(0f));
            Assert.Equal(0, GUI.DrawTextureArcSegments(-2f));
            Assert.Equal(0, GUI.DrawTextureArcSegments(float.NaN));
            Assert.Equal(1, GUI.DrawTextureArcSegments(0.1f));
            Assert.Equal(4, GUI.DrawTextureArcSegments(10f));
            Assert.Equal(GUI.DrawTextureMaxArcSegments, GUI.DrawTextureArcSegments(1e6f));
            foreach (float radius in new[] { 1f, 3f, 10f, 50f, 200f })
            {
                int n = GUI.DrawTextureArcSegments(radius);
                double sagitta = radius * (1.0 - Math.Cos(Math.PI / 4.0 / n));
                Assert.True(sagitta <= GUI.DrawTextureArcTolerance + 1e-6, $"radius {radius}: {n} chords, sagitta {sagitta}");
            }
        }

        /// <summary>Unbound's choice (unmeasured): a rect with zero or negative width or height
        /// draws no border or rounded shape. Red: remove the size guard in DrawTextureShape.</summary>
        [Fact]
        public void ARectWithoutPositiveSizeDrawsNothing()
        {
            Assert.Empty(Build(new Rect(0f, 0f, 0f, 10f), new Vector4(1f, 1f, 1f, 1f), Vector4.zero).Indices);
            Assert.Empty(Build(new Rect(0f, 0f, 10f, -5f), Vector4.zero, new Vector4(2f, 2f, 2f, 2f)).Indices);
        }

        /// <summary>Unbound's choice (unmeasured): triangles wind as the plain quad's (top left,
        /// top right, bottom right on the screen), so a culling material treats both alike.
        /// Red: reverse the ring's or the fan's index order.</summary>
        [Fact]
        public void TrianglesWindLikeThePlainQuad()
        {
            foreach (var s in new[]
            {
                Build(new Rect(0f, 0f, 60f, 40f), new Vector4(3f, 5f, 7f, 9f), new Vector4(12f, 0f, 6f, 20f)),
                Build(new Rect(0f, 0f, 60f, 40f), Vector4.zero, new Vector4(12f, 0f, 6f, 20f)),
            })
            {
                for (int i = 0; i + 2 < s.Indices.Count; i += 3)
                    Assert.True(Cross(s.Positions[s.Indices[i]], s.Positions[s.Indices[i + 1]], s.Positions[s.Indices[i + 2]]) >= -1e-4f);
            }
        }

        // ------------------------------------------------------------------ alphaBlend

        /// <summary>Documented: alphaBlend false draws without alpha blending. Unbound cannot draw
        /// IMGUI textures opaque yet, so it says so once, loudly, instead of silently blending.
        /// Red: remove the once-only guard (two warnings) or the warning (none). The call sites in
        /// GUI.DrawTexture and GUI.DrawTextureWithTexCoords are not exercised here (they need an
        /// OnGUI Repaint context).</summary>
        [Fact]
        public void OpaqueDrawingIsReportedOnceAsUnsupported()
        {
            bool previous = GUI.s_DrawTextureOpaqueReported;
            var warnings = new List<string>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Warning) warnings.Add(message);
            }
            GUI.s_DrawTextureOpaqueReported = false;
            Application.logMessageReceived += Capture;
            try
            {
                GUI.DrawTextureReportOpaque();
                GUI.DrawTextureReportOpaque();
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                GUI.s_DrawTextureOpaqueReported = previous;
            }
            var warning = Assert.Single(warnings);
            Assert.Contains("alphaBlend false", warning);
            Assert.Contains("not supported", warning);
        }
    }
}
