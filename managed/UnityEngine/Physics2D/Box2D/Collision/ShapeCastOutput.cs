using System.Numerics;

namespace Unbound.Box2D.Collision
{
    /// Output results for b2ShapeCast
    internal struct ShapeCastOutput
    {
        public Vector2 Point;

        public Vector2 Normal;

        public float Lambda;

        public int Iterations;
    }
}