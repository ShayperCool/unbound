using System;
using System.Numerics;
using Unbound.Box2D.Common;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Collision.Collider;
namespace Unbound.Runtime
{
    internal static class QueryParityManifold
    {
        public static void CollidePolygons(
            ref Manifold manifold,
            PolygonShape polyA,
            in Transform xfA,
            PolygonShape polyB,
            in Transform xfB)
        {
            manifold.PointCount = 0;
            var totalRadius = polyA.Radius + polyB.Radius;

            var separationA = CollisionUtils.FindMaxSeparation(
                out var edgeA,
                polyA,
                xfA,
                polyB,
                xfB);
            if (separationA > totalRadius)
            {
                return;
            }

            var separationB = CollisionUtils.FindMaxSeparation(
                out var edgeB,
                polyB,
                xfB,
                polyA,
                xfA);
            if (separationB > totalRadius)
            {
                return;
            }

            PolygonShape poly1; // reference polygon 
            PolygonShape poly2; // incident polygon 
            Transform xf1, xf2;
            int edge1; // reference edge
            byte flip;
            float k_tol = 0.1f * Settings.LinearSlop;

            if (separationB > separationA + k_tol)
            {
                poly1 = polyB;
                poly2 = polyA;
                xf1 = xfB;
                xf2 = xfA;
                edge1 = edgeB;
                manifold.Type = ManifoldType.FaceB;
                flip = 1;
            }
            else
            {
                poly1 = polyA;
                poly2 = polyB;
                xf1 = xfA;
                xf2 = xfB;
                edge1 = edgeA;
                manifold.Type = ManifoldType.FaceA;
                flip = 0;
            }

            Span<ClipVertex> incidentEdge = stackalloc ClipVertex[2];
            CollisionUtils.FindIncidentEdge(in incidentEdge, poly1, xf1, edge1, poly2, xf2);

            var count1 = poly1.Count;
            var vertices1 = poly1.Vertices;

            var iv1 = edge1;
            var iv2 = edge1 + 1 < count1 ? edge1 + 1 : 0;

            var v11 = vertices1[iv1];
            var v12 = vertices1[iv2];

            var localTangent = v12 - v11;
            localTangent.Normalize();

            var localNormal = MathUtils.Cross(localTangent, 1.0f);
            var planePoint = 0.5f * (v11 + v12);

            var tangent = MathUtils.Mul(xf1.Rotation, localTangent);
            var normal = MathUtils.Cross(tangent, 1.0f);

            v11 = MathUtils.Mul(xf1, v11);
            v12 = MathUtils.Mul(xf1, v12);

            // Face offset.
            var frontOffset = Vector2.Dot(normal, v11);

            // Side offsets, extended by polytope skin thickness.
            float sideRadius = poly1.Count > 2 && poly2.Count > 2 ? totalRadius : 0f;
            var sideOffset1 = -Vector2.Dot(tangent, v11) + sideRadius;
            var sideOffset2 = Vector2.Dot(tangent, v12) + sideRadius;

            // Clip incident edge against extruded edge1 side edges.
            Span<ClipVertex> clipPoints1 = stackalloc ClipVertex[2];
            Span<ClipVertex> clipPoints2 = stackalloc ClipVertex[2];

            // Clip to box side 1
            var np = CollisionUtils.ClipSegmentToLine(
                clipPoints1,
                incidentEdge,
                -tangent,
                sideOffset1,
                iv1);

            if (np < 2)
            {
                return;
            }

            // Clip to negative box side 1
            np = CollisionUtils.ClipSegmentToLine(
                clipPoints2,
                clipPoints1,
                tangent,
                sideOffset2,
                iv2);

            if (np < 2)
            {
                return;
            }

            // Now clipPoints2 contains the clipped points.
            manifold.LocalNormal = localNormal;
            manifold.LocalPoint = planePoint;

            var pointCount = 0;
            for (var i = 0; i < Settings.MaxManifoldPoints; ++i)
            {
                var separation = Vector2.Dot(normal, clipPoints2[i].Vector) - frontOffset;

                if (separation <= totalRadius)
                {
                    ref var cp = ref manifold.Points[pointCount];
                    cp.LocalPoint = MathUtils.MulT(xf2, clipPoints2[i].Vector);
                    cp.Id = clipPoints2[i].Id;
                    if (flip != default)
                    {
                        // Swap features
                        var cf = cp.Id.ContactFeature;
                        cp.Id.ContactFeature.IndexA = cf.IndexB;
                        cp.Id.ContactFeature.IndexB = cf.IndexA;
                        cp.Id.ContactFeature.TypeA = cf.TypeB;
                        cp.Id.ContactFeature.TypeB = cf.TypeA;
                    }

                    ++pointCount;
                }
            }

            manifold.PointCount = pointCount;
        }
    }
}
