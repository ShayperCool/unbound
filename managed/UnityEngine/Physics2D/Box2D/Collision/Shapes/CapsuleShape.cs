using System;
using System.Numerics;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision.Shapes
{
    /// <summary>
    /// Unbound: a capsule (a segment with a radius), the shape of Unity's CapsuleCollider2D, which
    /// Box2D v2.4 lacks. It is a two-vertex polygon whose radius is the capsule's: Box2D's polygon
    /// manifolds, distance, shape cast and time of impact all take the polygon radius into account,
    /// so it collides as a capsule; ray casts, point tests and mass are the capsule's own.
    /// </summary>
    internal sealed class CapsuleShape : PolygonShape
    {
        public CapsuleShape(Vector2 p1, Vector2 p2, float radius)
        {
            Count = 2;
            Vertices[0] = p1;
            Vertices[1] = p2;
            var axis = p2 - p1;
            var length = axis.Length();
            var n = length > Settings.Epsilon ? new Vector2(axis.Y, -axis.X) / length : new Vector2(1.0f, 0.0f);
            Normals[0] = n;
            Normals[1] = -n;
            Centroid = 0.5f * (p1 + p2);
            Radius = radius;
        }

        public Vector2 P1 => Vertices[0];

        public Vector2 P2 => Vertices[1];

        public override Shape Clone() => new CapsuleShape(Vertices[0], Vertices[1], Radius);

        public override bool TestPoint(in Transform transform, in Vector2 p)
        {
            var local = MathUtils.MulT(transform, p);
            return DistanceSquaredToSegment(local) <= Radius * Radius;
        }

        float DistanceSquaredToSegment(Vector2 p)
        {
            var a = Vertices[0];
            var d = Vertices[1] - a;
            var dd = Vector2.Dot(d, d);
            var t = dd > 0.0f ? Math.Clamp(Vector2.Dot(p - a, d) / dd, 0.0f, 1.0f) : 0.0f;
            var q = a + t * d;
            return Vector2.DistanceSquared(p, q);
        }

        /// Rays that start inside do not hit, as for Box2D's solid shapes.
        public override bool RayCast(out RayCastOutput output, in RayCastInput input, in Transform transform, int childIndex)
        {
            output = default;
            var p1 = MathUtils.MulT(transform, input.P1);
            var p2 = MathUtils.MulT(transform, input.P2);
            var d = p2 - p1;
            if (DistanceSquaredToSegment(p1) <= Radius * Radius)
            {
                return false;
            }

            var best = float.MaxValue;
            var bestNormal = Vector2.Zero;

            // The two sides: the segment moved out by the radius along each normal.
            var a = Vertices[0];
            var b = Vertices[1];
            var axis = b - a;
            var axisLength = axis.Length();
            if (axisLength > Settings.Epsilon)
            {
                for (var side = 0; side < 2; side++)
                {
                    var n = Normals[side];
                    var offset = Radius * n;
                    var sa = a + offset;
                    var denominator = Vector2.Dot(n, d);
                    if (denominator >= 0.0f)
                    {
                        continue; // moving away from (or along) this side
                    }

                    var t = Vector2.Dot(n, sa - p1) / denominator;
                    if (t < 0.0f || t > input.MaxFraction || t >= best)
                    {
                        continue;
                    }

                    var hit = p1 + t * d;
                    var s = Vector2.Dot(hit - sa, axis) / (axisLength * axisLength);
                    if (s < 0.0f || s > 1.0f)
                    {
                        continue;
                    }

                    best = t;
                    bestNormal = n;
                }
            }

            // The two end caps.
            for (var i = 0; i < 2; i++)
            {
                var center = Vertices[i];
                var s = p1 - center;
                var rr = Vector2.Dot(d, d);
                if (rr < Settings.Epsilon)
                {
                    continue;
                }

                var c = Vector2.Dot(s, d);
                var sigma = c * c - rr * (Vector2.Dot(s, s) - Radius * Radius);
                if (sigma < 0.0f)
                {
                    continue;
                }

                var t = -(c + (float)Math.Sqrt(sigma)) / rr;
                if (t < 0.0f || t > input.MaxFraction || t >= best)
                {
                    continue;
                }

                best = t;
                bestNormal = Vector2.Normalize(s + t * d);
            }

            if (best == float.MaxValue)
            {
                return false;
            }

            output = new RayCastOutput { Fraction = best, Normal = MathUtils.Mul(transform.Rotation, bestNormal) };
            return true;
        }

        public override void ComputeMass(out MassData massData, float density)
        {
            var p1 = Vertices[0];
            var p2 = Vertices[1];
            var rr = Radius * Radius;
            var length = (p2 - p1).Length();
            var ll = length * length;
            var circleMass = density * (Settings.Pi * rr);
            var boxMass = density * (2.0f * Radius * length);
            var center = 0.5f * (p1 + p2);

            // Two half discs at the ends: a semicircle's centroid is 4r/3pi from its flat side; the
            // parallel axis theorem moves them to the ends of the box.
            var lc = 4.0f * Radius / (3.0f * Settings.Pi);
            var h = 0.5f * length;
            var circleInertia = circleMass * (0.5f * rr + h * h + 2.0f * h * lc);
            var boxInertia = boxMass * (4.0f * rr + ll) / 12.0f;
            var mass = circleMass + boxMass;
            massData = new MassData
            {
                Mass = mass,
                Center = center,
                RotationInertia = circleInertia + boxInertia + mass * Vector2.Dot(center, center),
            };
        }
    }
}
