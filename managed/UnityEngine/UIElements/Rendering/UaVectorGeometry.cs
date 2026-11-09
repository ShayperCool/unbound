using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.UIElements
{
    /// <summary>A vertex of vector (Painter2D) geometry: local position, tint, and UIR's arc
    /// anti-aliasing data (<see cref="circle"/>: the outer circle in xy, inside of which is covered,
    /// the inner circle in zw, outside of which is covered; <see cref="arc"/> asks the shader to
    /// evaluate them).</summary>
    internal struct UaVectorVertex
    {
        public Vector2 position;
        public Color32 tint;
        public Vector4 circle;
        public bool arc;
    }

    /// <summary>Vector geometry as Painter2D tessellates it: vertices, triangle indices and the
    /// bounding box of the drawn shapes (without their anti-aliasing margins). Reused between
    /// calls; nothing is allocated once its arrays are large enough.</summary>
    internal sealed class UaVectorMesh
    {
        internal UaVectorVertex[] Vertices = new UaVectorVertex[64];
        internal int[] Indices = new int[96];
        internal int VertexCount, IndexCount;
        internal Vector2 BoundsMin, BoundsMax;

        internal UaVectorMesh() => ClearBounds();

        internal void Clear()
        {
            VertexCount = 0;
            IndexCount = 0;
        }

        /// <summary>Makes this mesh a copy of <paramref name="other"/>'s vertices and indices.</summary>
        internal void CopyFrom(UaVectorMesh other)
        {
            if (Vertices.Length < other.VertexCount) Vertices = new UaVectorVertex[Math.Max(other.VertexCount, Vertices.Length * 2)];
            if (Indices.Length < other.IndexCount) Indices = new int[Math.Max(other.IndexCount, Indices.Length * 2)];
            Array.Copy(other.Vertices, Vertices, other.VertexCount);
            Array.Copy(other.Indices, Indices, other.IndexCount);
            VertexCount = other.VertexCount;
            IndexCount = other.IndexCount;
        }

        internal void ClearBounds()
        {
            BoundsMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            BoundsMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        }

        internal bool HasBounds => BoundsMin.x <= BoundsMax.x;

        internal void AddBounds(float x, float y)
        {
            if (x < BoundsMin.x) BoundsMin.x = x;
            if (y < BoundsMin.y) BoundsMin.y = y;
            if (x > BoundsMax.x) BoundsMax.x = x;
            if (y > BoundsMax.y) BoundsMax.y = y;
        }

        internal void AddBounds(Vector2 p) => AddBounds(p.x, p.y);

        internal int Add(Vector2 p, Color32 tint, Vector4 circle, bool arc = true)
        {
            if (VertexCount == Vertices.Length) Array.Resize(ref Vertices, Vertices.Length * 2);
            Vertices[VertexCount] = new UaVectorVertex { position = p, tint = tint, circle = circle, arc = arc };
            return VertexCount++;
        }

        internal ref UaVectorVertex this[int i] => ref Vertices[i];

        void Index(int i)
        {
            if (IndexCount == Indices.Length) Array.Resize(ref Indices, Indices.Length * 2);
            Indices[IndexCount++] = i;
        }

        /// <summary>A triangle as given (no winding check).</summary>
        internal void Triangle(int a, int b, int c)
        {
            Index(a);
            Index(b);
            Index(c);
        }

        /// <summary>A triangle wound clockwise on screen: <paramref name="a"/> first, then the other
        /// two in the order that makes (b - a) × (c - a) non-negative.</summary>
        internal void TriangleCW(int a, int b, int c)
        {
            Vector2 pa = Vertices[a].position, pb = Vertices[b].position, pc = Vertices[c].position;
            float bx = pb.x - pa.x, by = pb.y - pa.y, cx = pc.x - pa.x, cy = pc.y - pa.y;
            float cross = bx * cy - by * cx;
            Index(a);
            if (cross >= 0f) { Index(b); Index(c); }
            else { Index(c); Index(b); }
        }
    }

    /// <summary>Constants and helpers shared by the stroke and fill tessellators.</summary>
    internal static class UaVector
    {
        /// <summary>The coordinate tolerance of the tessellators (points closer than this per axis
        /// are the same point).</summary>
        internal const float Epsilon = 0.001f;
        /// <summary>The circle coordinate that disables one of a vertex's two coverage circles.</summary>
        internal const float UnusedArc = -9999.9f;
        /// <summary>The width, in local units, of the anti-aliasing margin outside every edge.</summary>
        internal const float EdgeBuffer = 2f;
        internal const float TwoPi = 6.2831854820251465f;
        /// <summary>Below this length a curve's derivative is not normalised.</summary>
        internal const float TangentEpsilon = 1e-5f;

        internal static readonly Vector4 Unused = new Vector4(UnusedArc, UnusedArc, UnusedArc, UnusedArc);

        internal static Color32 Tint(Color c) =>
            new Color32(Byte(c.r), Byte(c.g), Byte(c.b), Byte(c.a));

        static byte Byte(float v) => (byte)(int)(Math.Min(1f, Math.Max(0f, v)) * 255f + 0.5f);

        internal static float Length(Vector2 v) => MathF.Sqrt(v.x * v.x + v.y * v.y);

        internal static Vector2 Normalized(Vector2 v)
        {
            float l = Length(v);
            return new Vector2(v.x / l, v.y / l);
        }

        internal static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;

        /// <summary>|a| reduced into [0, 2π] (the arc angle normalisation): negative angles count
        /// back from 2π.</summary>
        internal static float Wrap(float a)
        {
            float m = Math.Abs(a);
            if (m > TwoPi) m -= MathF.Floor(m / TwoPi) * TwoPi;
            return a < 0f ? TwoPi - m : m;
        }

        /// <summary>Clamps to [-1, 1] the way SSE's max/min do: a NaN (0/0 of a degenerate
        /// direction) becomes -1, which Unity's acos inputs get.</summary>
        internal static float ClampUnit(float x)
        {
            float m = x > -1f ? x : -1f;
            return m < 1f ? m : 1f;
        }

        internal static bool Near(Vector2 a, Vector2 b) =>
            Math.Abs(a.x - b.x) <= Epsilon && Math.Abs(a.y - b.y) <= Epsilon;
    }

    /// <summary>An arc of a circle: <see cref="Center"/>, <see cref="Radius"/>, raw start and end
    /// angles in radians and the direction (counter-clockwise on screen: angles decrease).</summary>
    internal struct UaArc
    {
        public Vector2 Center;
        public float Radius, Start, End;
        public bool Ccw;

        /// <summary>The start and end angles normalised into [0, 2π]; a span of 2π or more is the
        /// whole circle from 0.</summary>
        internal void Angles(out float a0, out float a1)
        {
            if (!(Math.Abs(End - Start) < UaVector.TwoPi))
            {
                a0 = 0f;
                a1 = UaVector.TwoPi;
                return;
            }
            a0 = UaVector.Wrap(Start);
            a1 = UaVector.Wrap(End);
        }

        /// <summary>The swept angle in (0, 2π], in the arc's direction; 0 when the normalised
        /// angles coincide.</summary>
        internal float Sweep()
        {
            Angles(out float a0, out float a1);
            if (Math.Abs(a0 - a1) <= 1e-6f) return 0f;
            return SweepBetween(UaVector.Wrap(a0), UaVector.Wrap(a1), Ccw);
        }

        internal static float SweepBetween(float a0, float a1, bool ccw)
        {
            float s = ccw ? a0 - a1 : a1 - a0;
            if (s < 0f) s += UaVector.TwoPi;
            if (s < 1e-30f) s = UaVector.TwoPi;
            return s;
        }

        /// <summary>The start and end points (from the raw angles; a whole circle starts and ends
        /// at angle 0).</summary>
        internal void Points(out Vector2 start, out Vector2 end)
        {
            if (Math.Abs(End - Start) >= UaVector.TwoPi)
            {
                start = end = new Vector2(Radius + Center.x, Center.y);
                return;
            }
            start = new Vector2(MathF.Cos(Start) * Radius + Center.x, MathF.Sin(Start) * Radius + Center.y);
            end = new Vector2(MathF.Cos(End) * Radius + Center.x, MathF.Sin(End) * Radius + Center.y);
        }

        /// <summary>Adds the arc's bounds, widened by <paramref name="halfWidth"/>, to
        /// <paramref name="mesh"/>: the end points and every axis extreme the sweep crosses.
        /// <paramref name="clockwise"/> follows the angles upwards regardless of the direction (the
        /// fill's bounds do; stroke bounds follow the arc).</summary>
        internal void AddBounds(UaVectorMesh mesh, float halfWidth, bool clockwise)
        {
            Angles(out float a0, out float a1);
            a0 = UaVector.Wrap(a0);
            a1 = UaVector.Wrap(a1);
            bool ccw = !clockwise && Ccw;
            float sweep = Math.Abs(a0 - a1) <= 1e-6f ? 0f : SweepBetween(a0, a1, ccw);
            float r = Radius + halfWidth;
            Points(out var s, out var e);
            Vector2 us = UaVector.Normalized(s - Center), ue = UaVector.Normalized(e - Center);
            mesh.AddBounds(Center + us * r);
            mesh.AddBounds(Center + ue * r);
            if (halfWidth > 0f)
            {
                mesh.AddBounds(Center + us * (Radius - halfWidth));
                mesh.AddBounds(Center + ue * (Radius - halfWidth));
            }
            // The axis extremes (0, π/2, π, 3π/2) inside the sweep.
            for (int q = 0; q < 4; q++)
            {
                float angle = q * (UaVector.TwoPi / 4f);
                float d = ccw ? a0 - angle : angle - a0;
                if (d < 0f) d += UaVector.TwoPi;
                if (d <= sweep)
                    mesh.AddBounds(Center.x + (q == 0 ? r : q == 2 ? -r : 0f), Center.y + (q == 1 ? r : q == 3 ? -r : 0f));
            }
        }
    }

    /// <summary>A cubic Bézier curve with the polynomial form Painter2D evaluates.</summary>
    internal struct UaCubic
    {
        public Vector2 P0, P1, P2, P3;

        internal UaCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            P0 = p0; P1 = p1; P2 = p2; P3 = p3;
        }

        // B(t) = ((t c) + ((t² b) + (t³ a))) + d.
        Vector2 A => new Vector2(P3.x - P2.x * 3f + P1.x * 3f - P0.x, P3.y - P2.y * 3f + P1.y * 3f - P0.y);
        Vector2 B => new Vector2(P1.x * -6f + P2.x * 3f + P0.x * 3f, P1.y * -6f + P2.y * 3f + P0.y * 3f);
        Vector2 C => new Vector2(P1.x * 3f - P0.x * 3f, P1.y * 3f - P0.y * 3f);

        internal Vector2 Eval(float t)
        {
            Vector2 a = A, b = B, c = C;
            float t2 = t * t, t3 = t2 * t;
            return new Vector2(t * c.x + (t2 * b.x + t3 * a.x) + P0.x, t * c.y + (t2 * b.y + t3 * a.y) + P0.y);
        }

        /// <summary>The unit tangent at <paramref name="t"/> (the raw derivative when it is shorter
        /// than <see cref="UaVector.TangentEpsilon"/>).</summary>
        internal Vector2 Tangent(float t)
        {
            Vector2 a = A, b = B, c = C;
            float t2 = t * t;
            var d = new Vector2(c.x + (t * (b.x + b.x) + t2 * (a.x * 3f)), c.y + (t * (b.y + b.y) + t2 * (a.y * 3f)));
            float l2 = d.x * d.x + d.y * d.y;
            if (UaVector.TangentEpsilon * UaVector.TangentEpsilon >= l2) return d;
            float l = MathF.Sqrt(l2);
            return new Vector2(d.x / l, d.y / l);
        }

        /// <summary>Adds, at each extreme of x or y inside (0, 1), the curve point widened by
        /// <paramref name="halfWidth"/> along that axis.</summary>
        internal void AddExtremeBounds(UaVectorMesh mesh, float halfWidth)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                float p0 = P0[axis], p1 = P1[axis], p2 = P2[axis], p3 = P3[axis];
                float a = p3 * 3f - p2 * 9f + p1 * 9f - p0 * 3f;
                float b = p2 * 6f - p1 * 12f + p0 * 6f;
                float c = p1 * 3f - p0 * 3f;
                float r0 = -1f, r1 = -1f;
                if (Math.Abs(a) > 1e-6f)
                {
                    float disc = b * b + (-4f * a) * c;
                    if (disc >= 0f)
                    {
                        float s = MathF.Sqrt(disc), inv = 1f / (a + a);
                        r0 = (s - b) * inv;
                        r1 = (-b - s) * inv;
                    }
                }
                else if (Math.Abs(b) > 1e-6f)
                {
                    r0 = -c / b;
                }
                AddRoot(mesh, r0, axis, halfWidth);
                AddRoot(mesh, r1, axis, halfWidth);
            }
        }

        void AddRoot(UaVectorMesh mesh, float t, int axis, float halfWidth)
        {
            if (!(t > 0f && t < 1f)) return;
            var p = Eval(t);
            if (axis == 0)
            {
                mesh.AddBounds(p.x + halfWidth, p.y);
                mesh.AddBounds(p.x - halfWidth, p.y);
            }
            else
            {
                mesh.AddBounds(p.x, p.y + halfWidth);
                mesh.AddBounds(p.x, p.y - halfWidth);
            }
        }
    }

    /// <summary>A piece of a Bézier curve: a circular arc (<see cref="IsArc"/>, with
    /// <see cref="Center"/> and <see cref="Radius"/>; a zero radius draws it as its chord) or a
    /// straight line through its points.</summary>
    internal struct UaCurvePiece
    {
        public float T0, T1;
        public Vector2 P0, Pm, P1, Center;
        public float Radius;
        public bool IsArc;
    }

    /// <summary>Approximates a cubic Bézier curve by circular arcs and straight pieces, the way
    /// Painter2D strokes and fills curves: each half-interval [t0, t1] (at most 7 halvings) gets the
    /// circle through B(t0), B(tm), B(t1) when the tangents agree and the curve stays near it
    /// (squared radius error at most 0.01 at the quarter points), a line when it is straight
    /// (tangents within 0.999 of the chord), and is halved again otherwise; arcs whose sagitta
    /// exceeds 1 are halved on the same circle. Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/uitkpainter).</summary>
    internal static class UaCurveSubdivision
    {
        const int MaxDepth = 7;
        const float MaxRadius = 100000f;
        const float MaxSquaredError = 0.01f;
        const float StraightDot = 0.9990000128746033f;
        const float MaxSagitta = 1f;

        internal static void Subdivide(in UaCubic curve, List<UaCurvePiece> into)
        {
            into.Clear();
            Subdivide(curve, into, 0f, 1f, 1);
        }

        static void Subdivide(in UaCubic curve, List<UaCurvePiece> into, float t0, float t1, int depth)
        {
            float tm = (t0 + t1) * 0.5f;
            Vector2 p0 = curve.Eval(t0), pm = curve.Eval(tm), p1 = curve.Eval(t1);
            if (depth > MaxDepth || (Math.Abs(p0.x - p1.x) <= UaVector.Epsilon && Math.Abs(p0.y - p1.y) <= UaVector.Epsilon))
            {
                SplitLarge(curve, new UaCurvePiece { T0 = t0, T1 = t1, P0 = p0, Pm = pm, P1 = p1, IsArc = true }, into);
                return;
            }
            Vector2 tan0 = curve.Tangent(t0), tanM = curve.Tangent(tm), tan1 = curve.Tangent(t1);
            float ax = pm.x - p0.x, ay = pm.y - p0.y, bx = p1.x - p0.x, by = p1.y - p0.y;
            float det = ax * by - ay * bx;
            if (!(1e-7f > Math.Abs(det)))
            {
                float a2 = ax * ax + ay * ay, b2 = bx * bx + by * by;
                var center = new Vector2(p0.x + (by * a2 - ay * b2) / (det * 2f), p0.y + (ax * b2 - bx * a2) / (det * 2f));
                float radius = UaVector.Length(center - p0);
                if (radius < MaxRadius)
                {
                    if (UaVector.Dot(tan0, tan1) < 0f || UaVector.Dot(tan0, tanM) < 0f)
                    {
                        Split(curve, into, t0, tm, t1, depth);
                        return;
                    }
                    var q1 = curve.Eval((tm + t0) * 0.5f);
                    var q2 = curve.Eval((tm + t1) * 0.5f);
                    float e1 = UaVector.Length(q1 - center) - radius, e2 = UaVector.Length(q2 - center) - radius;
                    if (e1 * e1 + e2 * e2 > MaxSquaredError)
                    {
                        Split(curve, into, t0, tm, t1, depth);
                        return;
                    }
                    SplitLarge(curve, new UaCurvePiece { T0 = t0, T1 = t1, P0 = p0, Pm = pm, P1 = p1, Center = center, Radius = radius, IsArc = true }, into);
                    return;
                }
            }
            var chord = UaVector.Normalized(p1 - p0);
            if (StraightDot > UaVector.Dot(tan0, chord) || StraightDot > UaVector.Dot(tan1, chord) || StraightDot > UaVector.Dot(tanM, chord))
            {
                Split(curve, into, t0, tm, t1, depth);
                return;
            }
            into.Add(new UaCurvePiece { T0 = t0, T1 = t1, P0 = p0, Pm = pm, P1 = p1 });
        }

        static void Split(in UaCubic curve, List<UaCurvePiece> into, float t0, float tm, float t1, int depth)
        {
            Subdivide(curve, into, t0, tm, depth + 1);
            Subdivide(curve, into, tm, t1, depth + 1);
        }

        static void SplitLarge(in UaCubic curve, UaCurvePiece piece, List<UaCurvePiece> into)
        {
            var mid = (piece.P0 + piece.P1) * 0.5f;
            if (MaxSagitta >= piece.Radius - UaVector.Length(mid - piece.Center))
            {
                into.Add(piece);
                return;
            }
            float tm = (piece.T0 + piece.T1) * 0.5f;
            var first = piece;
            first.T1 = tm;
            first.Pm = curve.Eval((piece.T0 + tm) * 0.5f);
            first.P1 = curve.Eval(tm);
            SplitLarge(curve, first, into);
            var second = piece;
            second.T0 = tm;
            second.P0 = curve.Eval(tm);
            second.Pm = curve.Eval((tm + piece.T1) * 0.5f);
            SplitLarge(curve, second, into);
        }

        /// <summary>The tangent at one end of <paramref name="piece"/> as its own geometry gives it
        /// (perpendicular to the radius for arcs, the chord for lines), falling back on the curve's
        /// tangent when that geometry is degenerate.</summary>
        internal static Vector2 PieceTangent(in UaCurvePiece piece, in UaCubic curve, bool start)
        {
            if (piece.IsArc)
            {
                var v = (start ? piece.P0 : piece.P1) - piece.Center;
                float l = UaVector.Length(v);
                if (l > UaVector.Epsilon)
                {
                    var u = new Vector2(v.x / l, v.y / l);
                    float cross = (piece.P1.x - piece.P0.x) * u.y - (piece.P1.y - piece.P0.y) * u.x;
                    return cross > 0f ? new Vector2(u.y, -u.x) : new Vector2(-u.y, u.x);
                }
            }
            else
            {
                var chord = piece.P1 - piece.P0;
                float l = UaVector.Length(chord);
                if (l > UaVector.Epsilon) return new Vector2(chord.x / l, chord.y / l);
            }
            return curve.Tangent(start ? piece.T0 : piece.T1);
        }
    }
}
