using System;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Common;
using B2 = System.Numerics.Vector2;

namespace Unbound.Runtime
{
    internal static class QueryParityContact
    {
        internal static bool TryContact(DistanceProxy a, Transform xfA, DistanceProxy b, Transform xfB,
            SimplexCache cache, out B2 point, out B2 normal)
        {
            point = normal = default;
            if (cache.Count != 2 || a.Count < 2 || b.Count < 2) return false;
            bool edgeA = cache.IndexA[0] != cache.IndexA[1];
            bool edgeB = cache.IndexB[0] != cache.IndexB[1];
            if (edgeA == edgeB) return false;
            var reference = edgeA ? a : b;
            var incident = edgeA ? b : a;
            var xfR = edgeA ? xfA : xfB;
            var xfI = edgeA ? xfB : xfA;
            int i0 = edgeA ? cache.IndexA[0] : cache.IndexB[0];
            int i1 = edgeA ? cache.IndexA[1] : cache.IndexB[1];
            var v0 = MathUtils.Mul(xfR, reference.Vertices[i0]);
            var v1 = MathUtils.Mul(xfR, reference.Vertices[i1]);
            var tangent = B2.Normalize(v1 - v0);
            var n = new B2(tangent.Y, -tangent.X);
            int support = edgeA ? cache.IndexB[0] : cache.IndexA[0];
            var witness = MathUtils.Mul(xfI, incident.Vertices[support]);
            if (B2.Dot(witness-v0, n)<0f) n=-n;
            // Find the incident edge whose outward normal most opposes the reference normal.
            int index = 0;
            float best = float.MaxValue;
            for (int i = 0; i < incident.Count; i++)
            {
                var p0 = MathUtils.Mul(xfI, incident.Vertices[i]);
                var p1 = MathUtils.Mul(xfI, incident.Vertices[(i+1)%incident.Count]);
                var t = B2.Normalize(p1-p0);
                float d = B2.Dot(new B2(t.Y,-t.X),n);
                if (d<best) { best=d; index=i; }
            }
            var q0 = MathUtils.Mul(xfI, incident.Vertices[index]);
            var q1 = MathUtils.Mul(xfI, incident.Vertices[(index+1)%incident.Count]);
            float lo = Math.Min(B2.Dot(v0,tangent),B2.Dot(v1,tangent));
            float hi = Math.Max(B2.Dot(v0,tangent),B2.Dot(v1,tangent));
            float s0 = B2.Dot(q0,tangent), s1 = B2.Dot(q1,tangent);
            if (s0>s1) { (q0,q1)=(q1,q0); (s0,s1)=(s1,s0); }
            if (s1<lo || s0>hi || s1-s0<=0f) return false;
            var delta=q1-q0;
            var lower=q0 + delta*Math.Max(0f,(lo-s0)/(s1-s0));
            var upper=q0 + delta*Math.Min(1f,(hi-s0)/(s1-s0));
            float d0=B2.Dot(lower-v0,n), d1=B2.Dot(upper-v0,n);
            var q=d0<d1 ? lower:upper;
            float separation=Math.Min(d0,d1);
            if (Math.Abs(d0-d1)<=Settings.Epsilon) { q=0.5f*(lower+upper); separation=0.5f*(d0+d1); }
            point=q + 0.5f*(reference.Radius-incident.Radius-separation)*n;
            normal=edgeA?n:-n;
            return true;
        }
    }
}
