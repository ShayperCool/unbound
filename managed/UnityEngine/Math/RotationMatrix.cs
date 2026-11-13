using System;

namespace UnityEngine
{
    /// <summary>
    /// Single-precision 3x3 rotation matrices (nine floats, column-major: element (row, column) at
    /// [column * 3 + row]) behind the rotation methods Unity binds to native code (Quaternion
    /// LookRotation, FromToRotation and eulerAngles, Vector3 Slerp and RotateTowards, Matrix4x4
    /// rotation). Each operation keeps the order of the Unity 6000.0.71f1 Editor's float arithmetic,
    /// so results match it bit for bit (tests/projects/floatrounding, `native/` rows).
    /// </summary>
    internal static class RotationMatrix
    {
        /// <summary>Length below which a direction counts as zero.</summary>
        internal const float MinimumLength = 1e-5f;

        /// <summary>The next axis after each axis, cyclically.</summary>
        static ReadOnlySpan<byte> NextAxis => new byte[] { 1, 2, 0 };

        internal static float Length(float x, float y, float z) => MathF.Sqrt(x * x + y * y + z * z);

        /// <summary>The identity matrix.</summary>
        internal static void Identity(Span<float> m)
        {
            m.Clear();
            m[0] = 1f;
            m[4] = 1f;
            m[8] = 1f;
        }

        /// <summary>
        /// The rotation taking unit vector <paramref name="from"/> to unit vector <paramref name="to"/>
        /// (Möller and Hughes, "Efficiently Building a Matrix to Rotate One Vector to Another"):
        /// identity when they are nearly equal, a half turn about an axis orthogonal to
        /// <paramref name="from"/> when they are nearly opposite.
        /// </summary>
        internal static void FromTo(Vector3 from, Vector3 to, Span<float> m)
        {
            // The parallel test compares in double against 1 - 1e-6f.
            const double parallel = 1.0 - (double)1e-6f;
            float e = from.x * to.x + from.y * to.y + from.z * to.z;
            if (e > parallel)
            {
                Identity(m);
                return;
            }
            if (e < -parallel)
            {
                // left = from x (1, 0, 0), or from x (0, 1, 0) when that is too short.
                const float minimumSquaredLength = 1e-6f;
                float lx = 0f, ly = from.z, lz = -from.y;
                if (from.z * from.z + from.y * from.y < minimumSquaredLength)
                {
                    lx = -from.z;
                    ly = 0f;
                    lz = from.x;
                }
                float k = 1f / Length(lx, ly, lz);
                lx *= k;
                ly *= k;
                lz *= k;
                float ux = from.z * ly - from.y * lz;
                float uy = from.x * lz - from.z * lx;
                float uz = from.y * lx - from.x * ly;
                m[0] = ux * ux - from.x * from.x - lx * lx;
                m[1] = m[3] = ux * uy - from.x * from.y - lx * ly;
                m[2] = m[6] = ux * uz - from.x * from.z - lx * lz;
                m[4] = uy * uy - from.y * from.y - ly * ly;
                m[5] = m[7] = uy * uz - from.z * from.y - ly * lz;
                m[8] = uz * uz - from.z * from.z - lz * lz;
                return;
            }
            float vx = from.y * to.z - from.z * to.y;
            float vy = from.z * to.x - to.z * from.x;
            float vz = to.y * from.x - from.y * to.x;
            float h = (1f - e) / (vx * vx + vy * vy + vz * vz);
            float hvx = vx * h;
            float hvz = vz * h;
            float hvxy = vy * hvx;
            float hvxz = vz * hvx;
            float hvyz = vy * hvz;
            m[0] = e + vx * hvx;
            m[1] = vz + hvxy;
            m[2] = hvxz - vy;
            m[3] = hvxy - vz;
            m[4] = e + vy * (vy * h);
            m[5] = vx + hvyz;
            m[6] = vy + hvxz;
            m[7] = hvyz - vx;
            m[8] = e + vz * hvz;
        }

