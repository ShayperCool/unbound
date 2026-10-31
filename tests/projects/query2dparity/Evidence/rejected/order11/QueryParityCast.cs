using System;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;

namespace Unbound.Runtime
{
    /// <summary>Linear query TOI. Query precision is independent of contact-offset solver tuning.</summary>
    internal static class QueryParityCast
    {
        const float LinearSlop = 0.005f;
        internal const float ContactSeparationTolerance = LinearSlop * LinearSlop;

        internal static bool Cast(out ShapeCastOutput output, in ShapeCastInput input)
        {
            var a = input.TransformA;
            var b = input.TransformB;
            float angleA = a.Rotation.Angle, angleB = b.Rotation.Angle;
            var sweepA = new Sweep { C0 = a.Position, C = a.Position, A0 = angleA, A = angleA };
            var sweepB = new Sweep { C0 = b.Position, C = b.Position + input.TranslationB, A0 = angleB, A = angleB };
            sweepA.Normalize();
            sweepB.Normalize();
            output = default;
            if (!Time(input.ProxyA, input.ProxyB, sweepA, sweepB, out float time)) return false;
            // TOI's minimum target can exceed the radii of very small polygons. Native
            // reports no hit when that configuration has no actual surface contact.
            var distance = new DistanceInput
            {
                ProxyA = input.ProxyA, ProxyB = input.ProxyB, TransformA = a,
                TransformB = new Transform(b.Position + time * input.TranslationB, b.Rotation),
                UseRadii = true,
            };
            var cache = new SimplexCache();
            DistanceAlgorithm.Distance(out var contact, ref cache, distance);
            if (contact.Distance > 0f) return false;
            output.Lambda = time;
            return true;
        }

        // Same swept separating-axis/root algorithm as the shared TOI, with query constants.
        // Contact/solver TOI and GJK are left unchanged.
        static bool Time(DistanceProxy a, DistanceProxy b, Sweep sweepA, Sweep sweepB, out float time)
        {
            float target = Math.Max(LinearSlop, a.Radius + b.Radius - 3f * LinearSlop);
            const float tolerance = 0.25f * LinearSlop;
            var cache = new SimplexCache();
            var distance = new DistanceInput { ProxyA = a, ProxyB = b, UseRadii = false };
            float t1 = 0f;
            time = 1f;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                sweepA.GetTransform(out var xfA, t1);
                sweepB.GetTransform(out var xfB, t1);
                distance.TransformA = xfA;
                distance.TransformB = xfB;
                DistanceAlgorithm.Distance(out var separation, ref cache, distance);
                if (separation.Distance <= 0f) return false;
                if (separation.Distance < target + tolerance) { time = t1; return true; }
                var axis = new SeparationFunction();
                axis.Initialize(ref cache, a, sweepA, b, sweepB, t1);
                float t2 = 1f;
                for (int push = 0; push < Settings.MaxPolygonVertices; push++)
                {
                    float s2 = axis.FindMinSeparation(out int indexA, out int indexB, t2);
                    if (s2 > target + tolerance) return false;
                    if (s2 > target - tolerance) { t1 = t2; break; }
                    float s1 = axis.Evaluate(indexA, indexB, t1);
                    if (s1 < target - tolerance) return false;
                    if (s1 <= target + tolerance) { time = t1; return true; }
                    float lo = t1, hi = t2;
                    for (int root = 0; root < 50; root++)
                    {
                        float t = (root & 1) != 0
                            ? lo + (target - s1) * (hi - lo) / (s2 - s1)
                            : 0.5f * (lo + hi);
                        float s = axis.Evaluate(indexA, indexB, t);
                        if (Math.Abs(s - target) < tolerance) { t2 = t; break; }
                        if (s > target) { lo = t; s1 = s; }
                        else { hi = t; s2 = s; }
                    }
                }
            }
            return false;
        }
    }
}
