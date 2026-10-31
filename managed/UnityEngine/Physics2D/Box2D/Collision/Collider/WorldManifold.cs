using System;
using System.Numerics;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision.Collider
{
    /// This is used to compute the current state of a contact manifold.
    internal struct WorldManifold
    {
        /// Evaluate the manifold with supplied transforms. This assumes
        /// modest motion from the original state. This does not change the
        /// point count, impulses, etc. The radii must come from the shapes
        /// that generated the manifold.
        public void Initialize(
            in Manifold manifold,
            in Transform xfA,
            float radiusA,
            in Transform xfB,
            float radiusB)
        {
            if (manifold.PointCount == 0)
            {
                return;
            }

            switch (manifold.Type)
            {
            case ManifoldType.Circles:
            {
                Normal.Set(1.0f, 0.0f);
                var pointA = MathUtils.Mul(xfA, manifold.LocalPoint);
                var pointB = MathUtils.Mul(xfB, manifold.Points.Value0.LocalPoint);
                if (Vector2.DistanceSquared(pointA, pointB) > Settings.Epsilon * Settings.Epsilon)
                {
                    Normal = pointB - pointA;
                    Normal.Normalize();
                }

                var cA = pointA + radiusA * Normal;
                var cB = pointB - radiusB * Normal;
                Points.Value0 = 0.5f * (cA + cB);
                Separations.Value0 = Vector2.Dot(cB - cA, Normal);
            }
                break;

            case ManifoldType.FaceA:
            {
                Normal = MathUtils.Mul(xfA.Rotation, manifold.LocalNormal);
                var planePoint = MathUtils.Mul(xfA, manifold.LocalPoint);

                for (var i = 0; i < manifold.PointCount; ++i)
                {
                    var clipPoint = MathUtils.Mul(xfB, manifold.Points[i].LocalPoint);
                    var cA = clipPoint + (radiusA - Vector2.Dot(clipPoint - planePoint, Normal)) * Normal;
                    var cB = clipPoint - radiusB * Normal;
                    Points[i] = 0.5f * (cA + cB);
                    Separations[i] = Vector2.Dot(cB - cA, Normal);
                }
            }
                break;

            case ManifoldType.FaceB:
            {
                Normal = MathUtils.Mul(xfB.Rotation, manifold.LocalNormal);
                var planePoint = MathUtils.Mul(xfB, manifold.LocalPoint);

                for (var i = 0; i < manifold.PointCount; ++i)
                {
                    var clipPoint = MathUtils.Mul(xfA, manifold.Points[i].LocalPoint);
                    var cB = clipPoint + (radiusB - Vector2.Dot(clipPoint - planePoint, Normal)) * Normal;
                    var cA = clipPoint - radiusA * Normal;
                    Points[i] = 0.5f * (cA + cB);
                    Separations[i] = Vector2.Dot(cA - cB, Normal);
                }

                // Ensure normal points from A to B.
                Normal = -Normal;
            }
                break;
            }
        }

        /// Choose the deepest contact, averaging two contacts only within the
        /// caller's separation tolerance. Casts may supply the core spread before
        /// the polygon skin extends the clipping sides.
        public Vector2 GetRepresentativePoint(int pointCount, float separationSpread, float averagingTolerance)
        {
            return pointCount == 2
                ? SelectPoint(Points[0], Points[1], Separations[0], Separations[1],
                    separationSpread, averagingTolerance, out _)
                : Points[0];
        }

        public static Vector2 SelectPoint(Vector2 point0, Vector2 point1, float separation0, float separation1,
            float separationSpread, float averagingTolerance, out float separation)
        {
            separation = Math.Min(separation0, separation1);
            if (separationSpread <= averagingTolerance)
            {
                separation = 0.5f * (separation0 + separation1);
                return 0.5f * (point0 + point1);
            }
            return separation1 < separation0 ? point1 : point0;
        }

        /// world vector pointing from A to B
        public Vector2 Normal;

        /// <summary>
        /// world contact point (point of intersection), size Settings.MaxManifoldPoints
        /// </summary>
        public FixedArray2<Vector2> Points;

        /// <summary>
        /// a negative value indicates overlap, in meters, size Settings.MaxManifoldPoints
        /// </summary>
        public FixedArray2<float> Separations;
    }
}