        /// <summary>
        /// The rotation whose Z axis is <paramref name="view"/> and whose Y axis lies in the plane of
        /// view and <paramref name="up"/>. False (and <paramref name="m"/> undefined) when view is
        /// shorter than <see cref="MinimumLength"/>, view and up are parallel, or the result is not
        /// orthonormal.
        /// </summary>
        internal static bool Look(Vector3 view, Vector3 up, Span<float> m)
        {
            const float unitTolerance = 1e-6f;
            float length = Length(view.x, view.y, view.z);
            if (length < MinimumLength)
                return false;
            float zx = view.x / length, zy = view.y / length, zz = view.z / length;
            float xx = zz * up.y - zy * up.z;
            float xy = zx * up.z - zz * up.x;
            float xz = zy * up.x - zx * up.y;
            float xLength = Length(xx, xy, xz);
            if (xLength < MinimumLength)
                return false;
            xx /= xLength;
            xy /= xLength;
            xz /= xLength;
            float yx = zy * xz - zz * xy;
            float yy = zz * xx - zx * xz;
            float yz = zx * xy - zy * xx;
            if (!(MathF.Abs(yx * yx + yy * yy + yz * yz - 1f) <= unitTolerance))
                return false;
            m[0] = xx;
            m[1] = xy;
            m[2] = xz;
            m[3] = yx;
            m[4] = yy;
            m[5] = yz;
            m[6] = zx;
            m[7] = zy;
            m[8] = zz;
            return true;
        }

        /// <summary>The rotation by <paramref name="angle"/> radians about the unit <paramref name="axis"/>.</summary>
        internal static void AxisAngle(Vector3 axis, float angle, Span<float> m)
        {
            var (s, c) = MathF.SinCos(angle);
            float x = axis.x, y = axis.y, z = axis.z;
            float xx = x * x, yy = y * y, zz = z * z;
            float xy = x * y, yz = y * z, xz = x * z;
            float xs = s * x, ys = s * y, zs = s * z;
            float t = 1f - c;
            m[0] = c + t * xx;
            m[1] = t * xy + zs;
            m[2] = t * xz - ys;
            m[3] = t * xy - zs;
            m[4] = c + t * yy;
            m[5] = xs + t * yz;
            m[6] = ys + t * xz;
            m[7] = t * yz - xs;
            m[8] = c + t * zz;
        }

        /// <summary>The matrix times the column vector.</summary>
        internal static Vector3 Multiply(ReadOnlySpan<float> m, Vector3 v) => new Vector3(
            m[0] * v.x + m[3] * v.y + m[6] * v.z,
            m[1] * v.x + m[4] * v.y + m[7] * v.z,
            m[2] * v.x + m[5] * v.y + m[8] * v.z);

        internal static float Determinant(ReadOnlySpan<float> m) =>
            m[0] * m[4] * m[8] + m[3] * m[7] * m[2] + m[6] * m[1] * m[5]
            - m[2] * (m[4] * m[6]) - m[8] * (m[3] * m[1]) - m[0] * m[7] * m[5];

        /// <summary>
        /// The rotation of an orthonormal matrix (Shoemake: from the trace when it is positive, else
        /// from the largest diagonal element), normalized.
        /// </summary>
        internal static Quaternion ToQuaternion(ReadOnlySpan<float> m)
        {
            Span<float> q = stackalloc float[4];
            float trace = m[0] + m[4] + m[8];
            if (trace > 0f)
            {
                float root = MathF.Sqrt(trace + 1f);
                q[3] = root * 0.5f;
                float s = 0.5f / root;
                q[0] = s * (m[5] - m[7]);
                q[1] = s * (m[6] - m[2]);
                q[2] = s * (m[1] - m[3]);
            }
            else
            {
                int i = 0;
                if (m[4] > m[0])
                    i = 1;
                if (m[8] > m[i * 4])
                    i = 2;
                int j = NextAxis[i];
                int k = NextAxis[j];
                float root = MathF.Sqrt(m[i * 4] - m[j * 4] - m[k * 4] + 1f);
                q[i] = root * 0.5f;
                float s = 0.5f / root;
                q[3] = s * (m[j * 3 + k] - m[k * 3 + j]);
                q[j] = s * (m[i * 3 + j] + m[j * 3 + i]);
                q[k] = s * (m[i * 3 + k] + m[k * 3 + i]);
            }
            float length = MathF.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            return new Quaternion(q[0] / length, q[1] / length, q[2] / length, q[3] / length);
        }
    }
}
