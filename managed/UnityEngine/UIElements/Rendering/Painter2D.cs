using System;
using System.Collections.Generic;
using Unbound.UIElements;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Object to draw 2D vector graphics (Unity 6000.0 Scripting API, Painter2D): paths of lines,
    /// arcs and Bézier curves, filled with <see cref="fillColor"/> by a fill rule or stroked with
    /// <see cref="lineWidth"/>, <see cref="strokeColor"/>, <see cref="lineJoin"/>,
    /// <see cref="lineCap"/> and <see cref="miterLimit"/>. A painter from
    /// <see cref="MeshGenerationContext.painter2D"/> draws into the element's content, in its local
    /// space, as meshes allocated from that context; a detached painter keeps what it draws.
    /// </summary>
    /// <remarks>
    /// What the Scripting API leaves unspecified is Unbound's choice (unmeasured): curves become
    /// polylines within <see cref="UaFlatteningTolerance"/> of the true curve, fills are split into
    /// trapezoids by a sweep that keeps each one open while the same two edges bound it (exact
    /// coverage of the fill rule, output proportional to the edges and their crossings),
    /// strokes are the union of segment rectangles, joins and caps filled with the non-zero rule (no
    /// overlap is drawn twice), edges are not anti-aliased, and each generateVisualContent call
    /// starts with an empty path and the default state below. <see cref="SaveToVectorImage"/> stays
    /// a generated stub. So does <see cref="strokeGradient"/>, a loud stub: the 6000.0 page says a
    /// stroke gradient overrides <see cref="strokeColor"/> and a null one falls back on it, but not
    /// how the gradient maps along the stroke, so Stroke always uses strokeColor.
    /// </remarks>
    public partial class Painter2D : IDisposable
    {
        /// <summary>The largest distance, in local units, between a curve or arc and the polyline
        /// that replaces it (Unbound's choice, unmeasured).</summary>
        internal const float UaFlatteningTolerance = 0.05f;

        readonly MeshGenerationContext m_UaContext;
        readonly bool m_UaIsDetached;
        readonly UaPainter2DPath m_UaPath = new UaPainter2DPath();
        readonly UaPainter2DMesh m_UaScratch = new UaPainter2DMesh();
        // A detached painter's content: the quads Fill and Stroke drew, and their colors.
        readonly UaPainter2DMesh m_UaDetachedContent;
        readonly List<Color32> m_UaDetachedTints;

        float m_UaLineWidth;
        Color m_UaStrokeColor;
        Color m_UaFillColor;
        LineJoin m_UaLineJoin;
        LineCap m_UaLineCap;
        float m_UaMiterLimit;

        internal Painter2D(MeshGenerationContext ctx)
        {
            m_UaContext = ctx;
            UaResetState();
        }

        /// <summary>A detached painter: what it draws is kept (for SaveToVectorImage, still a stub)
        /// until <see cref="Clear"/>.</summary>
        public Painter2D()
        {
            m_UaIsDetached = true;
            m_UaDetachedContent = new UaPainter2DMesh();
            m_UaDetachedTints = new List<Color32>();
            UaResetState();
        }

        // Unbound's choice (unmeasured): the defaults of HTML canvas 2D, whose path model the
        // Scripting API's members follow.
        void UaResetState()
        {
            m_UaLineWidth = 1f;
            m_UaStrokeColor = Color.black;
            m_UaFillColor = Color.black;
            m_UaLineJoin = LineJoin.Miter;
            m_UaLineCap = LineCap.Butt;
            m_UaMiterLimit = 10f;
        }

        /// <summary>Starts an element's generateVisualContent call: an empty path and the default
        /// state (Unbound's choice, unmeasured).</summary>
        internal void UaResetForElement()
        {
            m_UaPath.Clear();
            UaResetState();
        }

        public float lineWidth { get => m_UaLineWidth; set => m_UaLineWidth = value; }

        public Color strokeColor { get => m_UaStrokeColor; set => m_UaStrokeColor = value; }

        public Color fillColor { get => m_UaFillColor; set => m_UaFillColor = value; }

        public LineJoin lineJoin { get => m_UaLineJoin; set => m_UaLineJoin = value; }

        public LineCap lineCap { get => m_UaLineCap; set => m_UaLineCap = value; }

        public float miterLimit { get => m_UaMiterLimit; set => m_UaMiterLimit = value; }

        /// <summary>When created as a detached painter, clears the current content. Does nothing
        /// otherwise.</summary>
        public void Clear()
        {
            if (!m_UaIsDetached) return;
            m_UaDetachedContent.Clear();
            m_UaDetachedTints.Clear();
        }

        /// <summary>Releases the painter's content. Unbound holds no unmanaged resources for it; the
        /// painter stays usable (Unbound's choice, unmeasured).</summary>
        public void Dispose()
        {
            Clear();
            m_UaPath.Clear();
        }

        /// <summary>Begins a new path and empties the list of recorded sub-paths.</summary>
        public void BeginPath() => m_UaPath.Clear();

        /// <summary>Closes the current sub-path with a straight line; does nothing if it is already
        /// closed.</summary>
        public void ClosePath() => m_UaPath.ClosePath();

        /// <summary>Begins a new sub-path at <paramref name="pos"/>.</summary>
        public void MoveTo(Vector2 pos) => m_UaPath.MoveTo(pos);

        /// <summary>Adds a straight line to <paramref name="pos"/> (without a current point it
        /// starts a sub-path there, as HTML canvas does: Unbound's choice).</summary>
        public void LineTo(Vector2 pos) => m_UaPath.LineTo(pos);

        /// <summary>Adds an arc tangent to the lines from the current point to
        /// <paramref name="p1"/> and from <paramref name="p1"/> to <paramref name="p2"/>, preceded by
        /// a line to its first tangent point (HTML canvas arcTo geometry: Unbound's choice,
        /// unmeasured).</summary>
        public void ArcTo(Vector2 p1, Vector2 p2, float radius) => m_UaPath.ArcTo(p1, p2, radius, UaFlatteningTolerance);

        /// <summary>Adds an arc of <paramref name="radius"/> around <paramref name="center"/> from
        /// <paramref name="startAngle"/> to <paramref name="endAngle"/>, preceded by a line from the
        /// current point to its start. Angles grow clockwise on screen (UI Toolkit's y axis points
        /// down), so <see cref="ArcDirection.Clockwise"/> goes the way angles grow.</summary>
        public void Arc(Vector2 center, float radius, Angle startAngle, Angle endAngle, ArcDirection direction = ArcDirection.Clockwise) =>
            m_UaPath.Arc(center, radius, startAngle.ToRadians(), endAngle.ToRadians(), direction, UaFlatteningTolerance);

        /// <summary>Adds a cubic Bézier curve to <paramref name="p3"/> with control points
        /// <paramref name="p1"/> and <paramref name="p2"/>.</summary>
        public void BezierCurveTo(Vector2 p1, Vector2 p2, Vector2 p3) => m_UaPath.CubicTo(p1, p2, p3, UaFlatteningTolerance);

        /// <summary>Adds a quadratic Bézier curve to <paramref name="p2"/> with control point
        /// <paramref name="p1"/>.</summary>
        public void QuadraticCurveTo(Vector2 p1, Vector2 p2) => m_UaPath.QuadraticTo(p1, p2, UaFlatteningTolerance);

        /// <summary>Strokes the current path with <see cref="lineWidth"/>, <see cref="strokeColor"/>,
        /// <see cref="lineJoin"/>, <see cref="lineCap"/> and <see cref="miterLimit"/>.</summary>
        public void Stroke()
        {
            m_UaScratch.Clear();
            if (!(m_UaLineWidth > 0f) || float.IsInfinity(m_UaLineWidth)) return;
            var pieces = UaPainter2DStroker.Outline(m_UaPath, m_UaLineWidth * 0.5f, m_UaLineJoin, m_UaLineCap, m_UaMiterLimit, UaFlatteningTolerance);
            UaPainter2DTessellator.Fill(pieces, FillRule.NonZero, m_UaScratch);
            UaEmit(m_UaScratch, m_UaStrokeColor);
        }

        /// <summary>Fills the current path (each sub-path closed) with <see cref="fillColor"/> by
        /// <paramref name="fillRule"/>.</summary>
        public void Fill(FillRule fillRule = FillRule.NonZero)
        {
            m_UaScratch.Clear();
            UaPainter2DTessellator.Fill(m_UaPath.Polygons(), fillRule, m_UaScratch);
            UaEmit(m_UaScratch, m_UaFillColor);
        }

        void UaEmit(UaPainter2DMesh mesh, Color color)
        {
            int quads = mesh.QuadCount;
            if (quads == 0) return;
            Color32 tint = color;
            // Unbound's choice: fully transparent geometry is not emitted (the panel painter skips
            // transparent rectangles the same way).
            if (tint.a == 0) return;
            if (m_UaIsDetached)
            {
                m_UaDetachedContent.Append(mesh);
                for (int q = 0; q < quads; q++) m_UaDetachedTints.Add(tint);
                return;
            }
            const int MaxQuads = UaPainter.MaxVerticesPerMesh / 4;
            var corners = mesh.Corners;
            for (int start = 0; start < quads; start += MaxQuads)
            {
                int n = Math.Min(MaxQuads, quads - start);
                var data = m_UaContext.Allocate(n * 4, n * 6);
                for (int q = 0; q < n; q++)
                {
                    int c = (start + q) * 4;
                    for (int k = 0; k < 4; k++)
                    {
                        var p = corners[c + k];
                        data.SetNextVertex(new Vertex { position = new Vector3(p.x, p.y, Vertex.nearZ), tint = tint });
                    }
                }
                // Each trapezoid: top-left, top-right, bottom-right, bottom-left; two triangles,
                // clockwise on screen.
                for (int q = 0; q < n; q++)
                {
                    int b = q * 4;
                    data.SetNextIndex((ushort)b);
                    data.SetNextIndex((ushort)(b + 1));
                    data.SetNextIndex((ushort)(b + 2));
                    data.SetNextIndex((ushort)(b + 2));
                    data.SetNextIndex((ushort)(b + 3));
                    data.SetNextIndex((ushort)b);
                }
            }
        }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Trapezoids (four corners each: top-left, top-right, bottom-right, bottom-left) that
    /// a Painter2D fill or stroke covers.</summary>
    internal sealed class UaPainter2DMesh
    {
        internal readonly List<Vector2> Corners = new List<Vector2>();

        internal int QuadCount => Corners.Count / 4;

        internal void Clear() => Corners.Clear();

        internal void Append(UaPainter2DMesh other) => Corners.AddRange(other.Corners);

        internal void AddTrapezoid(double ya, double xla, double xra, double yb, double xlb, double xrb)
        {
            if ((xra - xla) + (xrb - xlb) <= 1e-9) return;
            Corners.Add(new Vector2((float)xla, (float)ya));
            Corners.Add(new Vector2((float)xra, (float)ya));
            Corners.Add(new Vector2((float)xrb, (float)yb));
            Corners.Add(new Vector2((float)xlb, (float)yb));
        }
    }

    /// <summary>A Painter2D path: sub-paths of points (curves and arcs already flattened), each open
    /// or closed. Without a current point, LineTo and the curves start a sub-path; after ClosePath
    /// the next segment starts a new sub-path at the closed one's start (HTML canvas rules:
    /// Unbound's choice where the Scripting API is silent).</summary>
    internal sealed class UaPainter2DPath
    {
        internal sealed class SubPath
        {
            internal readonly List<Vector2> Points = new List<Vector2>();
            internal bool Closed;
        }

        /// <summary>The most segments one curve or arc becomes.</summary>
        internal const int MaxCurveSegments = 4096;

        internal readonly List<SubPath> SubPaths = new List<SubPath>();
        SubPath m_Current;

        internal void Clear()
        {
            SubPaths.Clear();
            m_Current = null;
        }

        Vector2 CurrentPoint => m_Current.Closed ? m_Current.Points[0] : m_Current.Points[m_Current.Points.Count - 1];

        internal void MoveTo(Vector2 p)
        {
            m_Current = new SubPath();
            m_Current.Points.Add(p);
            SubPaths.Add(m_Current);
        }

        void EnsureOpen()
        {
            if (m_Current.Closed) MoveTo(m_Current.Points[0]);
        }

        internal void LineTo(Vector2 p)
        {
            if (m_Current == null)
            {
                MoveTo(p);
                return;
            }
            EnsureOpen();
            m_Current.Points.Add(p);
        }

        internal void ClosePath()
        {
            if (m_Current == null || m_Current.Closed) return;
            m_Current.Closed = true;
        }

        internal void QuadraticTo(Vector2 c, Vector2 p, float tolerance)
        {
            if (m_Current == null) MoveTo(c);
            EnsureOpen();
            var p0 = CurrentPoint;
            int n = UaPainter2DGeometry.QuadraticSegments(p0, c, p, tolerance);
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                m_Current.Points.Add(u * u * p0 + 2f * u * t * c + t * t * p);
            }
        }

        internal void CubicTo(Vector2 c1, Vector2 c2, Vector2 p, float tolerance)
        {
            if (m_Current == null) MoveTo(c1);
            EnsureOpen();
            var p0 = CurrentPoint;
            int n = UaPainter2DGeometry.CubicSegments(p0, c1, c2, p, tolerance);
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                m_Current.Points.Add(u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p);
            }
        }

        internal void Arc(Vector2 center, float radius, float a0, float a1, ArcDirection direction, float tolerance)
        {
            // Unbound's choice (unmeasured): a negative or NaN radius draws as radius 0.
            if (!(radius >= 0f)) radius = 0f;
            float sweep = UaPainter2DGeometry.Sweep(a0, a1, direction);
            var start = center + radius * new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            LineTo(start);
            AppendArc(center, radius, a0, sweep, tolerance);
        }

        internal void ArcTo(Vector2 p1, Vector2 p2, float radius, float tolerance)
        {
            if (m_Current == null) MoveTo(p1);
            EnsureOpen();
            var p0 = CurrentPoint;
            if (!(radius > 0f) || (p0 - p1).sqrMagnitude < 1e-12f || (p2 - p1).sqrMagnitude < 1e-12f)
            {
                LineTo(p1);
                return;
            }
            var d0 = (p0 - p1).normalized;
            var d2 = (p2 - p1).normalized;
            float cross = d0.x * d2.y - d0.y * d2.x;
            if (Mathf.Abs(cross) < 1e-6f)
            {
                LineTo(p1);
                return;
            }
            float phi = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d0, d2), -1f, 1f));
            float along = radius / Mathf.Tan(phi * 0.5f);
            var t0 = p1 + d0 * along;
            var t2 = p1 + d2 * along;
            var center = p1 + (d0 + d2).normalized * (radius / Mathf.Sin(phi * 0.5f));
            LineTo(t0);
            float a0 = Mathf.Atan2(t0.y - center.y, t0.x - center.x);
            float a1 = Mathf.Atan2(t2.y - center.y, t2.x - center.x);
            float sweep = a1 - a0;
            while (sweep > Mathf.PI) sweep -= 2f * Mathf.PI;
            while (sweep < -Mathf.PI) sweep += 2f * Mathf.PI;
            AppendArc(center, radius, a0, sweep, tolerance);
        }

        void AppendArc(Vector2 center, float radius, float a0, float sweep, float tolerance)
        {
            if (radius <= 0f || sweep == 0f) return;
            int n = UaPainter2DGeometry.ArcSegments(radius, Mathf.Abs(sweep), tolerance);
            for (int i = 1; i <= n; i++)
            {
                float a = a0 + sweep * i / n;
                m_Current.Points.Add(center + radius * new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
        }

        /// <summary>The sub-paths as polygons to fill (each closed implicitly).</summary>
        internal List<List<Vector2>> Polygons()
        {
            var polygons = new List<List<Vector2>>(SubPaths.Count);
            foreach (var s in SubPaths)
                if (s.Points.Count >= 3) polygons.Add(s.Points);
            return polygons;
        }
    }

    /// <summary>Flattening counts for Painter2D curves (Unbound's choice, unmeasured).</summary>
    internal static class UaPainter2DGeometry
    {
        /// <summary>The signed sweep of an arc from <paramref name="a0"/> to <paramref name="a1"/>
        /// (radians): a whole turn when the angles are a turn or more apart in the arc's direction,
        /// otherwise the difference brought into [0, 2π) that way (HTML canvas rules: Unbound's
        /// choice). Positive is clockwise on screen.</summary>
        internal static float Sweep(float a0, float a1, ArcDirection direction)
        {
            const float TwoPi = 2f * Mathf.PI;
            bool ccw = direction == ArcDirection.CounterClockwise;
            float d = ccw ? a0 - a1 : a1 - a0;
            if (d >= TwoPi) d = TwoPi;
            else
            {
                d %= TwoPi;
                if (d < 0f) d += TwoPi;
            }
            return ccw ? -d : d;
        }

        /// <summary>Segments for an arc so each chord's sagitta is at most
        /// <paramref name="tolerance"/>.</summary>
        internal static int ArcSegments(float radius, float sweep, float tolerance)
        {
            if (!(radius > 0f) || !(sweep > 0f)) return 1;
            double c = 1.0 - tolerance / (double)radius;
            double step = c <= -1.0 ? 2.0 * Math.PI : 2.0 * Math.Acos(c);
            return Clamp(Math.Ceiling(sweep / step));
        }

        /// <summary>Uniform segments for a quadratic Bézier within <paramref name="tolerance"/>
        /// (Wang's bound: n² ≥ d(d−1)·M / (8·tolerance), d = 2, M the second difference).</summary>
        internal static int QuadraticSegments(Vector2 p0, Vector2 p1, Vector2 p2, float tolerance)
        {
            double m = (p0 - 2f * p1 + p2).magnitude;
            return Clamp(Math.Ceiling(Math.Sqrt(m / (4.0 * tolerance))));
        }

        /// <summary>Uniform segments for a cubic Bézier within <paramref name="tolerance"/> (Wang's
        /// bound with d = 3: n² ≥ 0.75·M / tolerance, M the largest second difference).</summary>
        internal static int CubicSegments(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance)
        {
            double m = Math.Max((p0 - 2f * p1 + p2).magnitude, (p1 - 2f * p2 + p3).magnitude);
            return Clamp(Math.Ceiling(Math.Sqrt(0.75 * m / tolerance)));
        }

        static int Clamp(double n)
        {
            if (!(n >= 1.0)) return 1;
            return n > UaPainter2DPath.MaxCurveSegments ? UaPainter2DPath.MaxCurveSegments : (int)n;
        }
    }

    /// <summary>
    /// Fills polygons by a fill rule (Unbound's choice of method, unmeasured): a sweep down the
    /// plane in horizontal slabs between vertex heights, each cut short at the first height where
    /// two edges that are neighbours in x order cross, so no edges cross inside a slab and the
    /// spans the rule covers lie between neighbouring edges. A trapezoid stays open from slab to
    /// slab while the same pair of edges bounds a covered span and is emitted when that pair stops
    /// covering one, so the output grows with the edges and their crossings, not with
    /// slabs × spans. The trapezoids' union is exactly the covered region (in double precision),
    /// and they do not overlap.
    /// </summary>
    internal static class UaPainter2DTessellator
    {
        // Slabs at most this tall are skipped, and crossings this close to a slab's ends or
        // inversions this small in x are not split at.
        const double Epsilon = 1e-9;

        struct Edge
        {
            public double x0, y0, x1, y1;
            public int winding;

            public double XAt(double y) => y <= y0 ? x0 : y >= y1 ? x1 : x0 + (x1 - x0) * (y - y0) / (y1 - y0);
        }

        // An active edge at a slab's middle height.
        struct SlabEdge
        {
            public double xm;
            public int edge;
        }

        static readonly Comparison<Edge> ByTop = (l, r) => l.y0.CompareTo(r.y0);

        static readonly Comparison<SlabEdge> ByX = (l, r) =>
        {
            int c = l.xm.CompareTo(r.xm);
            return c != 0 ? c : l.edge.CompareTo(r.edge);
        };

        internal static void Fill(List<List<Vector2>> polygons, FillRule rule, UaPainter2DMesh into)
        {
            var edges = new List<Edge>();
            foreach (var poly in polygons)
            {
                int n = poly.Count;
                if (n < 3 || !Finite(poly)) continue;
                for (int i = 0; i < n; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % n];
                    if (a.y == b.y) continue;
                    edges.Add(a.y < b.y
                        ? new Edge { x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y, winding = 1 }
                        : new Edge { x0 = b.x, y0 = b.y, x1 = a.x, y1 = a.y, winding = -1 });
                }
            }
            int count = edges.Count;
            if (count == 0) return;
            edges.Sort(ByTop);

            var ys = new double[count * 2];
            for (int i = 0; i < count; i++)
            {
                ys[2 * i] = edges[i].y0;
                ys[2 * i + 1] = edges[i].y1;
            }
            Array.Sort(ys);

            // Per edge as the left side of an open trapezoid: its right edge (-1: none open), the
            // height it opened at and the last slab whose spans included it.
            var openRight = new int[count];
            for (int i = 0; i < count; i++) openRight[i] = -1;
            var openTop = new double[count];
            var seen = new int[count];
            var open = new List<int>();
            var active = new List<int>();
            var order = new List<SlabEdge>();
            int next = 0, stamp = 0;
            double ya = ys[0];
            for (int k = 1; k < ys.Length; k++)
            {
                double yv = ys[k];
                if (yv - ya <= Epsilon) continue;
                // No edge starts or ends between ya and yv: the active edges are those spanning
                // the middle.
                double mid = (ya + yv) * 0.5;
                while (next < count && edges[next].y0 < mid) active.Add(next++);
                int kept = 0;
                for (int i = 0; i < active.Count; i++)
                    if (edges[active[i]].y1 > mid) active[kept++] = active[i];
                active.RemoveRange(kept, active.Count - kept);

                while (ya < yv)
                {
                    double yb = SlabEnd(edges, active, order, ya, yv);
                    stamp++;
                    int winding = 0, left = -1;
                    foreach (var s in order)
                    {
                        bool was = Inside(winding, rule);
                        winding += edges[s.edge].winding;
                        bool now = Inside(winding, rule);
                        if (!was && now)
                        {
                            left = s.edge;
                        }
                        else if (was && !now)
                        {
                            if (openRight[left] != s.edge)
                            {
                                if (openRight[left] >= 0) Close(edges, openRight, openTop, left, ya, into);
                                else open.Add(left);
                                openRight[left] = s.edge;
                                openTop[left] = ya;
                            }
                            seen[left] = stamp;
                        }
                    }
                    int still = 0;
                    for (int i = 0; i < open.Count; i++)
                    {
                        int l = open[i];
                        if (seen[l] == stamp) open[still++] = l;
                        else Close(edges, openRight, openTop, l, ya, into);
                    }
                    open.RemoveRange(still, open.Count - still);
                    ya = yb;
                }
            }
            foreach (int l in open) Close(edges, openRight, openTop, l, ya, into);
        }

        /// <summary>The end of the slab from <paramref name="ya"/> (at most <paramref name="yb"/>)
        /// in which no two active edges cross, with <paramref name="order"/> the active edges in x
        /// order inside it. Neighbours in that order that are not inverted at either end keep it
        /// over the whole slab (edges are straight), so only neighbours are checked; the slab ends
        /// at their first crossing.</summary>
        static double SlabEnd(List<Edge> edges, List<int> active, List<SlabEdge> order, double ya, double yb)
        {
            while (true)
            {
                double ym = (ya + yb) * 0.5;
                order.Clear();
                foreach (int i in active) order.Add(new SlabEdge { xm = edges[i].XAt(ym), edge = i });
                order.Sort(ByX);
                double end = yb;
                for (int k = 0; k + 1 < order.Count; k++)
                {
                    var a = edges[order[k].edge];
                    var b = edges[order[k + 1].edge];
                    double dm = order[k + 1].xm - order[k].xm;
                    double da = b.XAt(ya) - a.XAt(ya);
                    double db = b.XAt(yb) - a.XAt(yb);
                    double y;
                    if (da < -Epsilon) y = ya + (ym - ya) * -da / (dm - da);
                    else if (db < -Epsilon) y = ym + (yb - ym) * dm / (dm - db);
                    else continue;
                    if (y > ya + Epsilon && y < yb - Epsilon && y < end) end = y;
                }
                if (end >= yb) return yb;
                yb = end;
            }
        }

        static void Close(List<Edge> edges, int[] openRight, double[] openTop, int left, double y, UaPainter2DMesh into)
        {
            var l = edges[left];
            var r = edges[openRight[left]];
            double top = openTop[left];
            into.AddTrapezoid(top, l.XAt(top), r.XAt(top), y, l.XAt(y), r.XAt(y));
            openRight[left] = -1;
        }

        static bool Inside(int winding, FillRule rule) => rule == FillRule.OddEven ? (winding & 1) != 0 : winding != 0;

        static bool Finite(List<Vector2> poly)
        {
            foreach (var p in poly)
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y)) return false;
            return true;
        }
    }

    /// <summary>
    /// The outline of a stroked Painter2D path as convex pieces of one orientation, whose non-zero
    /// union is the stroke (Unbound's choice, unmeasured): a rectangle per segment, then per join a
    /// bevel triangle, a miter quad (when its length over the line width is at most the miter
    /// limit, else a bevel) or a disk, and for open sub-paths round caps as disks (butt caps add
    /// nothing). Repeated points are dropped; sub-paths of a single point draw nothing. Curves are
    /// stroked as their flattened chords, so a butt end on a curve is cut square to its last chord,
    /// not to the curve's tangent: an open full-turn Arc (no ClosePath) leaves a thin wedge
    /// uncovered on the outer side where its ends meet, at angle 0 (Unbound's choice, unmeasured).
    /// </summary>
    internal static class UaPainter2DStroker
    {
        internal static List<List<Vector2>> Outline(UaPainter2DPath path, float halfWidth, LineJoin join, LineCap cap, float miterLimit, float tolerance)
        {
            var pieces = new List<List<Vector2>>();
            var pts = new List<Vector2>();
            foreach (var sub in path.SubPaths)
            {
                pts.Clear();
                foreach (var p in sub.Points)
                    if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude > 1e-12f) pts.Add(p);
                bool closed = sub.Closed;
                if (closed && pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude <= 1e-12f) pts.RemoveAt(pts.Count - 1);
                int n = pts.Count;
                if (n < 2) continue;
                int segments = closed ? n : n - 1;
                for (int s = 0; s < segments; s++) pieces.Add(Segment(pts[s], pts[(s + 1) % n], halfWidth));
                int firstJoin = closed ? 0 : 1, lastJoin = closed ? n - 1 : n - 2;
                for (int i = firstJoin; i <= lastJoin; i++)
                    Join(pieces, pts[(i - 1 + n) % n], pts[i], pts[(i + 1) % n], halfWidth, join, miterLimit, tolerance);
                if (!closed && cap == LineCap.Round)
                {
                    pieces.Add(Disk(pts[0], halfWidth, tolerance));
                    pieces.Add(Disk(pts[n - 1], halfWidth, tolerance));
                }
            }
            foreach (var piece in pieces)
                if (SignedArea(piece) < 0f) piece.Reverse();
            return pieces;
        }

        static Vector2 Perp(Vector2 d) => new Vector2(-d.y, d.x);

        static List<Vector2> Segment(Vector2 a, Vector2 b, float hw)
        {
            var n = Perp((b - a).normalized) * hw;
            return new List<Vector2> { a + n, b + n, b - n, a - n };
        }

        static void Join(List<List<Vector2>> pieces, Vector2 prev, Vector2 v, Vector2 next, float hw, LineJoin join, float miterLimit, float tolerance)
        {
            if (join == LineJoin.Round)
            {
                pieces.Add(Disk(v, hw, tolerance));
                return;
            }
            var d0 = (v - prev).normalized;
            var d1 = (next - v).normalized;
            float cross = d0.x * d1.y - d0.y * d1.x;
            float dot = Vector2.Dot(d0, d1);
            if (Mathf.Abs(cross) < 1e-6f && dot > 0f) return;
            // The outer side: where the two segments' offset edges part.
            float side = cross > 0f ? -1f : 1f;
            var oa = v + Perp(d0) * (hw * side);
            var ob = v + Perp(d1) * (hw * side);
            if (join == LineJoin.Miter)
            {
                // Miter length over line width = 1 / sin(θ/2), θ the angle between the segments.
                float sinHalf = Mathf.Sqrt(Mathf.Max(0f, (1f + dot) * 0.5f));
                if (sinHalf > 1e-6f && 1f / sinHalf <= miterLimit)
                {
                    var tip = v + ((oa - v) + (ob - v)).normalized * (hw / sinHalf);
                    pieces.Add(new List<Vector2> { v, oa, tip, ob });
                    return;
                }
            }
            pieces.Add(new List<Vector2> { v, oa, ob });
        }

        /// <summary>A disk as a regular polygon inscribed in it (a multiple of four sides, one
        /// vertex at angle 0, so its axis extremes are exact).</summary>
        static List<Vector2> Disk(Vector2 c, float r, float tolerance)
        {
            int n = Math.Max(8, UaPainter2DGeometry.ArcSegments(r, 2f * Mathf.PI, tolerance));
            n = (n + 3) / 4 * 4;
            var poly = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                poly.Add(c + r * new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            return poly;
        }

        static float SignedArea(List<Vector2> poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }
    }
}
