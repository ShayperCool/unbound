using System;

namespace UnityEngine
{
    /// <summary>A position and a rotation.</summary>
    [Serializable]
    public partial struct Pose : IEquatable<Pose>
    {
        public Vector3 position;
        public Quaternion rotation;

        public Pose(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public override string ToString() => "(" + position.ToString() + ", " + rotation.ToString() + ")";

        public string ToString(string format) => "(" + position.ToString(format) + ", " + rotation.ToString(format) + ")";

        public Pose GetTransformedBy(Pose lhs) => new Pose
        {
            position = lhs.position + (lhs.rotation * position),
#if UNBOUND_SCRIPT_MATH
            rotation = lhs.rotation * rotation,
#else
            rotation = ComposeRotation(lhs.rotation, rotation),
#endif
        };

#if !UNBOUND_SCRIPT_MATH // Transform uses the engine's math types
        public Pose GetTransformedBy(Transform lhs)
        {
            if (lhs is null && Unbound.Runtime.Compat.PoseNullTransformMeasured)
                throw Unbound.Runtime.MonoExceptions.NullReference();
            return new Pose
            {
                position = lhs.TransformPoint(position),
                rotation = ComposeRotation(lhs.rotation, rotation),
            };
        }

        static Quaternion ComposeRotation(Quaternion lhs, Quaternion rhs)
        {
            if (!Unbound.Runtime.Compat.QuaternionApiPrecisionMeasured)
                return lhs * rhs;

            // The measured Unity 6000.0.71f1 Pose compositions round each rotation component
            // after adding its terms (tests/projects/oroorder, both overloads).
            double x = lhs.x, y = lhs.y, z = lhs.z, w = lhs.w;
            return new Quaternion(
                (float)(w * rhs.x + x * rhs.w + y * rhs.z - z * rhs.y),
                (float)(w * rhs.y + y * rhs.w + z * rhs.x - x * rhs.z),
                (float)(w * rhs.z + z * rhs.w + x * rhs.y - y * rhs.x),
                (float)(w * rhs.w - x * rhs.x - y * rhs.y - z * rhs.z));
        }
#endif

        public Vector3 forward => rotation * Vector3.forward;

        public Vector3 right => rotation * Vector3.right;

        public Vector3 up => rotation * Vector3.up;

        static readonly Pose k_Identity = new Pose(Vector3.zero, Quaternion.identity);

        public static Pose identity => k_Identity;

        public override bool Equals(object obj) => obj is Pose other && Equals(other);

        /// <summary>Exact: both vectors' and quaternions' components compare with float.Equals.</summary>
        public bool Equals(Pose other) => position.Equals(other.position) && rotation.Equals(other.rotation);

        public override int GetHashCode() => position.GetHashCode() ^ (rotation.GetHashCode() << 1);

        /// <summary>The position compares approximately (Vector3 ==), the rotation exactly.</summary>
        public static bool operator ==(Pose a, Pose b) => a.position == b.position && a.rotation.Equals(b.rotation);

        public static bool operator !=(Pose a, Pose b) => !(a == b);
    }
}
