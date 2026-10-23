using System.Numerics;

namespace Unbound.Box2D.Collision.Collider
{
    /// Used for computing contact manifolds.
    internal struct ClipVertex
    {
        public Vector2 Vector;

        public ContactId Id;
    }
}