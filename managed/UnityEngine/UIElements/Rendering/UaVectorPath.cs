using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    internal enum UaPathKind : byte { MoveTo, Line, Arc, ArcTo, Bezier, CapStart, CapEnd, Join }

    /// <summary>One entry of a Painter2D path. Segments (lines, arcs, arcs-to and cubic curves)
    /// record the point they start from in <see cref="From"/>; markers say where the stroker puts a
    /// start cap, an end cap or a join (<see cref="JoinTo"/>: the segment the previous one joins;
    /// a closing join points back at the sub-path's first segment).</summary>
    internal struct UaPathEntry
    {
        public UaPathKind Kind;
        /// <summary>Line: start, end. ArcTo: the current point, the corner. Bezier: the four
        /// control points.</summary>
        public Vector2 A, B, C, D;
        /// <summary>Arc: the circle and angles (ArcTo: <see cref="UaArc.Radius"/> only).</summary>
        public UaArc Arc;
        /// <summary>The point the segment starts from, which ClosePath returns to when the
        /// segment opens its sub-path.</summary>
        public Vector2 From;
        public int JoinTo;

        internal bool IsSegment => Kind >= UaPathKind.Line && Kind <= UaPathKind.Bezier;
    }

    /// <summary>A Painter2D path: the entries its calls add and the sub-path bookkeeping (the
    /// current point, whether a new sub-path starts with the next segment, whether the current
    /// one is closed). Mirrors the measured Unity 6000.0.71f1 behaviour
    /// (tests/projects/uitkpainter): points closer than 0.001 per axis to the current point add no
    /// line, ClosePath returns to the point the sub-path's first segment recorded (a
    /// QuadraticCurveTo records the origin there), an arc that ends where it starts gets no end
    /// cap.</summary>
    internal sealed class UaVectorPath
    {
        internal readonly List<UaPathEntry> Entries = new List<UaPathEntry>();
        Vector2 m_Current;
        bool m_HasCurrent;
        bool m_NewSubPath = true;
        bool m_Closed;
        int m_SubPathStart;

        internal void Clear()
        {
            Entries.Clear();
            m_Current = Vector2.zero;
            m_HasCurrent = false;
            m_NewSubPath = true;
            m_Closed = false;
            m_SubPathStart = 0;
        }

        static bool Finite(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y);

        internal void MoveTo(Vector2 p)
        {
            if (!Finite(p)) return;
            Terminate(true);
            Entries.Add(new UaPathEntry { Kind = UaPathKind.MoveTo, A = p, B = p });
            m_SubPathStart = Entries.Count;
            m_Current = p;
            m_HasCurrent = true;
            m_NewSubPath = true;
            m_Closed = false;
        }

        internal void LineTo(Vector2 p)
        {
            if (!Finite(p)) return;
            if (!m_HasCurrent)
            {
                m_Current = p;
                m_HasCurrent = true;
                return;
            }
            Prepare();
            if (Math.Abs(p.x - m_Current.x) <= UaVector.Epsilon && Math.Abs(p.y - m_Current.y) <= UaVector.Epsilon) return;
            Entries.Add(new UaPathEntry { Kind = UaPathKind.Line, A = m_Current, B = p, From = m_Current });
            m_Current = p;
        }

        internal void Arc(Vector2 center, float radius, float start, float end, ArcDirection direction)
        {
            if (!Finite(center) || !float.IsFinite(radius) || !float.IsFinite(start) || !float.IsFinite(end)) return;
            if (!(radius > UaVector.Epsilon) || !(Math.Abs(start - end) > 1e-6f)) return;
            var arc = new UaArc { Center = center, Radius = radius, Start = start, End = end, Ccw = direction == ArcDirection.CounterClockwise };
            arc.Points(out var from, out var to);
            // The entry records the current point as it was before the arc (the arc's start
            // without one).
            var recorded = m_HasCurrent ? m_Current : from;
            if (m_HasCurrent)
            {
                if (from != m_Current) LineTo(from);
            }
            else
            {
                m_Current = from;
                m_HasCurrent = true;
            }
            // An arc that ends where it starts opens its sub-path without a start cap.
            Prepare(capStart: from != to);
            Entries.Add(new UaPathEntry { Kind = UaPathKind.Arc, Arc = arc, A = from, B = to, From = recorded });
            m_Current = to;
        }

        internal void ArcTo(Vector2 p1, Vector2 p2, float radius)
        {
            if (!Finite(p1) || !Finite(p2) || !float.IsFinite(radius)) return;
            if (!m_HasCurrent)
            {
                m_Current = p1;
                m_HasCurrent = true;
            }
            var current = m_Current;
            if (current == p1 && p1 == p2) return;
            // Without a corner (or a turn at it) the arc becomes a line to the target.
            if (p1 == p2 || current == p1)
            {
                LineTo(p2);
                return;
            }
            Vector2 a = p1 - current, b = p2 - p1;
            float cross = a.y * b.x - a.x * b.y;
            if (!(Math.Abs(cross) > UaVector.Epsilon))
            {
                LineTo(p2);
                return;
            }
            var entry = new UaPathEntry { Kind = UaPathKind.ArcTo, A = current, B = p1, C = p2, From = current };
            entry.Arc.Radius = radius;
            var next = p2;
            if (UaArcTo.Tangents(entry, out var t1, out var t2, out _))
            {
                if (Math.Abs(current.x - t1.x) > UaVector.Epsilon || Math.Abs(current.y - t1.y) > UaVector.Epsilon) LineTo(t1);
                next = t2;
            }
            // Recorded even when the turn is too slight for an arc (it then draws nothing).
            Prepare();
            Entries.Add(entry);
            m_Current = next;
        }

        internal void BezierCurveTo(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            if (!Finite(p1) || !Finite(p2) || !Finite(p3)) return;
            if (!m_HasCurrent)
            {
                m_Current = p1;
                m_HasCurrent = true;
            }
            var current = m_Current;
            if (current == p1 && current == p2 && current == p3) return;
            Prepare();
            Entries.Add(new UaPathEntry { Kind = UaPathKind.Bezier, A = current, B = p1, C = p2, D = p3, From = current });
            m_Current = p3;
        }

        internal void QuadraticCurveTo(Vector2 p1, Vector2 p2)
        {
            if (!Finite(p1) || !Finite(p2)) return;
            if (!m_HasCurrent)
            {
                m_Current = p1;
                m_HasCurrent = true;
            }
            var current = m_Current;
            if (current == p1 && current == p2) return;
            Prepare();
            const float TwoThirds = 0.6666666865348816f;
            var c1 = new Vector2((p1.x - current.x) * TwoThirds + current.x, (p1.y - current.y) * TwoThirds + current.y);
            var c2 = new Vector2((p1.x - p2.x) * TwoThirds + p2.x, (p1.y - p2.y) * TwoThirds + p2.y);
            // Measured: a quadratic segment records the origin, not the current point, as the
            // point it starts from (ClosePath of a sub-path opened by one returns there).
            Entries.Add(new UaPathEntry { Kind = UaPathKind.Bezier, A = current, B = c1, C = c2, D = p2, From = Vector2.zero });
            m_Current = p2;
        }

        internal void ClosePath()
        {
            if (m_Closed) return;
            int first = -1;
            for (int i = m_SubPathStart; i < Entries.Count; i++)
            {
                if (Entries[i].IsSegment)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
            {
                Terminate(false);
                return;
            }
            LineTo(Entries[first].From);
            Terminate(false);
            // The start cap gives way to the closing join.
            if (first > 0 && Entries[first - 1].Kind == UaPathKind.CapStart)
            {
                Entries.RemoveAt(first - 1);
                first--;
                // The joins after it now point one entry earlier.
                for (int i = first; i < Entries.Count; i++)
                {
                    if (Entries[i].Kind != UaPathKind.Join) continue;
                    var join = Entries[i];
                    join.JoinTo--;
                    Entries[i] = join;
                }
            }
            Entries.Add(new UaPathEntry { Kind = UaPathKind.Join, JoinTo = first });
            m_Closed = true;
            m_NewSubPath = true;
        }

        /// <summary>Before a segment: a start cap opens a new sub-path, a join links the segment
        /// to the previous one.</summary>
        void Prepare(bool capStart = true)
        {
            if (m_NewSubPath)
            {
                if (capStart && (Entries.Count == 0 || Entries[Entries.Count - 1].Kind != UaPathKind.CapStart))
                    Entries.Add(new UaPathEntry { Kind = UaPathKind.CapStart });
                m_NewSubPath = false;
                m_Closed = false;
                return;
            }
            if (Entries.Count > 0 && Entries[Entries.Count - 1].IsSegment)
                Entries.Add(new UaPathEntry { Kind = UaPathKind.Join, JoinTo = Entries.Count + 1 });
        }

        /// <summary>Ends the open sub-path: drops trailing joins and an unused start cap, and adds
        /// an end cap (<paramref name="cap"/>) unless the last segment is an arc that ends where it
        /// starts.</summary>
        internal void Terminate(bool cap)
        {
            if (m_NewSubPath) return;
            while (Entries.Count > 0)
            {
                var last = Entries[Entries.Count - 1];
                if (last.Kind == UaPathKind.Join)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                    continue;
                }
                if (last.Kind == UaPathKind.CapStart)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                    m_NewSubPath = true;
                    return;
                }
                if (last.Kind != UaPathKind.CapEnd && cap && !(last.Kind == UaPathKind.Arc && last.A == last.B))
                    Entries.Add(new UaPathEntry { Kind = UaPathKind.CapEnd });
                break;
            }
            m_NewSubPath = true;
        }
    }

    /// <summary>The geometry of an arc-to entry: the circle tangent to the lines from the current
    /// point to the corner and from the corner to the target.</summary>
    internal static class UaArcTo
    {
        internal static bool Tangents(in UaPathEntry e, out Vector2 t1, out Vector2 t2, out Vector2 center)
        {
            Vector2 p0 = e.A, p1 = e.B, p2 = e.C;
            float r = e.Arc.Radius;
            var v1 = UaVector.Normalized(p0 - p1);
            var v2 = UaVector.Normalized(p2 - p1);
            var b = UaVector.Normalized(new Vector2((v1.x + v2.x) * 0.5f, (v1.y + v2.y) * 0.5f));
            float cos = UaVector.Dot(v1, b) / (UaVector.Length(v1) * UaVector.Length(b));
            float half = MathF.Acos(Math.Min(1f, Math.Max(-1f, cos)));
            if (UaVector.Epsilon > half)
            {
                t1 = t2 = center = Vector2.zero;
                return false;
            }
            float dt = r / MathF.Tan(half), dc = r / MathF.Sin(half);
            center = new Vector2(b.x * dc + p1.x, b.y * dc + p1.y);
            t1 = new Vector2(v1.x * dt + p1.x, v1.y * dt + p1.y);
            t2 = new Vector2(v2.x * dt + p1.x, v2.y * dt + p1.y);
            return true;
        }

        /// <summary>The arc an arc-to entry draws (angles from the x axis, direction from the turn).</summary>
        internal static bool ToArc(in UaPathEntry e, out UaArc arc)
        {
            arc = default;
            if (!Tangents(e, out var t1, out var t2, out var c)) return false;
            Vector2 v1 = t1 - c, v2 = t2 - c;
            float cross = v1.x * v2.y - v1.y * v2.x;
            arc = new UaArc { Center = c, Radius = e.Arc.Radius, Start = Angle(v1), End = Angle(v2), Ccw = cross < 0f };
            return true;
        }

        static float Angle(Vector2 v)
        {
            float l = UaVector.Length(v);
            float a = MathF.Acos(Math.Min(1f, Math.Max(-1f, (v.y * 0f + v.x) / l)));
            return v.y - v.x * 0f < 0f ? -a : a;
        }
    }
}
