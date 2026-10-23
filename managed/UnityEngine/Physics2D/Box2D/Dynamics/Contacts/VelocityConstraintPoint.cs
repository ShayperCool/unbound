using System.Numerics;

namespace Unbound.Box2D.Dynamics.Contacts
{
    internal struct VelocityConstraintPoint
    {
        public float NormalImpulse;

        public float NormalMass;

        public Vector2 Ra;

        public Vector2 Rb;

        public float TangentImpulse;

        public float TangentMass;

        public float VelocityBias;
    }
}