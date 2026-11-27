using DotRecast.Core.Numerics;

namespace DotRecast.Detour.Crowd
{
    internal class DtObstacleSegment
    {
        /** End points of the obstacle segment (Unbound: relative to the avoiding agent) */
        public RcVec3f p = new RcVec3f();
        /** End points of the obstacle segment */
        public RcVec3f q = new RcVec3f();
    }

    /// <summary>A padded moving horizontal segment contributed by a capsule or box obstacle
    /// during velocity sampling (relative to the avoiding agent).</summary>
    internal struct DtObstacleMovingSegment
    {
        public float p0x;
        public float p0z;
        public float p1x;
        public float p1z;
        public float velX;
        public float velZ;
        public float midX;
        public float midZ;
        public float dirX;
        public float dirZ;
        public float halfLen;
        public float rad;
        public float weight;
    }
}