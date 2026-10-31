using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision
{
    internal static partial class CollisionUtils
    {
        /// Compute the point states given two manifolds. The states pertain to the transition from manifold1
        /// to manifold2. So state1 is either persist or remove while state2 is either add or persist.
        public static void GetPointStates(
            in PointState[] state1,
            in PointState[] state2,
            in Manifold manifold1,
            in Manifold manifold2)
        {
            for (var i = 0; i < Settings.MaxManifoldPoints; ++i)
            {
                state1[i] = PointState.NullState;
                state2[i] = PointState.NullState;
            }

            // Detect persists and removes.
            for (var i = 0; i < manifold1.PointCount; ++i)
            {
                var id = manifold1.Points[i].Id;

                state1[i] = PointState.RemoveState;

                for (var j = 0; j < manifold2.PointCount; ++j)
                {
                    if (manifold2.Points[j].Id.Key == id.Key)
                    {
                        state1[i] = PointState.PersistState;
                        break;
                    }
                }
            }

            // Detect persists and adds.
            for (var i = 0; i < manifold2.PointCount; ++i)
            {
                var id = manifold2.Points[i].Id;

                state2[i] = PointState.AddState;

                for (var j = 0; j < manifold1.PointCount; ++j)
                {
                    if (manifold1.Points[j].Id.Key == id.Key)
                    {
                        state2[i] = PointState.PersistState;
                        break;
                    }
                }
            }
        }

        /// Clipping for contact manifolds.
        public static int ClipSegmentToLine(
            in Span<ClipVertex> vOut,
            in Span<ClipVertex> vIn,
            in Vector2 normal,
            float offset,
            int vertexIndexA)
        {
            // Start with no output points
            var count = 0;

            // Calculate the distance of end points to the line
            var distance0 = Vector2.Dot(normal, vIn[0].Vector) - offset;
            var distance1 = Vector2.Dot(normal, vIn[1].Vector) - offset;

            // If the points are behind the plane
            if (distance0 <= 0.0f)
            {
                vOut[count++] = vIn[0];
            }

            if (distance1 <= 0.0f)
            {
                vOut[count++] = vIn[1];
            }

            // If the points are on different sides of the plane
            if (distance0 * distance1 < 0.0f)
            {
                // Find intersection point of edge and plane
                var interp = distance0 / (distance0 - distance1);
                vOut[count].Vector = vIn[0].Vector + interp * (vIn[1].Vector - vIn[0].Vector);

                // VertexA is hitting edgeB.
                vOut[count].Id.ContactFeature.IndexA = (byte) vertexIndexA;
                vOut[count].Id.ContactFeature.IndexB = vIn[0].Id.ContactFeature.IndexB;
                vOut[count].Id.ContactFeature.TypeA = (byte) ContactFeature.FeatureType.Vertex;
                vOut[count].Id.ContactFeature.TypeB = (byte) ContactFeature.FeatureType.Face;
                ++count;
                Debug.Assert(count == 2);
            }

            return count;
        }

        // Both polygon manifolds and distance-cache contacts choose the incident
        // edge by its most opposing normal, preserving vertex order for ties.
        public static int FindIncidentEdgeIndex(ReadOnlySpan<Vector2> normals, in Vector2 referenceNormal)
        {
            int index = 0;
            float minDot = Settings.MaxFloat;
            for (int i = 0; i < normals.Length; i++)
            {
                float dot = Vector2.Dot(referenceNormal, normals[i]);
                if (dot < minDot) { minDot = dot; index = i; }
            }
            return index;
        }

        static void FindIncidentEdge(in DistanceProxy incident, in Transform transform, in Vector2 normal,
            out Vector2 start, out Vector2 end)
        {
            // A distance proxy can be a segment, so derive its world normals
            // directly rather than requiring a PolygonShape's stored normals.
            Span<Vector2> normals = stackalloc Vector2[incident.Count];
            for (int i = 0; i < incident.Count; i++)
            {
                var p0 = MathUtils.Mul(transform, incident.Vertices[i]);
                var p1 = MathUtils.Mul(transform, incident.Vertices[(i + 1) % incident.Count]);
                var tangent = Vector2.Normalize(p1 - p0);
                normals[i] = new Vector2(tangent.Y, -tangent.X);
            }
            int index = FindIncidentEdgeIndex(normals, normal);
            start = MathUtils.Mul(transform, incident.Vertices[index]);
            end = MathUtils.Mul(transform, incident.Vertices[(index + 1) % incident.Count]);
        }

        // A two-point GJK cache identifies an edge facing a vertex. Reconstruct
        // its face contact using the same incident-edge selection as manifolds.
        public static bool TryGetFaceContact(in DistanceProxy a, in Transform xfA,
            in DistanceProxy b, in Transform xfB, in SimplexCache cache,
            out Vector2 point, out Vector2 normal)
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
            var tangent = Vector2.Normalize(v1 - v0);
            var n = new Vector2(tangent.Y, -tangent.X);
            int support = edgeA ? cache.IndexB[0] : cache.IndexA[0];
            var witness = MathUtils.Mul(xfI, incident.Vertices[support]);
            if (Vector2.Dot(witness - v0, n) < 0f) n = -n;
            FindIncidentEdge(incident, xfI, n, out var q0, out var q1);
            float lo = Math.Min(Vector2.Dot(v0, tangent), Vector2.Dot(v1, tangent));
            float hi = Math.Max(Vector2.Dot(v0, tangent), Vector2.Dot(v1, tangent));
            if (!ClipSegmentToInterval(q0, q1, tangent, lo, hi, out var lower, out var upper)) return false;
            float d0 = Vector2.Dot(lower - v0, n), d1 = Vector2.Dot(upper - v0, n);
            var q = WorldManifold.SelectPoint(lower, upper, d0, d1, Math.Abs(d0 - d1), Settings.Epsilon,
                out float separation);
            point = q + 0.5f * (reference.Radius - incident.Radius - separation) * n;
            normal = edgeA ? n : -n;
            return true;
        }

        // Clip a world segment against the two tangent bounds of a core edge.
        static bool ClipSegmentToInterval(Vector2 q0, Vector2 q1, in Vector2 tangent,
            float lo, float hi, out Vector2 lower, out Vector2 upper)
        {
            lower = upper = default;
            float s0 = Vector2.Dot(q0, tangent), s1 = Vector2.Dot(q1, tangent);
            if (s0 > s1) { (q0, q1) = (q1, q0); (s0, s1) = (s1, s0); }
            if (s1 < lo || s0 > hi || s1 - s0 <= 0f) return false;
            var delta = q1 - q0;
            lower = q0 + delta * Math.Max(0f, (lo - s0) / (s1 - s0));
            upper = q0 + delta * Math.Min(1f, (hi - s0) / (s1 - s0));
            return true;
        }

        /// Determine if two generic shapes overlap.
        public static bool TestOverlap(
            Shape shapeA,
            int indexA,
            Shape shapeB,
            int indexB,
            in Transform xfA,
            in Transform xfB,
            GJkProfile gJkProfile)
        {
            var input = new DistanceInput();
            input.ProxyA.Set(shapeA, indexA);
            input.ProxyB.Set(shapeB, indexB);
            input.TransformA = xfA;
            input.TransformB = xfB;
            input.UseRadii = true;

            var cache = new SimplexCache();
            DistanceAlgorithm.Distance(out var output, ref cache, input, gJkProfile);
            return output.Distance < 10.0f * Settings.Epsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TestOverlap(in AABB a, AABB b)
        {
            //var d1 = b.LowerBound - a.UpperBound;

            if (b.LowerBound.X - a.UpperBound.X > 0.0f || b.LowerBound.Y - a.UpperBound.Y > 0.0f)
            {
                return false;
            }

            //var d2 = a.LowerBound - b.UpperBound;
            if (a.LowerBound.X - b.UpperBound.X > 0.0f || a.LowerBound.Y - b.UpperBound.Y > 0.0f)
            {
                return false;
            }

            return true;
        }
    }
}