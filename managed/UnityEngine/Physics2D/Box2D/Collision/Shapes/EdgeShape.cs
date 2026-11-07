using System;
using System.Numerics;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision.Shapes
{
    /// <summary>
    /// A line segment (edge) shape. These can be connected in chains or loops
    /// to other edge shapes. Edges created independently are two-sided and do
    /// no provide smooth movement across junctions. 
    /// </summary>
    internal class EdgeShape : Shape
    {
        /// These are the edge vertices
        public Vector2 Vertex1;

        /// These are the edge vertices
        public Vector2 Vertex2;

        /// Optional adjacent vertices. These are used for smooth collision.
        public Vector2 Vertex0;

        /// Optional adjacent vertices. These are used for smooth collision.
        public Vector2 Vertex3;

        /// Uses m_vertex0 and m_vertex3 to create smooth collision.
        public bool OneSided;

        /// Unbound: a two-sided edge that is connected to a preceding edge at Vertex1 through
        /// Vertex0 (Box2D v2.3's m_hasVertex0). Unity's EdgeCollider2D segments collide on both
        /// sides and use their neighbours, or the adjacent start point, as ghost vertices.
        public bool HasVertex0;

        /// Unbound: a two-sided edge that is connected to a following edge at Vertex2 through
        /// Vertex3 (Box2D v2.3's m_hasVertex3); the neighbour or the adjacent end point.
        public bool HasVertex3;

        /// Unbound: whether the ghost vertex Vertex0 takes part in collision adjacency. An exactly
        /// zero-length ghost segment (Vertex0 equal to Vertex1, e.g. an adjacent start point on the
        /// chain's first point) has no direction, so collisions treat it as absent; HasVertex0 and
        /// the vertices stay as stored. Unity's behaviour for such a point is unmeasured.
        internal bool UsesVertex0 => HasVertex0 && Vertex0 != Vertex1;

        /// Unbound: the same for Vertex3; an exactly zero-length following ghost segment
        /// (Vertex3 equal to Vertex2) is absent for collision adjacency.
        internal bool UsesVertex3 => HasVertex3 && Vertex3 != Vertex2;

        public EdgeShape()
        {
            ShapeType = ShapeType.Edge;
            Radius = Settings.PolygonRadius;
        }

        /// <summary>
        /// Set this as a part of a sequence. Vertex v0 precedes the edge and vertex v3
        /// follows. These extra vertices are used to provide smooth movement
        /// across junctions. This also makes the collision one-sided. The edge
        /// normal points to the right looking from v1 to v2.
        /// </summary>
        /// <param name="v0"></param>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <param name="v3"></param>
        public void SetOneSided(in Vector2 v0, in Vector2 v1, in Vector2 v2, in Vector2 v3)
        {
            Vertex0 = v0;
            Vertex1 = v1;
            Vertex2 = v2;
            Vertex3 = v3;
            OneSided = true;
        }

        /// <summary>
        /// Set this as an isolated edge. Collision is two-sided.
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        public void SetTwoSided(in Vector2 v1, in Vector2 v2)
        {
            Vertex1 = v1;
            Vertex2 = v2;
            OneSided = false;
            HasVertex0 = false;
            HasVertex3 = false;
        }

        /// <inheritdoc />
        public override Shape Clone()
        {
            var clone = new EdgeShape
            {
                Vertex0 = Vertex0,
                Vertex1 = Vertex1,
                Vertex2 = Vertex2,
                Vertex3 = Vertex3,
                Radius = Radius,
                OneSided = OneSided,
                HasVertex0 = HasVertex0,
                HasVertex3 = HasVertex3
            };
            return clone;
        }

        /// <inheritdoc />
        public override int GetChildCount()
        {
            return 1;
        }

        /// <inheritdoc />
        public override bool TestPoint(in Transform transform, in Vector2 point)
        {
            return false;
        }

        /// <inheritdoc />
        public override bool RayCast(
            out RayCastOutput output,
            in RayCastInput input,
            in Transform transform,
            int childIndex)
        {
            output = default;

            // Put the ray into the edge's frame of reference.
            var p1 = MathUtils.MulT(transform.Rotation, input.P1 - transform.Position);
            var p2 = MathUtils.MulT(transform.Rotation, input.P2 - transform.Position);
            var d = p2 - p1;

            // Unbound: an edge radius rounds the segment for rays (Unity's EdgeCollider2D.edgeRadius).
            float rounding = Radius - Settings.PolygonRadius;
            if (rounding > 1e-6f)
            {
                if (!RoundedRayCast(new[] { Vertex1, Vertex2 }, 2, false, rounding, p1, p2, input.MaxFraction, out var f, out var n)) return false;
                output.Fraction = f;
                output.Normal = MathUtils.Mul(transform.Rotation, n);
                return true;
            }

            var v1 = Vertex1;
            var v2 = Vertex2;
            var e = v2 - v1;

            // Normal points to the right, looking from v1 at v2
            var normal = new Vector2(e.Y, -e.X);
            normal.Normalize();

            // q = p1 + t * d
            // dot(normal, q - v1) = 0
            // dot(normal, p1 - v1) + t * dot(normal, d) = 0
            var numerator = Vector2.Dot(normal, v1 - p1);
            if (OneSided && numerator > 0.0f)
            {
                return false;
            }

            var denominator = Vector2.Dot(normal, d);

            if (Math.Abs(denominator) < Settings.Epsilon)
            {
                return false;
            }

            var t = numerator / denominator;
            if (t < 0.0f || input.MaxFraction < t)
            {
                return false;
            }

            var q = p1 + t * d;

            // q = v1 + s * r
            // s = dot(q - v1, r) / dot(r, r)
            var r = v2 - v1;
            var rr = Vector2.Dot(r, r);
            if (Math.Abs(rr) < Settings.Epsilon)
            {
                return false;
            }

            var s = Vector2.Dot(q - v1, r) / rr;
            if (s < 0.0f || 1.0f < s)
            {
                return false;
            }

            output = new RayCastOutput
            {
                Fraction = t,
                Normal = numerator > 0.0f
                             ? -MathUtils.Mul(transform.Rotation, normal)
                             : MathUtils.Mul(transform.Rotation, normal)
            };

            return true;
        }

        /// <inheritdoc />
        public override void ComputeAABB(out AABB aabb, in Transform xf, int childIndex)
        {
            var v1 = MathUtils.Mul(xf, Vertex1);
            var v2 = MathUtils.Mul(xf, Vertex2);

            var lower = Vector2.Min(v1, v2);
            var upper = Vector2.Max(v1, v2);

            var r = new Vector2(Radius, Radius);
            aabb = new AABB(lower - r, upper + r);
        }

        /// <inheritdoc />
        public override void ComputeMass(out MassData massData, float density)
        {
            massData = new MassData
            {
                Mass = 0,
                Center = 0.5f * (Vertex1 + Vertex2),
                RotationInertia = 0
            };
        }
    }
}
