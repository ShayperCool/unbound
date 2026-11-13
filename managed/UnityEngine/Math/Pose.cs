using System;

namespace UnityEngine
{
    /// <summary>A position and a rotation. Its math is the Quaternion and Vector3 operators' and
    /// Transform.TransformPoint's, so the results are theirs to the bit (tests/projects/coreapis).</summary>
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
            rotation = lhs.rotation * rotation,
        };

#if !UNBOUND_SCRIPT_MATH // Transform uses the engine's math types
        public Pose GetTransformedBy(Transform lhs) => new Pose
        {
            position = lhs.TransformPoint(position),
            rotation = lhs.rotation * rotation,
        };
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
