using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Quaternions represent rotations. Euler angles follow Unity's convention: degrees, applied
    /// around Z, then X, then Y (q = qY * qX * qZ).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Quaternion : IEquatable<Quaternion>, IFormattable
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public const float kEpsilon = 0.000001F;

        private static readonly Quaternion identityQuaternion = new Quaternion(0F, 0F, 0F, 1F);

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    case 3: return w;
                    default: throw new IndexOutOfRangeException("Invalid Quaternion index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    case 3: w = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Quaternion index!");
                }
            }
        }

        public void Set(float newX, float newY, float newZ, float newW)
        {
            x = newX;
            y = newY;
            z = newZ;
            w = newW;
        }

        public static Quaternion identity => identityQuaternion;

        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) =>
            new Quaternion(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);

        /// <summary>Rotates <paramref name="point"/> (assumes a unit quaternion).</summary>
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            float x2 = rotation.x * 2F;
            float y2 = rotation.y * 2F;
            float z2 = rotation.z * 2F;
            float xx = rotation.x * x2;
            float yy = rotation.y * y2;
            float zz = rotation.z * z2;
            float xy = rotation.x * y2;
            float xz = rotation.x * z2;
            float yz = rotation.y * z2;
            float wx = rotation.w * x2;
            float wy = rotation.w * y2;
            float wz = rotation.w * z2;

            Vector3 res;
            res.x = (1F - (yy + zz)) * point.x + (xy - wz) * point.y + (xz + wy) * point.z;
            res.y = (xy + wz) * point.x + (1F - (xx + zz)) * point.y + (yz - wx) * point.z;
            res.z = (xz - wy) * point.x + (yz + wx) * point.y + (1F - (xx + yy)) * point.z;
            return res;
        }

        private static bool IsEqualUsingDot(float dot) => dot > 1.0f - kEpsilon;

        /// <summary>Approximate equality: true if the dot product exceeds 1 - kEpsilon (q and -q differ).</summary>
        public static bool operator ==(Quaternion lhs, Quaternion rhs) => IsEqualUsingDot(Dot(lhs, rhs));

        public static bool operator !=(Quaternion lhs, Quaternion rhs) => !(lhs == rhs);

        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        /// <summary>The angle in degrees between two rotations.</summary>
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Math.Min(Math.Abs(Dot(a, b)), 1.0F);
            return IsEqualUsingDot(dot) ? 0.0f : (float)Math.Acos(dot) * 2.0F * Mathf.Rad2Deg;
        }

        public static Quaternion Inverse(Quaternion rotation) => new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);

        public static Quaternion Normalize(Quaternion q)
        {
            float mag = (float)Math.Sqrt(Dot(q, q));
            if (mag < Mathf.Epsilon)
                return identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        public void Normalize() => this = Normalize(this);

        public Quaternion normalized => Normalize(this);

        // ---- Euler angles ------------------------------------------------------------------

        public static Quaternion Euler(float x, float y, float z) => FromEulerRad(new Vector3(x, y, z) * Mathf.Deg2Rad);

        public static Quaternion Euler(Vector3 euler) => FromEulerRad(euler * Mathf.Deg2Rad);

        public Vector3 eulerAngles
        {
            get => MakePositive(ToEulerRad(this) * Mathf.Rad2Deg);
            set => this = FromEulerRad(value * Mathf.Deg2Rad);
        }

        // Rotation about Z, then X, then Y: q = qY * qX * qZ.
        internal static Quaternion FromEulerRad(Vector3 euler)
        {
            double hx = euler.x * 0.5, hy = euler.y * 0.5, hz = euler.z * 0.5;
            double cx = Math.Cos(hx), sx = Math.Sin(hx);
            double cy = Math.Cos(hy), sy = Math.Sin(hy);
            double cz = Math.Cos(hz), sz = Math.Sin(hz);
            return new Quaternion(
                (float)(cy * sx * cz + sy * cx * sz),
                (float)(sy * cx * cz - cy * sx * sz),
                (float)(cy * cx * sz - sy * sx * cz),
                (float)(cy * cx * cz + sy * sx * sz));
        }

        // Inverse of FromEulerRad, in radians, each angle in (-PI, PI]. In gimbal lock (x = +-90
        // degrees) z is 0 and y carries the whole remaining rotation.
        internal static Vector3 ToEulerRad(Quaternion rotation)
        {
            double qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w;
            double n = qx * qx + qy * qy + qz * qz + qw * qw;
            if (n < 1e-30)
                return Vector3.zero;
            double s = 2.0 / n;

            // Rotation matrix elements (row, column) needed for the ZXY extraction.
            double m00 = 1.0 - s * (qy * qy + qz * qz);
            double m01 = s * (qx * qy - qw * qz);
            double m02 = s * (qx * qz + qw * qy);
            double m10 = s * (qx * qy + qw * qz);
            double m11 = 1.0 - s * (qx * qx + qz * qz);
            double m12 = s * (qy * qz - qw * qx);
            double m22 = 1.0 - s * (qx * qx + qy * qy);

            double sinX = Math.Clamp(-m12, -1.0, 1.0);
            double cosX = Math.Sqrt(m10 * m10 + m11 * m11);
            double ex, ey, ez;
            if (cosX > 1e-6)
            {
                ex = Math.Atan2(sinX, cosX);
                ey = Math.Atan2(m02, m22);
                ez = Math.Atan2(m10, m11);
            }
            else if (sinX > 0)
            {
                ex = Math.PI * 0.5;
                ey = Math.Atan2(m01, m00);
                ez = 0.0;
            }
            else
            {
                ex = -Math.PI * 0.5;
                ey = Math.Atan2(-m01, m00);
                ez = 0.0;
            }
            return new Vector3((float)ex, (float)ey, (float)ez);
        }

        // Maps angles into [0, 360), leaving tiny negative values (above -0.0001 rad) untouched,
        // as Unity's eulerAngles does.
        private static Vector3 MakePositive(Vector3 euler)
        {
            const float negativeFlip = -0.0001f * Mathf.Rad2Deg;
            const float positiveFlip = 360.0f + negativeFlip;
            euler.x = MakePositive(euler.x, negativeFlip, positiveFlip);
            euler.y = MakePositive(euler.y, negativeFlip, positiveFlip);
            euler.z = MakePositive(euler.z, negativeFlip, positiveFlip);
            return euler;
        }

        private static float MakePositive(float angle, float negativeFlip, float positiveFlip)
        {
            if (angle < negativeFlip)
                return angle + 360.0f;
            if (angle > positiveFlip)
                return angle - 360.0f;
            return angle;
        }

        // ---- Axis / angle ------------------------------------------------------------------

        public static Quaternion AngleAxis(float angle, Vector3 axis) => AxisAngleRad(axis, angle * Mathf.Deg2Rad);

        private static Quaternion AxisAngleRad(Vector3 axis, float radians)
        {
            float mag = axis.magnitude;
            if (mag <= Vector3.kEpsilon)
                return identity;
            double half = radians * 0.5;
            float s = (float)Math.Sin(half) / mag;
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(half));
        }

        public void ToAngleAxis(out float angle, out Vector3 axis)
        {
            ToAxisAngleRad(this, out axis, out angle);
            angle *= Mathf.Rad2Deg;
        }

        private static void ToAxisAngleRad(Quaternion q, out Vector3 axis, out float angle)
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

        // ---- Look / from-to ----------------------------------------------------------------

        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection)
        {
            Vector3 from = Vector3.Normalize(fromDirection);
            Vector3 to = Vector3.Normalize(toDirection);
            if (from == Vector3.zero || to == Vector3.zero)
                return identity;
            FromToMatrix(from, to, out double[,] m);
            return FromMatrix(m);
        }

        public void SetFromToRotation(Vector3 fromDirection, Vector3 toDirection) => this = FromToRotation(fromDirection, toDirection);

        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);

        /// <summary>
        /// The rotation whose Z axis points along <paramref name="forward"/> and whose Y axis lies in
        /// the plane of forward and <paramref name="upwards"/>. A zero forward gives identity; a
        /// forward parallel to upwards gives the shortest rotation from Z to forward.
        /// </summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            double fx = forward.x, fy = forward.y, fz = forward.z;
            double fMag = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            if (fMag <= Vector3.kEpsilon)
                return identity;
            fx /= fMag;
            fy /= fMag;
            fz /= fMag;

            // right = up x forward
            double rx = upwards.y * fz - upwards.z * fy;
            double ry = upwards.z * fx - upwards.x * fz;
            double rz = upwards.x * fy - upwards.y * fx;
            double rMag = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            double upMag = Math.Sqrt((double)upwards.x * upwards.x + (double)upwards.y * upwards.y + (double)upwards.z * upwards.z);
            if (rMag <= Vector3.kEpsilon * Math.Max(upMag, 1.0))
                return FromToRotation(Vector3.forward, new Vector3((float)fx, (float)fy, (float)fz));
            rx /= rMag;
            ry /= rMag;
            rz /= rMag;

            // up' = forward x right
            double ux = fy * rz - fz * ry;
            double uy = fz * rx - fx * rz;
            double uz = fx * ry - fy * rx;

            var m = new double[3, 3]
            {
                { rx, ux, fx },
                { ry, uy, fy },
                { rz, uz, fz },
            };
            return FromMatrix(m);
        }

        public void SetLookRotation(Vector3 view)
        {
            Vector3 up = Vector3.up;
            SetLookRotation(view, up);
        }

        public void SetLookRotation(Vector3 view, Vector3 up) => this = LookRotation(view, up);

        // Rotation matrix taking unit vector 'from' to unit vector 'to' (Moller & Hughes, "Efficiently
        // Building a Matrix to Rotate One Vector to Another", 1999).
        private static void FromToMatrix(Vector3 from, Vector3 to, out double[,] m)
        {
            double[] f = { from.x, from.y, from.z };
            double[] t = { to.x, to.y, to.z };
            double e = f[0] * t[0] + f[1] * t[1] + f[2] * t[2];
            m = new double[3, 3];
            if (Math.Abs(e) > 1.0 - 1e-6)
            {
                // Nearly parallel: reflect through the plane of a coordinate axis far from 'from'.
                double[] axis = new double[3];
                double ax = Math.Abs(f[0]), ay = Math.Abs(f[1]), az = Math.Abs(f[2]);
                if (ax < ay)
                {
                    if (ax < az)
                        axis[0] = 1.0;
                    else
                        axis[2] = 1.0;
                }
                else
                {
                    if (ay < az)
                        axis[1] = 1.0;
                    else
                        axis[2] = 1.0;
                }
                double[] u = new double[3], v = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    u[i] = axis[i] - f[i];
                    v[i] = axis[i] - t[i];
                }
                double c1 = 2.0 / (u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
                double c2 = 2.0 / (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
                double c3 = c1 * c2 * (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]);
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                        m[i, j] = -c1 * u[i] * u[j] - c2 * v[i] * v[j] + c3 * v[i] * u[j];
                    m[i, i] += 1.0;
                }
            }
            else
            {
                double vx = f[1] * t[2] - f[2] * t[1];
                double vy = f[2] * t[0] - f[0] * t[2];
                double vz = f[0] * t[1] - f[1] * t[0];
                double h = 1.0 / (1.0 + e);
                double hvx = h * vx;
                double hvz = h * vz;
                double hvxy = hvx * vy;
                double hvxz = hvx * vz;
                double hvyz = h * vy * vz;
                m[0, 0] = e + hvx * vx;
                m[0, 1] = hvxy - vz;
                m[0, 2] = hvxz + vy;
                m[1, 0] = hvxy + vz;
                m[1, 1] = e + h * vy * vy;
                m[1, 2] = hvyz - vx;
                m[2, 0] = hvxz - vy;
                m[2, 1] = hvyz + vx;
                m[2, 2] = e + hvz * vz;
            }
        }

        // Rotation matrix (row, column) to quaternion (Shoemake). The trace branch yields w > 0.
        internal static Quaternion FromMatrix(double[,] m)
        {
            double trace = m[0, 0] + m[1, 1] + m[2, 2];
            double qx, qy, qz, qw;
            if (trace > 0.0)
            {
                double s = Math.Sqrt(trace + 1.0);
                qw = 0.5 * s;
                s = 0.5 / s;
                qx = (m[2, 1] - m[1, 2]) * s;
                qy = (m[0, 2] - m[2, 0]) * s;
                qz = (m[1, 0] - m[0, 1]) * s;
            }
            else
            {
                int i = 0;
                if (m[1, 1] > m[0, 0])
                    i = 1;
                if (m[2, 2] > m[i, i])
                    i = 2;
                int j = (i + 1) % 3;
                int k = (j + 1) % 3;
                double s = Math.Sqrt(m[i, i] - m[j, j] - m[k, k] + 1.0);
                double[] q = new double[3];
                q[i] = 0.5 * s;
                s = 0.5 / s;
                qw = (m[k, j] - m[j, k]) * s;
                q[j] = (m[j, i] + m[i, j]) * s;
                q[k] = (m[k, i] + m[i, k]) * s;
                qx = q[0];
                qy = q[1];
                qz = q[2];
            }
            double n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            return new Quaternion((float)(qx / n), (float)(qy / n), (float)(qz / n), (float)(qw / n));
        }

        // ---- Interpolation -----------------------------------------------------------------

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => SlerpUnclamped(a, b, Mathf.Clamp01(t));

        /// <summary>Spherical interpolation along the shorter arc; the result is normalized.</summary>
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
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
                return identity;
            return new Quaternion((float)(rx / n), (float)(ry / n), (float)(rz / n), (float)(rw / n));
        }

        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => LerpUnclamped(a, b, Mathf.Clamp01(t));

        /// <summary>Normalized linear interpolation along the shorter arc.</summary>
        public static Quaternion LerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            float sign = Dot(a, b) < 0F ? -1F : 1F;
            float u = 1F - t;
            float st = sign * t;
            Quaternion r = new Quaternion(a.x * u + b.x * st, a.y * u + b.y * st, a.z * u + b.z * st, a.w * u + b.w * st);
            return Normalize(r);
        }

        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            if (angle == 0.0f)
                return to;
            return SlerpUnclamped(from, to, Math.Min(1.0f, maxDegreesDelta / angle));
        }

        // ---- Object overrides --------------------------------------------------------------

        public override int GetHashCode() =>
            x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);

        public override bool Equals(object other) => other is Quaternion q && Equals(q);

        public bool Equals(Quaternion other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F5");
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + MathFormat.F(x, format, formatProvider) + ", " + MathFormat.F(y, format, formatProvider) + ", " +
                   MathFormat.F(z, format, formatProvider) + ", " + MathFormat.F(w, format, formatProvider) + ")";
        }

        // ---- Obsolete radian-based API -----------------------------------------------------

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerRotation(float x, float y, float z) => FromEulerRad(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerRotation(Vector3 euler) => FromEulerRad(euler);

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerRotation(float x, float y, float z) => this = FromEulerRad(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerRotation(Vector3 euler) => this = FromEulerRad(euler);

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public Vector3 ToEuler() => ToEulerRad(this);

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerAngles(float x, float y, float z) => FromEulerRad(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerAngles(Vector3 euler) => FromEulerRad(euler);

        [Obsolete("Use Quaternion.ToAngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public void ToAxisAngle(out Vector3 axis, out float angle) => ToAxisAngleRad(this, out axis, out angle);

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerAngles(float x, float y, float z) => SetEulerRotation(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerAngles(Vector3 euler) => this = EulerRotation(euler);

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Vector3 ToEulerAngles(Quaternion rotation) => ToEulerRad(rotation);

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public Vector3 ToEulerAngles() => ToEulerRad(this);

        [Obsolete("Use Quaternion.AngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetAxisAngle(Vector3 axis, float angle) => this = AxisAngleRad(axis, angle);

        [Obsolete("Use Quaternion.AngleAxis instead. This function was deprecated because it uses radians instead of degrees")]
        public static Quaternion AxisAngle(Vector3 axis, float angle) => AxisAngleRad(axis, angle);
    }
}
