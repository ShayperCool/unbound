using System.Numerics;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision
{
    /// Input parameters for b2ShapeCast
    internal struct ShapeCastInput
    {
        public DistanceProxy ProxyA;

        public DistanceProxy ProxyB;

        public Transform TransformA;

        public Transform TransformB;

        public Vector2 TranslationB;
    }
}