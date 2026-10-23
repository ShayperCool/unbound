using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision
{
    /// Input for b2Distance.
    /// You have to option to use the shape radii
    /// in the computation. Even 
    internal struct DistanceInput
    {
        public DistanceProxy ProxyA;

        public DistanceProxy ProxyB;

        public Transform TransformA;

        public Transform TransformB;

        public bool UseRadii;
    }
}