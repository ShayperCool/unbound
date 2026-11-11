using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>The state a stroke or fill uses.</summary>
    internal struct UaVectorStyle
    {
        public float LineWidth;
        public Color StrokeColor, FillColor;
        public LineJoin Join;
        public LineCap Cap;
        public float MiterLimit;
        /// <summary>The stroke gradient in use, or null: strokes then use <see cref="StrokeColor"/>.</summary>
        public UaStrokeGradient Gradient;
    }

    /// <summary>A Painter2D stroke gradient as it was when set: the colour along a stroke comes
    /// from it by the share of the path's length drawn so far. Colours are rounded to 8 bits per
    /// channel as they are read, and a gradient in linear colour space is shown in sRGB. Matches
    /// the Unity 6000.0.71f1 Editor (tests/projects/uitkpainter, gradient cases).</summary>
    internal sealed class UaStrokeGradient
    {
        // A key closer than this after a position does not count as the next key.
        const float KeyEpsilon = 0.001f;
        readonly Gradient m_Gradient;
        readonly float[] m_ColorTimes, m_AlphaTimes;
        readonly bool m_Linear;

        internal UaStrokeGradient(Gradient source)
        {
            m_Gradient = Copy(source);
            var colors = m_Gradient.colorKeys;
            var alphas = m_Gradient.alphaKeys;
            m_ColorTimes = new float[colors.Length];
            m_AlphaTimes = new float[alphas.Length];
            for (int i = 0; i < colors.Length; i++) m_ColorTimes[i] = colors[i].time;
            for (int i = 0; i < alphas.Length; i++) m_AlphaTimes[i] = alphas[i].time;
            m_Linear = m_Gradient.colorSpace == ColorSpace.Linear;
        }

        internal static Gradient Copy(Gradient source)
        {
            var copy = new Gradient { mode = source.mode, colorSpace = source.colorSpace };
            copy.SetKeys(source.colorKeys, source.alphaKeys);
            return copy;
        }

        internal Gradient Copy() => Copy(m_Gradient);

        internal Color ColorAt(float t)
        {
            var c = m_Gradient.EvaluateUnrounded(t < 0f ? 0f : Math.Min(t, 1f));
            float r = Channel(c.r), g = Channel(c.g), b = Channel(c.b);
            if (m_Linear)
            {
                r = LinearToSrgb(r);
                g = LinearToSrgb(g);
                b = LinearToSrgb(b);
            }
            return new Color(r, g, b, c.a);
        }

        static float Channel(float v) => (float)((uint)(long)(Math.Min(1f, Math.Max(0f, v)) * 255f + 0.5f) & 0xff) / 255f;

        static float LinearToSrgb(float v)
        {
            if (!(v > 0f)) return 0f;
            if (v <= 0.0031308f) return v * 12.92f;
            if (v < 1f) return MathF.Pow(v, 1f / 2.4f) * 1.055f + -0.055f;
            return 1f;
        }

        /// <summary>The time of the first colour or alpha key after <paramref name="t"/>, 1 when
        /// there is none.</summary>
        internal float NextKey(float t)
        {
            float after = t + KeyEpsilon, next = 1f;
            foreach (float time in m_ColorTimes)
            {
                if (after < time)
                {
                    next = time;
                    break;
                }
            }
            foreach (float time in m_AlphaTimes)
            {
                if (after < time && time < next)
                {
                    next = time;
                    break;
                }
            }
            return next;
        }
    }

    /// <summary>Where a segment meets the next one or ends: its points, its direction there, its
    /// color and, for straight segments, the vertices of its two sides (side B: (start, end,
    /// start center), the outer side of a left turn; side A: (start, end, center at this end)). Arcs
    /// and curves have no side vertices; their joins work from <see cref="Dir"/>.</summary>
    internal struct UaSegmentEnd
    {
        public Vector2 P0, P1, Dir;
        public int B0, B1, B2, A0, A1, A2;
        public bool Arc, Valid;
        public Color Color;
    }

    /// <summary>
    /// Strokes a Painter2D path as Unity 6000.0.71f1 does (tests/projects/uitkpainter): every segment
    /// is extruded by half the line width plus a 2-unit anti-aliasing margin on both sides, its
    /// coverage given by UIR's arc data (straight sides as two linear "circles", arcs as their true
    /// circles). Straight segments are two rows of (center, side A, side B) vertices; arcs are
    /// rings of steps of at most 30°; curves are arc and line pieces. Joins (miter up to the miter
    /// limit, else bevel; bevel; round) fill the outer corner and move the inner side vertices to the
    /// intersection of the inner sides; round caps cover a square half-cell around the end.
    /// With a stroke gradient the colour follows the share of the whole path's length (all
    /// sub-paths, in order) drawn so far: a straight segment gets a row of vertices at every
    /// gradient key and at least every eighth of its span of the gradient (at most 17 rows), an
    /// arc one colour per ring step, a curve one per piece end after its pieces are cut at the
    /// gradient keys; joins and caps take the colour of the segment end they belong to.
    /// </summary>
    internal sealed class UaVectorStroke
    {
        const float MaxArcStep = 0.5235987901687622f;
        const float ArcOuterScale = 1.1f;
        const float AlmostOne = 0.99999994f;
        // Gradient strokes: the longest share of a straight segment's gradient span between two
        // rows, the most rows after the first, the gap below which positions count as equal, and
        // the path length below which the gradient stays at its start.
        const float MaxLineStep = 0.125f;
        const int MaxSections = 16;
        const float ProgressEpsilon = 0.001f;
        const float MinPathLength = 1e-4f;

        UaSegmentEnd[] m_Starts = new UaSegmentEnd[16], m_Ends = new UaSegmentEnd[16];
        readonly List<UaCurvePiece> m_Pieces = new List<UaCurvePiece>();
        readonly List<Vector2> m_Quads = new List<Vector2>();

        UaVectorMesh m_Mesh;
        UaVectorStyle m_Style;
        float m_Half;
        UaStrokeGradient m_Gradient;
        // The length drawn so far and the whole path's length (gradient strokes only).
        float m_Done, m_Total;
        readonly List<Color> m_PieceColors = new List<Color>();

        internal void Stroke(UaVectorPath path, in UaVectorStyle style, UaVectorMesh mesh)
        {
            float width = style.LineWidth;
            if (1e-30f > width || !float.IsFinite(width) || 1e-30f > style.StrokeColor.a) return;
            path.Terminate(true);
            m_Mesh = mesh;
            m_Style = style;
            m_Half = width * 0.5f;
            var entries = path.Entries;
            int count = entries.Count;
            m_Gradient = style.Gradient;
            m_Done = 0f;
            m_Total = m_Gradient != null ? PathLength(entries) : 0f;
            if (m_Starts.Length < count)
            {
                Array.Resize(ref m_Starts, Math.Max(count, m_Starts.Length * 2));
                Array.Resize(ref m_Ends, m_Starts.Length);
            }
            for (int i = 0; i < count; i++)
            {
                m_Starts[i] = default;
                m_Ends[i] = default;
            }
            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                switch (e.Kind)
                {
                    case UaPathKind.Line: Line(e, i); break;
                    case UaPathKind.Arc: Arc(e.Arc, e.A, e.B, i); break;
                    case UaPathKind.ArcTo:
                        if (UaArcTo.ToArc(e, out var arc))
                        {
                            arc.Points(out var s, out var t);
                            Arc(arc, s, t, i);
                        }
                        break;
                    case UaPathKind.Bezier: Bezier(entries, i); break;
                }
                if (i > 0)
                {
                    var prev = entries[i - 1];
                    if (prev.Kind == UaPathKind.CapStart)
                    {
                        if (e.IsSegment) Cap(m_Starts[i], true);
                    }
                    else if (prev.Kind == UaPathKind.Join && prev.JoinTo == i && i >= 2 && prev.JoinTo < count
                             && entries[i - 2].IsSegment && entries[prev.JoinTo].IsSegment)
                    {
                        Join(m_Ends[i - 2], m_Starts[i]);
                    }
                }
                if (i + 1 < count)
                {
                    var next = entries[i + 1];
                    if (next.Kind == UaPathKind.CapEnd)
                    {
                        if (e.IsSegment) Cap(m_Ends[i], false);
                    }
                    else if (next.Kind == UaPathKind.Join && i > next.JoinTo && next.JoinTo < count
                             && e.IsSegment && entries[next.JoinTo].IsSegment)
                    {
                        Join(m_Ends[i], m_Starts[next.JoinTo]);
                    }
                }
            }
            m_Mesh = null;
            m_Gradient = null;
        }

        // ------------------------------------------------------------------ gradient

        float PathLength(List<UaPathEntry> entries)
        {
            float total = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                switch (e.Kind)
                {
                    case UaPathKind.Line:
                        total += UaVector.Length(e.B - e.A);
                        break;
                    case UaPathKind.Arc:
                        total += e.Arc.Radius * e.Arc.Sweep();
                        break;
                    case UaPathKind.ArcTo:
                        if (UaArcTo.ToArc(e, out var arc)) total += arc.Radius * arc.Sweep();
                        break;
                    case UaPathKind.Bezier:
                        UaCurveSubdivision.Subdivide(new UaCubic(e.A, e.B, e.C, e.D), m_Pieces);
                        float length = 0f;
                        foreach (var piece in m_Pieces) length += PieceLength(piece);
                        total += length;
                        break;
                }
            }
            return total;
        }

        /// <summary>The share of the path drawn after <paramref name="done"/> of its length.</summary>
        float Progress(float done)
        {
            float t = MinPathLength < m_Total ? done / m_Total : 0f;
            return t >= 1f ? 1f : t <= 0f ? 0f : t;
        }

        Color ColorAt(float t) => m_Gradient != null ? m_Gradient.ColorAt(t) : m_Style.StrokeColor;

        /// <summary>The angle between two directions from a centre. A direction without length
        /// counts as opposite (a half turn): an arc whose inner side lies on its centre advances
        /// the gradient by half a circle of its radius per vertex, as measured
        /// (gradient-arc-inner-at-centre).</summary>
        static float AngleBetween(Vector2 a, Vector2 b)
        {
            float cos = (a.x * b.x + a.y * b.y) / (UaVector.Length(a) * UaVector.Length(b));
            if (float.IsNaN(cos)) return MathF.PI;
            return MathF.Acos(cos <= -1f ? -1f : cos >= 1f ? 1f : cos);
        }

        /// <summary>A curve piece's length: its chord, or its arc's radius times the angle between
        /// its ends; an arc piece whose ends coincide has none.</summary>
        static float PieceLength(in UaCurvePiece piece)
        {
            if (!piece.IsArc) return UaVector.Length(piece.P1 - piece.P0);
            if (piece.P0 == piece.P1) return 0f;
            return AngleBetween(piece.P1 - piece.Center, piece.P0 - piece.Center) * piece.Radius;
        }

        /// <summary>Cuts a curve's pieces at the gradient keys within their share of the path, so
        /// each piece blends between two colours only. A piece is cut at most 16 times; each cut
        /// is placed by the key's position within the whole piece's share, on what is left of the
        /// piece.</summary>
        void SplitPiecesAtKeys()
        {
            float done = m_Done;
            for (int k = 0; k < m_Pieces.Count; k++)
            {
                var piece = m_Pieces[k];
                float t0 = Progress(done);
                done += PieceLength(piece);
                float t1 = Progress(done);
                float key = m_Gradient.NextKey(t0);
                float end = t1 - ProgressEpsilon;
                for (int cuts = 0; key < end && cuts < MaxSections; cuts++)
                {
                    float f = t0 != t1 ? (key - t0) / (t1 - t0) : t0;
                    var rest = piece;
                    Vector2 cut;
                    if (!piece.IsArc)
                    {
                        Vector2 d = piece.P1 - piece.P0;
                        float length = UaVector.Length(d);
                        cut = new Vector2(d.x / length * length * f + piece.P0.x, d.y / length * length * f + piece.P0.y);
                        rest.Pm = new Vector2((cut.x + piece.P1.x) * 0.5f, (cut.y + piece.P1.y) * 0.5f);
                        piece.Pm = new Vector2((cut.x + piece.P0.x) * 0.5f, (cut.y + piece.P0.y) * 0.5f);
                    }
                    else
                    {
                        Vector2 c = piece.Center, v0 = piece.P0 - c, v1 = piece.P1 - c;
                        float l0 = UaVector.Length(v0);
                        var normal = new Vector2(-v0.y / l0, v0.x / l0);
                        float angle = AngleBetween(v1, v0);
                        if (v1.y * normal.y + v1.x * normal.x < 0f) angle = -angle;
                        // Turned by half-angle terms, as the ring steps of an arc are.
                        float h = angle * f * 0.5f;
                        float qz = MathF.Sin(h), qw = MathF.Cos(h), z2 = qz + qz;
                        cut = new Vector2(-(qw * z2) * v0.y + (1f - qz * z2) * v0.x + c.x, (1f - qz * z2) * v0.y + qw * z2 * v0.x + c.y);
                        piece.Pm = OnCircle(new Vector2((piece.P0.x + cut.x) * 0.5f - c.x, (piece.P0.y + cut.y) * 0.5f - c.y), c, piece.Radius);
                        rest.Pm = OnCircle(new Vector2((cut.x + piece.P1.x) * 0.5f - c.x, (cut.y + piece.P1.y) * 0.5f - c.y), c, piece.Radius);
                    }
                    float at = f * (piece.T1 - piece.T0) + piece.T0;
                    piece.P1 = cut;
                    piece.T1 = at;
                    rest.P0 = cut;
                    rest.T0 = at;
                    m_Pieces[k] = piece;
                    m_Pieces.Insert(++k, rest);
                    piece = rest;
                    key = m_Gradient.NextKey(key);
                }
            }
        }

        static Vector2 OnCircle(Vector2 direction, Vector2 center, float radius)
        {
            float l = UaVector.Length(direction);
            return new Vector2(direction.x / l * radius + center.x, direction.y / l * radius + center.y);
        }

        // ------------------------------------------------------------------ lines

        void Line(in UaPathEntry e, int index)
        {
            Vector2 p0 = e.A, p1 = e.B;
            float dx = p1.x - p0.x, dy = p1.y - p0.y;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            int b = m_Mesh.VertexCount;
            // The segment's span of the gradient; without one, a single span.
            float t0 = 0f, t1 = 1f;
            if (m_Gradient != null)
            {
                t0 = Progress(m_Done);
                t1 = Progress(m_Done + length);
            }
            if (t1 - ProgressEpsilon <= t0)
            {
                LineSection(p0, dx, dy, length, ColorAt(t0), 0, 0f);
                LineSection(p0, dx, dy, length, ColorAt(t1), 1, 1f);
            }
            else
            {
                float maxStep = MaxLineStep * (t1 - t0);
                float t = t0;
                int row = 0;
                do
                {
                    LineSection(p0, dx, dy, length, ColorAt(t), row, (t - t0) / (t1 - t0));
                    float next = t1;
                    if (m_Gradient != null)
                    {
                        next = m_Gradient.NextKey(t);
                        if (maxStep < next - t) next = t + maxStep;
                    }
                    t = next;
                    row++;
                }
                while (row < MaxSections && t < t1 - ProgressEpsilon);
                LineSection(p0, dx, dy, length, ColorAt(t1), row, 1f);
            }
            m_Done += length;
            // The start works from the first two rows, the end from the last two.
            int n = m_Mesh.VertexCount;
            var dir = new Vector2(dx / length, dy / length);
            m_Starts[index] = new UaSegmentEnd { P0 = p0, P1 = p1, Dir = dir, B0 = b + 2, B1 = b + 5, B2 = b, A0 = b + 1, A1 = b + 4, A2 = b, Color = ColorAt(t0), Valid = true };
            m_Ends[index] = new UaSegmentEnd { P0 = p0, P1 = p1, Dir = dir, B0 = n - 4, B1 = n - 1, B2 = n - 6, A0 = n - 5, A1 = n - 2, A2 = n - 3, Color = ColorAt(t1), Valid = true };
            float nx = dir.x * m_Half, ny = dir.y * m_Half;
            m_Mesh.AddBounds(p0.x + ny, p0.y - nx);
            m_Mesh.AddBounds(p0.x - ny, p0.y + nx);
            m_Mesh.AddBounds(p1.x + ny, p1.y - nx);
            m_Mesh.AddBounds(p1.x - ny, p1.y + nx);
        }

        /// <summary>The (center, side A, side B) vertices at <paramref name="t"/> along a straight
        /// segment; from the second row on, the two quads to the previous row.</summary>
        void LineSection(Vector2 p0, float dx, float dy, float length, Color color, int row, float t)
        {
            float width = m_Style.LineWidth;
            float total = width + 4f;
            float extent = width * 0.5f + UaVector.EdgeBuffer;
            float s = length * t;
            var center = new Vector2(dx / length * s + p0.x, dy / length * s + p0.y);
            var n = new Vector2(-dy / length * extent, dx / length * extent);
            float yMax = total / (width + 2f), wMax = total * 0.5f;
            var tint = UaVector.Tint(color);
            int c = m_Mesh.Add(center, tint, new Vector4(0f, (yMax + 0f) * 0.5f, 0f, (wMax + 0f) * 0.5f));
            m_Mesh.Add(center + n, tint, Vector4.zero);
            m_Mesh.Add(center - n, tint, new Vector4(0f, yMax, 0f, wMax));
            if (row == 0) return;
            int p = c - 3;
            m_Mesh.Triangle(c, p, c + 2);
            m_Mesh.Triangle(c + 2, p, p + 2);
            m_Mesh.Triangle(c, p + 1, p);
            m_Mesh.Triangle(c, c + 1, p + 1);
        }

        // ------------------------------------------------------------------ arcs

        void Arc(in UaArc arc, Vector2 start, Vector2 end, int index)
        {
            float half = m_Half;
            float sweep = arc.Sweep();
            Vector2 c = arc.Center;
            float r = arc.Radius;
            float step = sweep > MaxArcStep ? MaxArcStep : sweep * 0.5f;
            if (arc.Ccw) step = -step;
            var color = ColorAt(Progress(m_Done));
            var endColor = color;
            Vector2 us = UaVector.Normalized(start - c), ue = UaVector.Normalized(end - c);
            if (!(UaVector.Epsilon < Math.Abs(step)))
            {
                // Too little sweep for a ring: nothing is drawn. Ends apart join and cap along the
                // chord; ends that meet (an arc-to of radius 0, measured: stroke-arcto-radius-zero)
                // neither join nor cap.
                var chord = end - start;
                float length = UaVector.Length(chord);
                if (UaVector.Epsilon > length)
                {
                    m_Starts[index] = default;
                    m_Ends[index] = default;
                    return;
                }
                var dir = new Vector2(chord.x / length, chord.y / length);
                m_Starts[index] = new UaSegmentEnd { P0 = start, P1 = end, Dir = dir, Arc = true, Color = color, Valid = true };
                m_Ends[index] = m_Starts[index];
                return;
            }
            {
                float ratio = sweep / Math.Abs(step);
                int n = Math.Abs((int)(ratio >= 0f ? ratio + AlmostOne : ratio));
                float h = step * 0.5f;
                float qz = MathF.Sin(h), qw = MathF.Cos(h), z2 = qz + qz;
                float m00 = 1f - qz * z2, m01 = -(qw * z2), m10 = qw * z2;
                float outer = ArcOuterScale * r + half + UaVector.EdgeBuffer;
                float inner = r - half + -UaVector.EdgeBuffer;
                float inv1 = 1f / (r + half), inv2 = 1f / (r - half);
                int first = m_Mesh.VertexCount;
                var p = start;
                Vector2 previous = default;
                for (int k = -1; k < n; k++)
                {
                    Vector2 v = k == -1 ? start - c : k == n - 1 ? end - c : p - c;
                    var d = UaVector.Normalized(v);
                    for (int side = 0; side < 2; side++)
                    {
                        float radius = side == 0 ? outer : inner;
                        var q = new Vector2(radius * d.x + c.x, radius * d.y + c.y);
                        Vector2 w = q - c;
                        if (m_Gradient != null)
                        {
                            // The length drawn grows by the angle from the vertex before, on the arc's own radius.
                            if (k >= 0 || side == 1) m_Done += AngleBetween(w, previous - c) * r;
                            previous = q;
                            endColor = ColorAt(Progress(m_Done));
                        }
                        m_Mesh.Add(q, UaVector.Tint(endColor), new Vector4(w.x * inv1, w.y * inv1, w.x * inv2, w.y * inv2));
                    }
                    p = new Vector2(d.y * m01 + d.x * m00 + 0f + c.x, d.y * m00 + d.x * m10 + 0f + c.y);
                }
                for (int k = 0, i = first; k < n; k++, i += 2)
                {
                    m_Mesh.TriangleCW(i, i + 1, i + 2);
                    m_Mesh.TriangleCW(i + 2, i + 1, i + 3);
                }
            }
            Vector2 ds = arc.Ccw ? new Vector2(us.y, -us.x) : new Vector2(-us.y, us.x);
            Vector2 de = arc.Ccw ? new Vector2(ue.y, -ue.x) : new Vector2(-ue.y, ue.x);
            m_Starts[index] = new UaSegmentEnd { P0 = start, P1 = end, Dir = ds, Arc = true, Color = color, Valid = true };
            m_Ends[index] = new UaSegmentEnd { P0 = start, P1 = end, Dir = de, Arc = true, Color = endColor, Valid = true };
            arc.AddBounds(m_Mesh, half, clockwise: false);
        }

        // ------------------------------------------------------------------ curves

        void Bezier(List<UaPathEntry> entries, int index)
        {
            var e = entries[index];
            var curve = new UaCubic(e.A, e.B, e.C, e.D);
            UaCurveSubdivision.Subdivide(curve, m_Pieces);
            if (m_Pieces.Count == 0) return;
            if (m_Gradient != null) SplitPiecesAtKeys();
            int n = m_Pieces.Count;
            // The colour at each piece end: piece k runs from colour k to colour k + 1.
            m_PieceColors.Clear();
            m_PieceColors.Add(ColorAt(Progress(m_Done)));
            for (int k = 0; k < n; k++)
            {
                if (m_Gradient != null) m_Done += PieceLength(m_Pieces[k]);
                m_PieceColors.Add(ColorAt(Progress(m_Done)));
            }
            bool capStart = index > 0 && entries[index - 1].Kind == UaPathKind.CapStart;
            bool capEnd = index + 1 < entries.Count && entries[index + 1].Kind == UaPathKind.CapEnd;
            float half = m_Half, extent = half + UaVector.EdgeBuffer;
            m_Quads.Clear();
            m_PieceFlags.Clear();
            for (int k = 0; k < n; k++)
            {
                var piece = m_Pieces[k];
                bool arc = piece.IsArc && piece.Radius > 0f;
                var t0 = k == 0 && capStart ? UaCurveSubdivision.PieceTangent(piece, curve, true) : curve.Tangent(piece.T0);
                var t1 = k == n - 1 && capEnd ? UaCurveSubdivision.PieceTangent(piece, curve, false) : curve.Tangent(piece.T1);
                float cross = t0.x * t1.y - t1.x * t0.y;
                var n0 = new Vector2(-t0.y * extent, t0.x * extent);
                var n1 = new Vector2(t1.y * extent, t1.x * extent);
                Vector2 a0 = n0 + piece.P0, a1 = piece.P0 - n0;
                var b0 = new Vector2(piece.P1.x - n1.x, piece.P1.y + n1.y);
                var b1 = new Vector2(piece.P1.x + n1.x, piece.P1.y - n1.y);
                if (cross >= 0f) { m_Quads.Add(a1); m_Quads.Add(b1); m_Quads.Add(b0); m_Quads.Add(a0); }
                else { m_Quads.Add(a0); m_Quads.Add(b0); m_Quads.Add(b1); m_Quads.Add(a1); }
                byte flags = 0;
                if (arc)
                {
                    if (UaVector.Dot(t0, t1) < 0f) flags = PieceSkipped;
                    else if (CollapseInner(m_Quads.Count - 4)) flags = PieceCollapsed;
                }
                m_PieceFlags.Add(flags);
            }
            for (int k = 0; k < n; k++)
            {
                var piece = m_Pieces[k];
                if (m_PieceFlags[k] == PieceSkipped) continue;
                int q = k * 4, first = m_Mesh.VertexCount;
                // A piece's quad: its start corner, the two end corners, the other start corner.
                Color32 tint = UaVector.Tint(m_PieceColors[k]), endTint = UaVector.Tint(m_PieceColors[k + 1]);
                if (piece.IsArc && piece.Radius > 0f)
                {
                    float toHit = UaVector.Length(piece.Pm - m_Quads[q + 2]);
                    if (m_PieceFlags[k] == PieceCollapsed && half >= toHit)
                    {
                        // Wider than the curve's radius: the inner side is the curve's center,
                        // covered up to the outer circle only.
                        var hit = m_Quads[q + 2];
                        float r = toHit + half;
                        m_Mesh.Add(m_Quads[q], tint, new Vector4((m_Quads[q].x - hit.x) / r, (m_Quads[q].y - hit.y) / r, UaVector.UnusedArc, UaVector.UnusedArc));
                        m_Mesh.Add(m_Quads[q + 1], endTint, new Vector4((m_Quads[q + 1].x - hit.x) / r, (m_Quads[q + 1].y - hit.y) / r, UaVector.UnusedArc, UaVector.UnusedArc));
                        m_Mesh.Add(m_Quads[q + 2], endTint, new Vector4(0f, 0f, UaVector.UnusedArc, UaVector.UnusedArc));
                        m_Mesh.Add(m_Quads[q + 3], tint, new Vector4(0f, 0f, UaVector.UnusedArc, UaVector.UnusedArc));
                    }
                    else
                    {
                        float m1 = 1f / (piece.Radius + half), m2 = 1f / (piece.Radius - half);
                        for (int j = 0; j < 4; j++)
                        {
                            Vector2 v = m_Quads[q + j], d = v - piece.Center;
                            m_Mesh.Add(v, j == 1 || j == 2 ? endTint : tint, new Vector4(d.x * m1, d.y * m1, d.x * m2, d.y * m2));
                        }
                    }
                }
                else
                {
                    float m = 1f / (m_Style.LineWidth + 2f);
                    Vector2 c0 = m_Quads[q] - m_Quads[q + 3], c1 = m_Quads[q + 1] - m_Quads[q + 2];
                    m_Mesh.Add(m_Quads[q], tint, new Vector4(c0.x * m, c0.y * m, c0.x * 0.5f, c0.y * 0.5f));
                    m_Mesh.Add(m_Quads[q + 1], endTint, new Vector4(c1.x * m, c1.y * m, c1.x * 0.5f, c1.y * 0.5f));
                    m_Mesh.Add(m_Quads[q + 2], endTint, Vector4.zero);
                    m_Mesh.Add(m_Quads[q + 3], tint, Vector4.zero);
                }
                m_Mesh.TriangleCW(first, first + 1, first + 2);
                m_Mesh.TriangleCW(first, first + 2, first + 3);
                if (m_PieceFlags[k] == PieceCollapsed) OuterMargin(piece, q, half, tint, endTint);
            }
            UaCurvePiece f = m_Pieces[0], l = m_Pieces[n - 1];
            var ds = f.IsArc && f.Radius > 0f ? UaCurveSubdivision.PieceTangent(f, curve, true) : curve.Tangent(0f);
            var de = l.IsArc && l.Radius > 0f ? UaCurveSubdivision.PieceTangent(l, curve, false) : curve.Tangent(1f);
            m_Starts[index] = new UaSegmentEnd { P0 = f.P0, P1 = f.P1, Dir = ds, Arc = true, Color = m_PieceColors[0], Valid = true };
            m_Ends[index] = new UaSegmentEnd { P0 = l.P0, P1 = l.P1, Dir = de, Arc = true, Color = m_PieceColors[n], Valid = true };
            m_Mesh.AddBounds(curve.P0);
            m_Mesh.AddBounds(curve.P3);
            AddNormalBounds(curve.P0, ds);
            AddNormalBounds(curve.P3, de);
            curve.AddExtremeBounds(m_Mesh, half);
        }

        /// <summary>A collapsed piece's outer edge q0→q1 pushed outwards past the coverage circles
        /// around the crossing (radius from each end's curve point plus half the line width), plus
        /// the anti-aliasing margin when it moves at all. With a gradient the edge has the
        /// piece's start colour and the pushed-out side its end colour, as measured.</summary>
        void OuterMargin(in UaCurvePiece piece, int q, float half, Color32 tint, Color32 endTint)
        {
            Vector2 q0 = m_Quads[q], q1 = m_Quads[q + 1], hit = m_Quads[q + 2];
            float r0 = UaVector.Length(piece.P0 - hit) + half, r1 = UaVector.Length(piece.P1 - hit) + half;
            float m = UaVector.Length((q0 + q1) * 0.5f - hit);
            float push = Math.Max(Math.Max(r1, r0) - m, 0f);
            if (UaVector.Epsilon < push) push += UaVector.EdgeBuffer;
            var e = UaVector.Normalized(q1 - q0);
            var n = new Vector2(e.y, -e.x);
            if (UaVector.Dot(n, (q0 + q1) * 0.5f - hit) < 0f) n = -n;
            Vector2 o0 = q0 + n * push, o1 = q1 + n * push;
            float i0 = 1f / r0, i1 = 1f / r1;
            int first = m_Mesh.Add(q0, tint, new Vector4((q0.x - hit.x) * i0, (q0.y - hit.y) * i0, UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(q1, tint, new Vector4((q1.x - hit.x) * i1, (q1.y - hit.y) * i1, UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(o1, endTint, new Vector4((o1.x - hit.x) * i1, (o1.y - hit.y) * i1, UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(o0, endTint, new Vector4((o0.x - hit.x) * i0, (o0.y - hit.y) * i0, UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.TriangleCW(first, first + 1, first + 2);
            m_Mesh.TriangleCW(first, first + 2, first + 3);
        }

        const byte PieceSkipped = 1, PieceCollapsed = 2;
        readonly List<byte> m_PieceFlags = new List<byte>();

        void AddNormalBounds(Vector2 p, Vector2 dir)
        {
            m_Mesh.AddBounds(p.x + dir.y * m_Half, p.y - dir.x * m_Half);
            m_Mesh.AddBounds(p.x - dir.y * m_Half, p.y + dir.x * m_Half);
        }

        /// <summary>A curve piece's quad (q0, q1, q2, q3: start and end on one side, end and start on
        /// the other): when its start cross-section q0→q3 and end cross-section q1→q2 cross inside
        /// both (the stroke is wider than the curve's radius there), q2 and q3 move to the crossing.</summary>
        bool CollapseInner(int q)
        {
            Vector2 q0 = m_Quads[q], q1 = m_Quads[q + 1], q2 = m_Quads[q + 2], q3 = m_Quads[q + 3];
            Vector2 d1 = q3 - q0, d2 = q2 - q1;
            float cross = d1.y * d2.x - d1.x * d2.y;
            if (!(Math.Abs(cross) > 1e-6f)) return false;
            float t = ((q1.y - q0.y) * d2.x - d2.y * (q1.x - q0.x)) / cross;
            var hit = new Vector2(t * d1.x + q0.x, t * d1.y + q0.y);
            if (!WithinSection(hit - q1, d2) || !WithinSection(hit - q0, d1)) return false;
            m_Quads[q + 2] = hit;
            m_Quads[q + 3] = hit;
            return true;
        }

        /// <summary>Whether <paramref name="v"/> (from a segment's start) reaches along the segment
        /// <paramref name="d"/> without passing its end.</summary>
        static bool Within(Vector2 v, Vector2 d)
        {
            float ld = UaVector.Length(d), lv = UaVector.Length(v);
            float r = 0f;
            if (ld > 1e-30f)
            {
                r = lv / ld;
                if (UaVector.Dot(d, v) < 0f) r = -r;
            }
            return r >= 0f && r <= 1f;
        }

        static bool WithinSection(Vector2 v, Vector2 d)
        {
            float lv = UaVector.Length(v), ld = UaVector.Length(d);
            Vector2 nv = lv > UaVector.Epsilon ? v / lv : Vector2.zero, nd = ld > UaVector.Epsilon ? d / ld : Vector2.zero;
            float r = ld > UaVector.Epsilon ? lv / ld : 0f;
            if (UaVector.Dot(nv, nd) < 0f) r = -r;
            return r >= 0f && r <= 1f;
        }

        // ------------------------------------------------------------------ joins

        void Join(in UaSegmentEnd a, in UaSegmentEnd b)
        {
            if (!a.Valid || !b.Valid) return;
            switch (m_Style.Join)
            {
                case LineJoin.Miter: Miter(a, b); break;
                case LineJoin.Bevel: Bevel(a, b); break;
                case LineJoin.Round: RoundJoin(a, b); break;
            }
        }

        static float TurnCross(in UaSegmentEnd a, in UaSegmentEnd b) =>
            (a.P1.x - a.P0.x) * (b.P1.y - b.P0.y) - (a.P1.y - a.P0.y) * (b.P1.x - b.P0.x);

        static bool SameDirection(in UaSegmentEnd a, in UaSegmentEnd b) =>
            1e-30f > Math.Abs(a.Dir.x * b.Dir.x + a.Dir.y * b.Dir.y + -1f);

        /// <summary>The outer side of the turn: the end side's start and end points of
        /// <paramref name="a"/> (o1, o2) and the start side's points of <paramref name="b"/> (o3, o4).
        /// Arcs and curves offset their end points by half the line width.</summary>
        void OuterPoints(in UaSegmentEnd a, in UaSegmentEnd b, out Vector2 o1, out Vector2 o2, out Vector2 o3, out Vector2 o4)
        {
            bool left = TurnCross(a, b) >= 0f;
            if (a.Arc || b.Arc)
            {
                float h = m_Half;
                Vector2 na = left ? new Vector2(a.Dir.y * h, -a.Dir.x * h) : new Vector2(-a.Dir.y * h, a.Dir.x * h);
                Vector2 nb = left ? new Vector2(b.Dir.y * h, -b.Dir.x * h) : new Vector2(-b.Dir.y * h, b.Dir.x * h);
                o1 = new Vector2(a.P1.x - a.Dir.x + na.x, a.P1.y - a.Dir.y + na.y);
                o2 = na + a.P1;
                o3 = b.P0 + nb;
                o4 = new Vector2(b.P0.x + b.Dir.x + nb.x, b.P0.y + b.Dir.y + nb.y);
                return;
            }
            var v = m_Mesh.Vertices;
            o1 = v[left ? a.B0 : a.A0].position;
            o2 = v[left ? a.B1 : a.A1].position;
            o3 = v[left ? b.B0 : b.A0].position;
            o4 = v[left ? b.B1 : b.A1].position;
        }

        /// <summary>Moves the inner side vertices of two straight segments to the crossing of
        /// their inner sides when it lies within both.</summary>
        void ConnectInner(in UaSegmentEnd a, in UaSegmentEnd b)
        {
            if (a.Arc || b.Arc) return;
            bool left = TurnCross(a, b) >= 0f;
            int a0 = left ? a.A0 : a.B0, a1 = left ? a.A1 : a.B1, a2 = left ? a.A2 : a.B2;
            int b0 = left ? b.A0 : b.B0, b1 = left ? b.A1 : b.B1, b2 = left ? b.A2 : b.B2;
            var v = m_Mesh.Vertices;
            Vector2 pa0 = v[a0].position, pa1 = v[a1].position, pb0 = v[b0].position, pb1 = v[b1].position;
            Vector2 dA = pa1 - pa0, dB = pb1 - pb0;
            float cross = dA.y * dB.x - dA.x * dB.y;
            if (!(Math.Abs(cross) > 1e-6f)) return;
            float t = ((pb0.y - pa0.y) * dB.x - dB.y * (pb0.x - pa0.x)) / cross;
            var hit = new Vector2(t * dA.x + pa0.x, t * dA.y + pa0.y);
            if (!Within(hit - pa0, dA) || !Within(hit - pb0, dB)) return;
            var ca = InterpolatedCircle(hit, v[a0], v[a1], v[a2]);
            var cb = InterpolatedCircle(hit, v[b0], v[b1], v[b2]);
            v[a1].position = hit;
            v[a1].circle = ca;
            v[b0].position = hit;
            v[b0].circle = cb;
        }

        /// <summary>The circle data at <paramref name="p"/> interpolated barycentrically in the
        /// triangle of <paramref name="v0"/>, <paramref name="v1"/>, <paramref name="v2"/>.</summary>
        static Vector4 InterpolatedCircle(Vector2 p, in UaVectorVertex v0, in UaVectorVertex v1, in UaVectorVertex v2)
        {
            Vector2 vp = p - v0.position, a = v2.position - v0.position, b = v1.position - v0.position;
            float dab = b.y * a.y + b.x * a.x;
            float daa = a.y * a.y + a.x * a.x;
            float dbb = b.y * b.y + b.x * b.x;
            float dpb = vp.y * b.y + vp.x * b.x;
            float dpa = vp.y * a.y + vp.x * a.x;
            float den = dbb * daa - dab * dab;
            float w1 = (daa * dpb - dab * dpa) / den;
            float w2 = (dbb * dpa - dab * dpb) / den;
            float w0 = 1f - w1 - w2;
            Vector4 c0 = v0.circle, c1 = v1.circle, c2 = v2.circle;
            return new Vector4(c2.x * w2 + (c1.x * w1 + c0.x * w0), c2.y * w2 + (c1.y * w1 + c0.y * w0),
                c2.z * w2 + (c1.z * w1 + c0.z * w0), c2.w * w2 + (c1.w * w1 + c0.w * w0));
        }

        void Miter(in UaSegmentEnd a, in UaSegmentEnd b)
        {
            if (SameDirection(a, b)) return;
            Vector2 d1 = a.Dir, d2 = b.Dir;
            float l1 = UaVector.Length(d1), l2 = UaVector.Length(d2);
            float cos = (-(d1.y * d2.y) - d1.x * d2.x) / (l1 * l2);
            float angle = MathF.Acos(UaVector.ClampUnit(cos));
            float limit = MathF.Asin(1f / m_Style.MiterLimit) * 2f;
            if (!(angle > limit))
            {
                Bevel(a, b);
                return;
            }
            ConnectInner(a, b);
            OuterPoints(a, b, out var o1, out var o2, out var o3, out var o4);
            Vector2 r1 = o2 - o1, r2 = o4 - o3;
            float cross = r1.y * r2.x - r1.x * r2.y;
            if (!(Math.Abs(cross) > 1e-6f)) return;
            float t = ((o3.y - o1.y) * r2.x - r2.y * (o3.x - o1.x)) / cross;
            var tip = new Vector2(t * r1.x + o1.x, t * r1.y + o1.y);
            float half = m_Half;
            var c = a.P1;
            var tint = UaVector.Tint(a.Color);
            // Each half of the corner is centred on its own segment's end (they differ where a
            // closing join spans a gap).
            var cB = b.P0;
            var ca = new Vector4((o2.x - c.x) / half, (o2.y - c.y) / half, UaVector.UnusedArc, UaVector.UnusedArc);
            var cb = new Vector4((o3.x - cB.x) / half, (o3.y - cB.y) / half, UaVector.UnusedArc, UaVector.UnusedArc);
            var center = new Vector4(0f, 0f, UaVector.UnusedArc, UaVector.UnusedArc);
            int i = m_Mesh.Add(c, tint, center);
            m_Mesh.Add(o2, tint, ca);
            m_Mesh.Add(tip, tint, ca);
            m_Mesh.Add(cB, tint, center);
            m_Mesh.Add(o3, tint, cb);
            m_Mesh.Add(tip, tint, cb);
            m_Mesh.TriangleCW(i, i + 1, i + 2);
            m_Mesh.TriangleCW(i + 3, i + 4, i + 5);
            // The corner's unextended outer point: along the outer side's normal by half the line
            // width, then along the miter by the same proportion.
            Vector2 side = o2 - c, toTip = tip - o2;
            float lSide = UaVector.Length(side), lTip = UaVector.Length(toTip);
            if (1e-30f > lSide || 1e-30f > lTip) return;
            float k = half * lTip / lSide;
            m_Mesh.AddBounds(side.x / lSide * half + c.x + toTip.x / lTip * k, side.y / lSide * half + c.y + toTip.y / lTip * k);
        }

        void Bevel(in UaSegmentEnd a, in UaSegmentEnd b)
        {
            if (SameDirection(a, b)) return;
            ConnectInner(a, b);
            OuterPoints(a, b, out _, out var o2, out var o3, out _);
            float half = m_Half;
            var c = a.P1;
            var ua = UaVector.Normalized(o2 - a.P1);
            var ub = UaVector.Normalized(o3 - b.P0);
            var mid = new Vector2((b.P0.x + ub.x * half + (ua.x * half + c.x)) * 0.5f - c.x,
                (b.P0.y + ub.y * half + (ua.y * half + c.y)) * 0.5f - c.y);
            float dist = UaVector.Length(mid);
            if (UaVector.Epsilon > dist) return;
            float k = UaVector.EdgeBuffer + dist;
            var tip = new Vector2(mid.x / dist * k + c.x, mid.y / dist * k + c.y);
            // Each half is centred on its own segment's end, as for miters.
            var cB = b.P0;
            var tint = UaVector.Tint(a.Color);
            var center = new Vector4(0f, 0f, UaVector.UnusedArc, UaVector.UnusedArc);
            int i = m_Mesh.Add(c, tint, center);
            m_Mesh.Add(o2, tint, new Vector4(0f, UaVector.Length(new Vector2((o2.x - c.x) / half, (o2.y - c.y) / half)), UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(tip, tint, new Vector4(0f, UaVector.Length(new Vector2((tip.x - c.x) / dist, (tip.y - c.y) / dist)), UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(cB, tint, center);
            m_Mesh.Add(o3, tint, new Vector4(0f, UaVector.Length(new Vector2((o3.x - cB.x) / half, (o3.y - cB.y) / half)), UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(tip, tint, new Vector4(0f, UaVector.Length(new Vector2((tip.x - cB.x) / dist, (tip.y - cB.y) / dist)), UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.TriangleCW(i, i + 1, i + 2);
            m_Mesh.TriangleCW(i + 3, i + 4, i + 5);
            m_Mesh.AddBounds(mid + c);
        }

        void RoundJoin(in UaSegmentEnd a, in UaSegmentEnd b)
        {
            if (SameDirection(a, b)) return;
            ConnectInner(a, b);
            OuterPoints(a, b, out _, out var o2, out var o3, out _);
            if (o2 == o3) return;
            Vector2 dA = a.P1 - a.P0, dB = b.P1 - b.P0;
            float lA = UaVector.Length(dA);
            if (UaVector.Epsilon > lA) return;
            float lB = UaVector.Length(dB);
            if (UaVector.Epsilon > lB) return;
            float cross = dA.x / lA * (dB.y / lB) - dA.y / lA * (dB.x / lB);
            float half = m_Half, extent = UaVector.EdgeBuffer + half;
            Vector2 d = o3 - o2;
            float lD = MathF.Sqrt(d.y * d.y + d.x * d.x);
            var u = new Vector2(d.y / lD * extent, extent * (-d.x / lD));
            if (cross < 0f) u = -u;
            Fan(a.P1, a.Color, o2, o2 + u, o3 + u, o3, squareBounds: false);
            // Bounds: half the line width out along the corner's bisector.
            var m = (o2 + o3) * 0.5f - a.P1;
            float l2 = m.x * m.x + m.y * m.y;
            if (UaVector.TangentEpsilon * UaVector.TangentEpsilon >= l2) return;
            float l = MathF.Sqrt(l2);
            m_Mesh.AddBounds(m.x / l * half + a.P1.x, half * (m.y / l) + a.P1.y);
        }

        /// <summary>A round join or cap: three triangles from the center over a quad around its
        /// circle, coverage by that circle (radius half the line width).</summary>
        void Fan(Vector2 c, Color color, Vector2 q0, Vector2 q1, Vector2 q2, Vector2 q3, bool squareBounds = true)
        {
            float half = m_Half;
            var tint = UaVector.Tint(color);
            int i = m_Mesh.Add(c, tint, new Vector4(0f, 0f, UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.Add(q0, tint, CircleAround(q0, c, half));
            m_Mesh.Add(q1, tint, CircleAround(q1, c, half));
            m_Mesh.Add(q2, tint, CircleAround(q2, c, half));
            m_Mesh.Add(q3, tint, CircleAround(q3, c, half));
            m_Mesh.TriangleCW(i, i + 1, i + 2);
            m_Mesh.TriangleCW(i, i + 2, i + 3);
            m_Mesh.TriangleCW(i, i + 3, i + 4);
            if (!squareBounds) return;
            m_Mesh.AddBounds(c.x - half, c.y - half);
            m_Mesh.AddBounds(c.x + half, c.y + half);
        }

        static Vector4 CircleAround(Vector2 p, Vector2 c, float radius) =>
            new Vector4((p.x - c.x) / radius, (p.y - c.y) / radius, UaVector.UnusedArc, UaVector.UnusedArc);

        // ------------------------------------------------------------------ caps

        void Cap(in UaSegmentEnd j, bool start)
        {
            if (!j.Valid || m_Style.Cap != LineCap.Round) return;
            float half = m_Half;
            if (UaVector.Epsilon > half) return;
            float extent = half + UaVector.EdgeBuffer;
            Vector2 sideB, sideA, c, d;
            if (j.Arc)
            {
                d = UaVector.Normalized(j.Dir);
                if (start) d = -d;
                c = start ? j.P0 : j.P1;
                float ex = extent * d.x, ey = extent * d.y;
                sideB = new Vector2(c.x - ey, c.y + ex);
                sideA = new Vector2(c.x + ey, c.y - ex);
                var cb = new Vector2(sideB.x + ex, sideB.y + ey);
                var ca = new Vector2(sideA.x + ex, sideA.y + ey);
                Fan(c, j.Color, sideB, cb, ca, sideA);
                return;
            }
            var v = m_Mesh.Vertices;
            Vector2 b0 = v[j.B0].position, b1 = v[j.B1].position;
            d = UaVector.Normalized(b1 - b0);
            if (start)
            {
                d = -d;
                sideB = b0;
                sideA = v[j.A0].position;
                c = j.P0;
            }
            else
            {
                sideB = b1;
                sideA = v[j.A1].position;
                c = j.P1;
            }
            Fan(c, j.Color, sideB, sideB + d * extent, sideA + d * extent, sideA);
        }
    }
}
