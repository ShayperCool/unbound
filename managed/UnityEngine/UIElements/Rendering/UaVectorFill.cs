using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>
    /// Fills a Painter2D path as Unity 6000.0.71f1 does (tests/projects/uitkpainter). The path
    /// becomes contours of boundary pieces (lines, and arcs split until each spans at most 60° and
    /// bulges less than a tenth of its chord), libtess2 triangulates them (constrained Delaunay,
    /// the fill rule), and each triangle is drawn by how many of its edges are boundary pieces it
    /// lies inside of: none, a plain triangle; one, two or three, a triangle per such edge (from
    /// the opposite corner, the midpoint of the third edge, or the centroid) whose edge fades out
    /// over a 2-unit strip, or whose arc is cut by its circle. An arc piece the triangle lies on the
    /// circle's side of adds a quad out to the arc. Triangles that two vertices map to the same
    /// boundary point (such as every triangle at the crossings of a self-intersecting star) or
    /// thinner than 0.001 are dropped; edges through libtess2's crossing vertices are not
    /// anti-aliased; the closing side of an open sub-path neither.
    /// </summary>
    internal sealed class UaVectorFill
    {
        const float MaxPieceSweep = 1.0471975803375244f;
        const float MaxBulge = 0.1f;

        /// <summary>A boundary piece: a line, or an arc (<see cref="Center"/>, <see cref="Radius"/>,
        /// <see cref="Pm"/> a point on it between its ends); a separator ends a contour.</summary>
        struct Piece
        {
            public Vector2 P0, Pm, P1, Center;
            public float Radius;
            public bool IsArc, Separator;
        }

        /// <summary>A triangle to draw: plain, or with one boundary edge (E0, E1) faded or cut
        /// toward Apex, or an arc piece's outer quad.</summary>
        struct Shape
        {
            public byte Kind;
            public Vector2 E0, E1, Apex;
            public int Piece;
        }

        const byte ShapePlain = 0, ShapeEdge = 1, ShapeOuterArc = 2;

        readonly List<Piece> m_Pieces = new List<Piece>();
        readonly List<UaCurvePiece> m_Curve = new List<UaCurvePiece>();
        readonly List<Vector2> m_Points = new List<Vector2>();
        readonly List<int> m_Counts = new List<int>();
        /// <summary>The boundary pieces each point belongs to (at most four, -1 unused).</summary>
        readonly List<Int4> m_PointPieces = new List<Int4>();
        readonly List<Shape> m_Shapes = new List<Shape>();
        readonly List<(int piece, int u, int w)> m_Matched = new List<(int, int, int)>();
        readonly List<int> m_Outer = new List<int>();

        float[] m_TessPoints = new float[64];
        int[] m_TessCounts = new int[8];
        float[] m_TessVertices = new float[128];
        int[] m_TessIndices = new int[64];
        int[] m_TessTriangles = new int[192];

        struct Int4
        {
            public int A, B, C, D;
            internal static readonly Int4 None = new Int4 { A = -1, B = -1, C = -1, D = -1 };

            internal void Add(int piece)
            {
                if (A == piece || B == piece || C == piece || D == piece) return;
                if (A < 0) A = piece;
                else if (B < 0) B = piece;
                else if (C < 0) C = piece;
                else if (D < 0) D = piece;
            }

            internal bool Contains(int piece) => piece >= 0 && (A == piece || B == piece || C == piece || D == piece);
        }

        UaVectorMesh m_Mesh;

        internal void Fill(UaVectorPath path, Color color, FillRule rule, UaVectorMesh mesh)
        {
            path.Terminate(true);
            m_Mesh = mesh;
            BuildPieces(path.Entries);
            BuildContours();
            if (m_Counts.Count > 0 && Triangulate(rule == FillRule.OddEven, out int vertexCount, out int triangleCount))
                Classify(vertexCount, triangleCount);
            else
                m_Shapes.Clear();
            var tint = UaVector.Tint(color);
            for (int i = 0; i < m_Shapes.Count; i++)
            {
                var s = m_Shapes[i];
                if (s.Kind == ShapePlain) Plain(s, tint);
                else if (s.Kind == ShapeOuterArc) OuterArc(m_Pieces[s.Piece], tint);
                else if (m_Pieces[s.Piece].IsArc) InnerArc(s, m_Pieces[s.Piece], tint);
                else FadedEdge(s, tint);
            }
            m_Mesh = null;
        }

        // ------------------------------------------------------------------ boundary pieces

        void BuildPieces(List<UaPathEntry> entries)
        {
            m_Pieces.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                switch (e.Kind)
                {
                    case UaPathKind.MoveTo:
                        if (m_Pieces.Count > 0 && !m_Pieces[m_Pieces.Count - 1].Separator) m_Pieces.Add(new Piece { Separator = true });
                        break;
                    case UaPathKind.Line:
                        m_Pieces.Add(new Piece { P0 = e.A, P1 = e.B });
                        m_Mesh.AddBounds(e.A);
                        m_Mesh.AddBounds(e.B);
                        break;
                    case UaPathKind.Arc:
                        ArcPieces(e.Arc);
                        break;
                    case UaPathKind.ArcTo:
                        if (UaArcTo.ToArc(e, out var arc)) ArcPieces(arc);
                        break;
                    case UaPathKind.Bezier:
                        CurvePieces(new UaCubic(e.A, e.B, e.C, e.D));
                        break;
                }
            }
            // An arc piece that bulges a tenth of its chord or more (or the only piece) is halved
            // once more, at its middle point.
            int total = m_Pieces.Count;
            for (int i = 0; i < m_Pieces.Count; i++)
            {
                var p = m_Pieces[i];
                if (!p.IsArc) continue;
                float chord = UaVector.Length(p.P1 - p.P0);
                float bulge = p.Radius - UaVector.Length((p.P0 + p.P1) * 0.5f - p.Center);
                if (total != 1 && bulge / chord < MaxBulge) continue;
                Halve(i);
                i++;
            }
        }

        /// <summary>Replaces arc piece <paramref name="i"/> by its halves at its middle point.</summary>
        void Halve(int i)
        {
            var p = m_Pieces[i];
            var first = p;
            first.Pm = OnCircle(p, (p.P0 + p.Pm) * 0.5f);
            first.P1 = p.Pm;
            var second = p;
            second.P0 = p.Pm;
            second.Pm = OnCircle(p, (p.Pm + p.P1) * 0.5f);
            m_Pieces[i] = first;
            m_Pieces.Insert(i + 1, second);
        }

        static Vector2 OnCircle(in Piece p, Vector2 q) => p.Center + UaVector.Normalized(q - p.Center) * p.Radius;

        void ArcPieces(in UaArc arc)
        {
            arc.Points(out var start, out var end);
            arc.AddBounds(m_Mesh, 0f, clockwise: true);
            float sweep = arc.Sweep();
            if (!(sweep > 0f)) return;
            int first = m_Pieces.Count;
            var u = UaVector.Normalized(start - arc.Center);
            SplitArc(arc, u, start, end, 0f, sweep);
            if (m_Pieces.Count - first == 1) Halve(first);
        }

        /// <summary>Halves the arc between <paramref name="from"/> and <paramref name="to"/>
        /// (angles from its start) until each piece spans at most 60°.</summary>
        void SplitArc(in UaArc arc, Vector2 u, Vector2 start, Vector2 end, float from, float to)
        {
            float sign = arc.Ccw ? -1f : 1f;
            if (MaxPieceSweep >= to - from)
            {
                m_Pieces.Add(new Piece
                {
                    P0 = from == 0f ? start : Rotate(arc, u, sign * from),
                    Pm = Rotate(arc, u, sign * (from + to) * 0.5f),
                    P1 = to >= arc.Sweep() ? end : Rotate(arc, u, sign * to),
                    Center = arc.Center,
                    Radius = arc.Radius,
                    IsArc = true,
                });
                return;
            }
            float mid = (from + to) * 0.5f;
            SplitArc(arc, u, start, end, from, mid);
            SplitArc(arc, u, start, end, mid, to);
        }

        static Vector2 Rotate(in UaArc arc, Vector2 u, float angle)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new Vector2(arc.Center.x + (u.x * c - u.y * s) * arc.Radius, arc.Center.y + (u.x * s + u.y * c) * arc.Radius);
        }

        void CurvePieces(in UaCubic curve)
        {
            UaCurveSubdivision.Subdivide(curve, m_Curve);
            for (int i = 0; i < m_Curve.Count; i++)
            {
                var c = m_Curve[i];
                bool arc = c.IsArc && c.Radius > 0f;
                m_Pieces.Add(new Piece { P0 = c.P0, Pm = c.Pm, P1 = c.P1, Center = c.Center, Radius = c.Radius, IsArc = arc });
            }
            m_Mesh.AddBounds(curve.P0);
            m_Mesh.AddBounds(curve.P3);
            curve.AddExtremeBounds(m_Mesh, 0f);
        }

        // ------------------------------------------------------------------ contours

        /// <summary>One contour per run of pieces: the first piece's start and every piece's end
        /// (the last one's only when it does not return to a start of the run), each point
        /// remembering its pieces; consecutive pieces that meet within 0.001 share their point.</summary>
        void BuildContours()
        {
            m_Points.Clear();
            m_Counts.Clear();
            m_PointPieces.Clear();
            int runStart = 0, contourStart = 0, last = -1;
            for (int i = 0; i <= m_Pieces.Count; i++)
            {
                bool end = i == m_Pieces.Count || m_Pieces[i].Separator;
                if (end)
                {
                    if (m_Points.Count > contourStart) m_Counts.Add(m_Points.Count - contourStart);
                    contourStart = m_Points.Count;
                    runStart = i + 1;
                    last = -1;
                    continue;
                }
                var p = m_Pieces[i];
                int start;
                if (last >= 0 && UaVector.Near(m_Points[last], p.P0)) start = last;
                else start = AddPoint(p.P0);
                AddPiece(start, i);
                bool closing = i + 1 == m_Pieces.Count || m_Pieces[i + 1].Separator;
                int endPoint = -1;
                if (closing)
                {
                    for (int k = runStart; k <= i && endPoint < 0; k++)
                        if (UaVector.Near(m_Pieces[k].P0, p.P1)) endPoint = FindPoint(contourStart, m_Pieces[k].P0);
                }
                if (endPoint < 0) endPoint = AddPoint(p.P1);
                AddPiece(endPoint, i);
                last = endPoint;
            }
        }

        int AddPoint(Vector2 p)
        {
            m_Points.Add(p);
            m_PointPieces.Add(Int4.None);
            return m_Points.Count - 1;
        }

        int FindPoint(int from, Vector2 p)
        {
            for (int i = from; i < m_Points.Count; i++)
                if (UaVector.Near(m_Points[i], p)) return i;
            return -1;
        }

        void AddPiece(int point, int piece)
        {
            var set = m_PointPieces[point];
            set.Add(piece);
            m_PointPieces[point] = set;
        }

        // ------------------------------------------------------------------ triangulation

        [DllImport("unbound_physx", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe int ua_tess_triangulate(float* points, int* counts, int contours, int oddEven,
            float* outVertices, int* outVertexIndices, int vertexCapacity, int* outTriangles, int triangleCapacity,
            int* outVertexCount, int* outTriangleCount);

        unsafe bool Triangulate(bool oddEven, out int vertexCount, out int triangleCount)
        {
            if (m_TessPoints.Length < m_Points.Count * 2) m_TessPoints = new float[m_Points.Count * 4];
            for (int i = 0; i < m_Points.Count; i++)
            {
                m_TessPoints[i * 2] = m_Points[i].x;
                m_TessPoints[i * 2 + 1] = m_Points[i].y;
            }
            if (m_TessCounts.Length < m_Counts.Count) m_TessCounts = new int[m_Counts.Count * 2];
            for (int i = 0; i < m_Counts.Count; i++) m_TessCounts[i] = m_Counts[i];
            while (true)
            {
                int status, vc, tc;
                fixed (float* points = m_TessPoints)
                fixed (int* counts = m_TessCounts)
                fixed (float* vertices = m_TessVertices)
                fixed (int* indices = m_TessIndices)
                fixed (int* triangles = m_TessTriangles)
                    status = ua_tess_triangulate(points, counts, m_Counts.Count, oddEven ? 1 : 0, vertices, indices,
                        m_TessIndices.Length, triangles, m_TessTriangles.Length / 3, &vc, &tc);
                vertexCount = vc;
                triangleCount = tc;
                if (status == 0) return true;
                if (status < 0) return false;
                if (vc > m_TessIndices.Length)
                {
                    m_TessIndices = new int[vc * 2];
                    m_TessVertices = new float[vc * 4];
                }
                if (tc * 3 > m_TessTriangles.Length) m_TessTriangles = new int[tc * 6];
            }
        }

        Vector2 TessVertex(int i) => new Vector2(m_TessVertices[i * 2], m_TessVertices[i * 2 + 1]);

        static float MinHeight(Vector2 a, Vector2 b, Vector2 c) =>
            Math.Min(Height(a, b, c), Math.Min(Height(b, c, a), Height(c, a, b)));

        static float Height(Vector2 p, Vector2 q, Vector2 r)
        {
            Vector2 d = q - p;
            float l = UaVector.Length(d);
            return l == 0f ? 0f : Math.Abs(d.x * (r.y - p.y) - d.y * (r.x - p.x)) / l;
        }

        void Classify(int vertexCount, int triangleCount)
        {
            m_Shapes.Clear();
            var map = m_TessIndices;
            RemapCrossings(vertexCount);
            var tris = m_TessTriangles;
            for (int t = 0; t < triangleCount; t++)
            {
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                Vector2 pa = TessVertex(a), pb = TessVertex(b), pc = TessVertex(c);
                if (MinHeight(pa, pb, pc) < UaVector.Epsilon) continue;
                int ia = map[a], ib = map[b], ic = map[c];
                if (ib == ic || ia == ib || ia == ic) continue;
                m_Matched.Clear();
                m_Outer.Clear();
                Match(a, b, c);
                Match(b, c, a);
                Match(c, a, b);
                switch (m_Matched.Count)
                {
                    case 0:
                        break;
                    case 1:
                    {
                        var (piece, u, w) = m_Matched[0];
                        int apex = u != a && w != a ? a : u != b && w != b ? b : c;
                        var pp = m_Pieces[piece];
                        m_Shapes.Add(new Shape { Kind = ShapeEdge, E0 = pp.P0, E1 = pp.P1, Apex = TessVertex(apex), Piece = piece });
                        break;
                    }
                    case 2:
                    {
                        var (k1, u1, w1) = m_Matched[0];
                        var (k2, u2, w2) = m_Matched[1];
                        int shared = u1 == u2 || u1 == w2 ? u1 : w1;
                        int o1 = u1 == shared ? w1 : u1, o2 = u2 == shared ? w2 : u2;
                        Vector2 ps = TessVertex(shared), p1 = TessVertex(o1), p2 = TessVertex(o2);
                        var mid = new Vector2((p1.x + p2.x) * 0.5f, (p1.y + p2.y) * 0.5f);
                        m_Shapes.Add(new Shape { Kind = ShapeEdge, E0 = ps, E1 = p1, Apex = mid, Piece = k1 });
                        m_Shapes.Add(new Shape { Kind = ShapeEdge, E0 = ps, E1 = p2, Apex = mid, Piece = k2 });
                        break;
                    }
                    default:
                    {
                        var g = new Vector2((pa.x + pb.x + pc.x) / 3f, (pa.y + pb.y + pc.y) / 3f);
                        m_Shapes.Add(new Shape { Kind = ShapeEdge, E0 = pa, E1 = pb, Apex = g, Piece = PieceOf(a, b) });
                        m_Shapes.Add(new Shape { Kind = ShapeEdge, E0 = pb, E1 = pc, Apex = g, Piece = PieceOf(b, c) });
                        m_Shapes.Add(new Shape { Kind = ShapeEdge, E0 = pa, E1 = pc, Apex = g, Piece = PieceOf(c, a) });
                        break;
                    }
                }
                for (int k = 0; k < m_Outer.Count; k++) m_Shapes.Add(new Shape { Kind = ShapeOuterArc, Piece = m_Outer[k] });
                if (m_Matched.Count == 0) m_Shapes.Add(new Shape { Kind = ShapePlain, E0 = pa, E1 = pb, Apex = pc });
            }
        }

        /// <summary>A vertex libtess2 made at a crossing takes the point of the first vertex (in
        /// output order, crossings already given one included) within 0.001 of it, if any. Vertices
        /// are found through a grid of 0.001 cells, so the cost stays linear.</summary>
        void RemapCrossings(int vertexCount)
        {
            var map = m_TessIndices;
            bool any = false;
            for (int j = 0; j < vertexCount && !any; j++) any = map[j] < 0;
            if (!any) return;
            m_Grid.Clear();
            if (m_GridNext.Length < vertexCount) m_GridNext = new int[vertexCount * 2];
            for (int k = vertexCount - 1; k >= 0; k--)
                if (map[k] >= 0) GridInsert(k);
            for (int j = 0; j < vertexCount; j++)
            {
                if (map[j] >= 0) continue;
                var v = TessVertex(j);
                long cx = Cell(v.x), cy = Cell(v.y);
                int best = -1;
                for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (!m_Grid.TryGetValue(Key(cx + dx, cy + dy), out int k)) continue;
                    for (; k >= 0; k = m_GridNext[k])
                        if (k != j && (best < 0 || k < best) && UaVector.Near(v, TessVertex(k))) best = k;
                }
                if (best < 0) continue;
                map[j] = map[best];
                GridInsert(j);
            }
        }

        static long Cell(float v) => (long)Math.Floor(v / (double)UaVector.Epsilon);

        static long Key(long x, long y) => (x << 32) ^ (y & 0xffffffffL);

        void GridInsert(int k)
        {
            var v = TessVertex(k);
            long key = Key(Cell(v.x), Cell(v.y));
            m_GridNext[k] = m_Grid.TryGetValue(key, out int head) ? head : -1;
            m_Grid[key] = k;
        }

        readonly Dictionary<long, int> m_Grid = new Dictionary<long, int>();
        int[] m_GridNext = new int[64];

        int PieceOf(int u, int w)
        {
            for (int i = 0; i < m_Matched.Count; i++)
            {
                var m = m_Matched[i];
                if ((m.u == u && m.w == w) || (m.u == w && m.w == u)) return m.piece;
            }
            return m_Matched[0].piece;
        }

        /// <summary>Records the boundary pieces edge (u, w) lies on. Arc pieces the triangle lies
        /// on the circle's side of get an outer quad instead.</summary>
        void Match(int u, int w, int apex)
        {
            int mu = m_TessIndices[u], mw = m_TessIndices[w];
            if (mu < 0 || mw < 0) return;
            Int4 su = m_PointPieces[mu], sw = m_PointPieces[mw];
            Span<int> pieces = stackalloc int[] { su.A, su.B, su.C, su.D };
            foreach (int k in pieces)
            {
                if (k < 0 || !sw.Contains(k)) continue;
                var p = m_Pieces[k];
                if (p.IsArc)
                {
                    var mid = new Vector2((p.P0.x + p.P1.x) * 0.5f, (p.P0.y + p.P1.y) * 0.5f);
                    var ap = TessVertex(apex);
                    float dot = (ap.x - mid.x) * (p.Center.x - mid.x) + (ap.y - mid.y) * (p.Center.y - mid.y);
                    if (!(0f >= dot))
                    {
                        m_Outer.Add(k);
                        continue;
                    }
                }
                m_Matched.Add((k, u, w));
            }
        }

        // ------------------------------------------------------------------ shapes

        void Plain(in Shape s, Color32 tint)
        {
            if (UaVector.Near(s.E0, s.E1) || UaVector.Near(s.E0, s.Apex) || UaVector.Near(s.E1, s.Apex)) return;
            int i = m_Mesh.Add(s.E0, tint, Vector4.zero, arc: false);
            m_Mesh.Add(s.E1, tint, Vector4.zero, arc: false);
            m_Mesh.Add(s.Apex, tint, Vector4.zero, arc: false);
            m_Mesh.TriangleCW(i, i + 1, i + 2);
        }

        /// <summary>A straight boundary edge: the triangle to <see cref="Shape.Apex"/>, fading out at
        /// the edge, and a strip of <see cref="UaVector.EdgeBuffer"/> outside it.</summary>
        void FadedEdge(in Shape s, Color32 tint)
        {
            var edge = new Vector4(0f, 1f, UaVector.UnusedArc, UaVector.UnusedArc);
            int i = m_Mesh.Add(s.E0, tint, edge);
            m_Mesh.Add(s.E1, tint, edge);
            m_Mesh.Add(s.Apex, tint, new Vector4(0f, 0f, UaVector.UnusedArc, UaVector.UnusedArc));
            m_Mesh.TriangleCW(i, i + 1, i + 2);
            Vector2 d = s.E1 - s.E0;
            float l = UaVector.Length(d);
            var n = new Vector2(d.y / l, -d.x / l);
            if (n.x * (s.Apex.x - s.E0.x) + n.y * (s.Apex.y - s.E0.y) > 0f) n = -n;
            var outside = new Vector4(1f, 2f, UaVector.UnusedArc, UaVector.UnusedArc);
            int j = m_Mesh.Add(s.E0, tint, edge);
            m_Mesh.Add(s.E1, tint, edge);
            m_Mesh.Add(s.E0 + n * UaVector.EdgeBuffer, tint, outside);
            m_Mesh.Add(s.E1 + n * UaVector.EdgeBuffer, tint, outside);
            m_Mesh.TriangleCW(j, j + 1, j + 2);
            m_Mesh.TriangleCW(j + 1, j + 2, j + 3);
        }

        /// <summary>A boundary arc the triangle lies outside the circle of: the triangle, its
        /// coverage cut by the circle (inside it is not covered).</summary>
        void InnerArc(in Shape s, in Piece p, Color32 tint)
        {
            if (UaVector.Epsilon > p.Radius) return;
            float inv = 1f / p.Radius;
            int i = m_Mesh.Add(s.E0, tint, OutsideCircle(s.E0, p.Center, inv));
            m_Mesh.Add(s.E1, tint, OutsideCircle(s.E1, p.Center, inv));
            m_Mesh.Add(s.Apex, tint, OutsideCircle(s.Apex, p.Center, inv));
            m_Mesh.TriangleCW(i, i + 1, i + 2);
        }

        static Vector4 OutsideCircle(Vector2 v, Vector2 c, float inv) =>
            new Vector4(UaVector.UnusedArc, UaVector.UnusedArc, (v.x - c.x) * inv, (v.y - c.y) * inv);

        /// <summary>The part of a boundary arc beyond its chord: a quad from the chord out past the
        /// arc's bulge plus <see cref="UaVector.EdgeBuffer"/>, covered inside the circle.</summary>
        void OuterArc(in Piece p, Color32 tint)
        {
            Vector2 p0 = p.P0, p1 = p.P1, c = p.Center;
            float r = p.Radius;
            var mid = new Vector2((p0.x + p1.x) * 0.5f, (p0.y + p1.y) * 0.5f);
            var dm = mid - c;
            float bulge = r - UaVector.Length(dm);
            Vector2 d = p1 - p0;
            float l = UaVector.Length(d);
            var n = new Vector2(d.y / l, -d.x / l);
            if (n.x * dm.x + n.y * dm.y < 0f) n = -n;
            float k = bulge + UaVector.EdgeBuffer;
            Vector2 o0 = p0 + n * k, o1 = p1 + n * k;
            int i = m_Mesh.Add(p0, tint, InsideCircle(p0, c, r));
            m_Mesh.Add(p1, tint, InsideCircle(p1, c, r));
            m_Mesh.Add(o1, tint, InsideCircle(o1, c, r));
            m_Mesh.Add(o0, tint, InsideCircle(o0, c, r));
            m_Mesh.TriangleCW(i, i + 1, i + 2);
            m_Mesh.TriangleCW(i + 2, i, i + 3);
        }

        static Vector4 InsideCircle(Vector2 v, Vector2 c, float r) =>
            new Vector4((v.x - c.x) / r, (v.y - c.y) / r, UaVector.UnusedArc, UaVector.UnusedArc);
    }
}
