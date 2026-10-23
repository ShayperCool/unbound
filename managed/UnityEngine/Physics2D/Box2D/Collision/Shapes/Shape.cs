using System;
using System.Numerics;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision.Shapes
{
    /// This holds the mass data computed for a shape.
    /// A shape is used for collision detection. You can create a shape however you like.
    /// Shapes used for simulation in b2World are created automatically when a b2Fixture
    /// is created. Shapes may encapsulate a one or more child shapes.
    internal abstract class Shape
    {
        /// <summary>
        /// Unbound: a ray against a polygon or segment rounded by <paramref name="r"/> (a collider's
        /// edge radius, which Unity's ray casts include; the collision skin they do not), in the
        /// shape's frame. The ray enters through an offset edge or a vertex's disc.
        /// </summary>
        internal static bool RoundedRayCast(Vector2[] vertices, int count, bool closed, float r, Vector2 p1, Vector2 p2,
            float maxFraction, out float fraction, out Vector2 normal)
        {
            fraction = float.MaxValue;
            normal = Vector2.Zero;
            var d = p2 - p1;
            int edges = closed ? count : count - 1;
            for (int i = 0; i < edges; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % count];
                var e = b - a;
                float length = e.Length();
                if (length <= 0f) continue;
                var t = e / length;
                // Outward normal (counter-clockwise polygons); a segment has both sides.
                foreach (var n in closed ? new[] { new Vector2(t.Y, -t.X) } : new[] { new Vector2(t.Y, -t.X), new Vector2(-t.Y, t.X) })
                {
                    float denominator = Vector2.Dot(n, d);
                    if (denominator >= 0f) continue;
                    var o = a + r * n;
                    float hit = Vector2.Dot(n, o - p1) / denominator;
                    if (hit < 0f || hit > maxFraction || hit >= fraction) continue;
                    float along = Vector2.Dot(p1 + hit * d - o, t);
                    if (along < 0f || along > length) continue;
                    fraction = hit;
                    normal = n;
                }
            }
            for (int i = 0; i < count; i++)
            {
                // |p1 + s d - v|^2 = r^2
                var m = p1 - vertices[i];
                float aa = Vector2.Dot(d, d);
                float bb = Vector2.Dot(m, d);
                float cc = Vector2.Dot(m, m) - r * r;
                float sigma = bb * bb - aa * cc;
                if (aa <= 0f || sigma < 0f || cc < 0f) continue;
                float hit = -(bb + MathF.Sqrt(sigma)) / aa;
                if (hit < 0f || hit > maxFraction || hit >= fraction) continue;
                fraction = hit;
                normal = Vector2.Normalize(m + hit * d);
            }
            return fraction <= maxFraction;
        }

        /// Radius of a shape. For polygonal shapes this must be b2_polygonRadius. There is no support for
        /// making rounded polygons.
        public float Radius { get; internal set; }

        public ShapeType ShapeType { get; internal set; }

        /// Clone the concrete shape using the provided allocator.
        public abstract Shape Clone();

        /// Get the number of child primitives.
        public abstract int GetChildCount();

        /// Test a point for containment in this shape. This only works for convex shapes.
        /// @param xf the shape world transform.
        /// @param p a point in world coordinates.
        public abstract bool TestPoint(in Transform transform, in Vector2 point);

        /// Cast a ray against a child shape.
        /// @param output the ray-cast results.
        /// @param input the ray-cast input parameters.
        /// @param transform the transform to be applied to the shape.
        /// @param childIndex the child shape index
        public abstract bool RayCast(
            out RayCastOutput output,
            in RayCastInput input,
            in Transform transform,
            int childIndex);

        /// Given a transform, compute the associated axis aligned bounding box for a child shape.
        /// @param aabb returns the axis aligned box.
        /// @param xf the world transform of the shape.
        /// @param childIndex the child shape
        public abstract void ComputeAABB(out AABB aabb, in Transform xf, int childIndex);

        /// Compute the mass properties of this shape using its dimensions and density.
        /// The inertia tensor is computed about the local origin.
        /// @param massData returns the mass data for this shape.
        /// @param density the density in kilograms per meter squared.
        public abstract void ComputeMass(out MassData massData, float density);
    }
}