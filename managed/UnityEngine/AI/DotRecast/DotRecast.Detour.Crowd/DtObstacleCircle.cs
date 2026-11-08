using DotRecast.Core.Numerics;

namespace DotRecast.Detour.Crowd
{
    /// < Max number of adaptive rings.
    internal class DtObstacleCircle
    {
        /** Position of the obstacle (Unbound: relative to the avoiding agent) */
        public RcVec3f p = new RcVec3f();
        /** Velocity of the obstacle */
        public RcVec3f vel = new RcVec3f();
        /** Radius of the obstacle (Unbound: with the avoiding agent's) */
        public float rad;
        /** Unbound: the obstacle's share in the relative velocity (Unity's responsibility weight). */
        public float weight;
    }
}