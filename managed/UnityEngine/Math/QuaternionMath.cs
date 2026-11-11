using System;

namespace UnityEngine
{
    // Engine calculations keep single-precision products and sums independently of the
    // script API. Caller coverage and unmeasured contexts are listed in quaternioneuler.
    internal static class QuaternionMath
    {
        internal static float Dot(Quaternion a, Quaternion b) =>
            a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        internal static Quaternion Normalize(Quaternion q)
        {
            float mag = (float)Math.Sqrt(Dot(q, q));
            if (mag < Mathf.Epsilon)
                return Quaternion.identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        internal static bool Equal(Quaternion a, Quaternion b) => Dot(a, b) > 1f - Quaternion.kEpsilon;

        internal static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Math.Min(Math.Abs(Dot(a, b)), 1f);
            return dot > 1f - Quaternion.kEpsilon ? 0f : (float)Math.Acos(dot) * 2f * Mathf.Rad2Deg;
        }

        internal static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            return angle == 0f ? to : Quaternion.SlerpUnclamped(from, to, Math.Min(1f, maxDegreesDelta / angle));
        }
    }
}
