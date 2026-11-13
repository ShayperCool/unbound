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

        internal static void ToAngleAxis(Quaternion q, out float angle, out Vector3 axis)
        {
            ToAxisAngleRad(q, out axis, out angle);
            angle *= Mathf.Rad2Deg;
        }

        internal static void ToAxisAngleRad(Quaternion q, out Vector3 axis, out float angle)
        {
            q = Normalize(q);
            double w = Math.Clamp((double)q.w, -1.0, 1.0);
            angle = (float)(2.0 * Math.Acos(w));
            double sinHalf = Math.Sqrt(1.0 - w * w);
            if (Mathf.Approximately(angle, 0.0f) || sinHalf < 1e-7)
            {
                axis = new Vector3(1F, 0F, 0F);
                return;
            }
            axis = new Vector3((float)(q.x / sinHalf), (float)(q.y / sinHalf), (float)(q.z / sinHalf));
        }

        internal static bool Equal(Quaternion a, Quaternion b) => Dot(a, b) > 1f - Quaternion.kEpsilon;

        internal static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Math.Min(Math.Abs(Dot(a, b)), 1f);
            return dot > 1f - Quaternion.kEpsilon ? 0f : (float)Math.Acos(dot) * 2f * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Spherical interpolation along the shorter arc in double precision, normalized: the engine's
        /// own blending of rotations (root motion, layer turns, body interpolation, agent turning), which the
        /// animation and physics probes measure. Unity's scripting Quaternion.Slerp is
        /// Quaternion.SlerpUnclamped, with its own single-precision order.
        /// </summary>
        internal static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
            double bx = b.x, by = b.y, bz = b.z, bw = b.w;
            if (dot < 0.0)
            {
                dot = -dot;
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
            }
            double wa, wb;
            if (dot > 0.9999)
            {
                wa = 1.0 - t;
                wb = t;
            }
            else
            {
                double theta = Math.Acos(Math.Min(dot, 1.0));
                double sinTheta = Math.Sin(theta);
                wa = Math.Sin((1.0 - t) * theta) / sinTheta;
                wb = Math.Sin(t * theta) / sinTheta;
            }
            double rx = wa * a.x + wb * bx;
            double ry = wa * a.y + wb * by;
            double rz = wa * a.z + wb * bz;
            double rw = wa * a.w + wb * bw;
            double n = Math.Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
            if (n < 1e-30)
                return Quaternion.identity;
            return new Quaternion((float)(rx / n), (float)(ry / n), (float)(rz / n), (float)(rw / n));
        }

        /// <summary>The rotation by <paramref name="degrees"/> about <paramref name="axis"/> with a
        /// double-precision half angle (identity for an axis not longer than 1e-5): the engine's own
        /// turning of bodies, transforms, vehicles, terrain trees and particles, which the physics and
        /// 2D probes measure. Unity's scripting Quaternion.AngleAxis has its own single-precision order.</summary>
        internal static Quaternion AngleAxis(float degrees, Vector3 axis)
        {
            float mag = axis.magnitude;
            if (mag <= Vector3.kEpsilon)
                return Quaternion.identity;
            double half = degrees * Mathf.Deg2Rad * 0.5;
            float s = (float)Math.Sin(half) / mag;
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(half));
        }

        /// <summary>Normalized linear interpolation along the shorter arc with the weights summed in
        /// single precision: the engine's constraint blending, which tests/projects/constraints measures.</summary>
        internal static Quaternion LerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            float sign = Dot(a, b) < 0F ? -1F : 1F;
            float u = 1F - t;
            float st = sign * t;
            return Normalize(new Quaternion(a.x * u + b.x * st, a.y * u + b.y * st, a.z * u + b.z * st, a.w * u + b.w * st));
        }

        internal static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            return angle == 0f ? to : Slerp(from, to, Math.Min(1f, maxDegreesDelta / angle));
        }
    }
}
