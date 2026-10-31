using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>A four-float vector for the humanoid math (quaternions are (x, y, z, w)); products
    /// and normalizations keep the operation order that reproduces the Editor's results
    /// (tests/projects/humanoid).</summary>
    internal struct F4
    {
        public float x, y, z, w;

        public F4(float x, float y, float z, float w)
        {
            this.x = x; this.y = y; this.z = z; this.w = w;
        }

        public static readonly F4 Zero = new F4(0, 0, 0, 0);
        public static readonly F4 QIdentity = new F4(0, 0, 0, 1);
        public static readonly F4 One3 = new F4(1, 1, 1, 0);
        public static readonly F4 One = new F4(1, 1, 1, 1);

        public static F4 operator +(F4 a, F4 b) => new F4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        public static F4 operator -(F4 a, F4 b) => new F4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        public static F4 operator -(F4 a) => new F4(-a.x, -a.y, -a.z, -a.w);
        public static F4 operator *(F4 a, F4 b) => new F4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        public static F4 operator *(F4 a, float s) => new F4(a.x * s, a.y * s, a.z * s, a.w * s);
        public static F4 operator *(float s, F4 a) => new F4(a.x * s, a.y * s, a.z * s, a.w * s);
        public static F4 operator /(F4 a, F4 b) => new F4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);
        public static F4 operator /(F4 a, float s) => new F4(a.x / s, a.y / s, a.z / s, a.w / s);

        public F4 Xyz0 => new F4(x, y, z, 0);

        public float this[int i]
        {
            get => i switch { 0 => x, 1 => y, 2 => z, _ => w };
            set
            {
                switch (i)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default: w = value; break;
                }
            }
        }

        public override string ToString() => $"({x}, {y}, {z}, {w})";
    }

    /// <summary>A transform: translation, rotation, scale.</summary>
    internal struct Trs
    {
        public F4 t, q, s;

        public Trs(F4 t, F4 q, F4 s)
        {
            this.t = t; this.q = q; this.s = s;
        }

        public static readonly Trs Identity = new Trs(F4.Zero, F4.QIdentity, F4.One3);
    }

    internal static class M
    {
        public const float Epsilon = 1e-15f;

        public static float Dot3(F4 a, F4 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        /// <summary>The length of the xyz part, summed as (y² + x²) + (z² + 0).</summary>
        public static float Length3(F4 v)
        {
            float a = v.y * v.y + v.x * v.x;
            float b = v.z * v.z + 0f;
            return MathF.Sqrt(b + a);
        }

        public static F4 Cross(F4 a, F4 b) =>
            new F4(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x, 0);

        public static F4 Normalize3(F4 v)
        {
            float l = Length3(v);
            return new F4(v.x / l, v.y / l, v.z / l, 0);
        }

        /// <summary>A normalized quaternion: q * (1 / sqrt((w² + z²) + (y² + x²))).</summary>
        public static F4 QNormalize(F4 q)
        {
            float a0 = q.y * q.y + q.x * q.x, a1 = q.z * q.z + q.y * q.y, a2 = q.w * q.w + q.z * q.z, a3 = q.x * q.x + q.w * q.w;
            return new F4(1f / MathF.Sqrt(a2 + a0) * q.x, 1f / MathF.Sqrt(a3 + a1) * q.y, 1f / MathF.Sqrt(a0 + a2) * q.z, 1f / MathF.Sqrt(a1 + a3) * q.w);
        }

        public static F4 QConj(F4 q) => new F4(-q.x, -q.y, -q.z, q.w);

        /// <summary>The quaternion product l * r.</summary>
        public static F4 QMul(F4 l, F4 r) => new F4(
            -(((l.z * r.y - l.y * r.z) - l.w * r.x) - l.x * r.w),
            -(((l.x * r.z - l.z * r.x) - l.w * r.y) - l.y * r.w),
            -(((l.y * r.x - l.w * r.z) - l.z * r.w) - l.x * r.y),
            ((l.w * r.w - l.x * r.x) - l.z * r.z) - l.y * r.y);

        /// <summary>v rotated by q.</summary>
        public static F4 QRotate(F4 q, F4 v)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            return new F4(
                v.z * (x * 2f * z + y * 2f * w) + v.y * (x * 2f * y - z * 2f * w) + v.x * (-(y * 2f * y) - z * 2f * z) + v.x,
                v.z * (y * 2f * z - x * 2f * w) + v.y * (-(z * 2f * z) - x * 2f * x) + v.x * (x * 2f * y + z * 2f * w) + v.y,
                v.z * (-(x * 2f * x) - y * 2f * y) + v.y * (y * 2f * z + x * 2f * w) + v.x * (x * 2f * z - y * 2f * w) + v.z,
                0);
        }

        /// <summary>A reciprocal that is 0 where |v| is below 1e-9 (the w
        /// component's threshold is 0).</summary>
        public static F4 RcpSafe(F4 v) => new F4(Rcp(v.x, 1e-9f), Rcp(v.y, 1e-9f), Rcp(v.z, 1e-9f), Rcp(v.w, 0f));

        static float Rcp(float v, float eps) => MathF.Abs(v) < eps ? 0f : 1f / v;

        /// <summary>The transform a * b (a parent, b local).</summary>
        public static Trs Mul(in Trs a, in Trs b) =>
            new Trs(QRotate(a.q, b.t * a.s) + a.t, QMul(a.q, b.q), a.s * b.s);

        /// <summary>The transform b in a's space.</summary>
        public static Trs InvMul(in Trs a, in Trs b)
        {
            var inv = RcpSafe(a.s);
            var qc = QConj(a.q);
            return new Trs(QRotate(qc, b.t - a.t) * inv, QMul(qc, b.q), b.s * inv);
        }

        /// <summary>A position in a's space.</summary>
        public static F4 InvMulPoint(in Trs a, F4 p) => QRotate(QConj(a.q), p - a.t) * RcpSafe(a.s);

        public static F4 MulPoint(in Trs a, F4 p) => QRotate(a.q, p * a.s) + a.t;

        /// <summary>The rotation whose matrix has columns x, y, z (Unity's branches on the diagonal),
        /// normalized.</summary>
        public static F4 QFromColumns(F4 X, F4 Y, F4 Z)
        {
            float m00 = X.x, m11 = Y.y, m22 = Z.z;
            float qx, qy, qz, qw;
            if (0f <= m00)
            {
                float t = m11 + m22;
                if (0f <= t)
                {
                    qx = Y.z - Z.y; qy = Z.x - X.z; qz = X.y - Y.x; qw = 1f + m00 + t;
                }
                else
                {
                    qx = (1f + m00) - t; qy = X.y + Y.x; qz = X.z + Z.x; qw = Y.z - Z.y;
                }
            }
            else
            {
                float d = m11 - m22;
                if (0f <= d)
                {
                    qx = X.y + Y.x; qy = (1f - m00) + d; qz = Z.y + Y.z; qw = Z.x - X.z;
                }
                else
                {
                    qx = X.z + Z.x; qy = Y.z + Z.y; qz = (1f - m00) - d; qw = X.y - Y.x;
                }
            }
            return QNormalize(new F4(qx, qy, qz, qw));
        }

        /// <summary>The tangents of the half angles, q.xyz / q.w (/ 1e-6 when |w| is
        /// below 1e-6).</summary>
        public static F4 Quat2Qtan(F4 q)
        {
            if (MathF.Abs(q.w) < 1e-6f) return new F4(q.x / 1e-6f, q.y / 1e-6f, q.z / 1e-6f, 0);
            return new F4(q.x / q.w, q.y / q.w, q.z / q.w, 1);
        }

        public static F4 Qtan2Quat(F4 qtan) => QNormalize(new F4(qtan.x, qtan.y, qtan.z, 1f));

        public static F4 Quat2ZYRoll(F4 q)
        {
            var t = Quat2Qtan(q);
            float d = t.x * t.x + 1f;
            return new F4(t.x, (t.y - t.z * t.x) / d, (t.y * t.x + t.z) / d, 0);
        }

        public static F4 Quat2RollZY(F4 q)
        {
            var t = Quat2Qtan(q);
            float d = t.x * t.x + 1f;
            return new F4(t.x, (t.z * t.x + t.y) / d, (t.z - t.y * t.x) / d, 0);
        }

        public static F4 ZYRoll2Quat(F4 v) => QNormalize(new F4(v.x, v.y + v.x * v.z, v.z - v.x * v.y, 1f));

        public static F4 RollZY2Quat(F4 v) => QNormalize(new F4(v.x, v.y - v.x * v.z, v.z + v.x * v.y, 1f));

        public static F4 DoubleAtan(F4 v) => new F4(2f * MathF.Atan(v.x), 2f * MathF.Atan(v.y), 2f * MathF.Atan(v.z), 0);

        public static F4 HalfTan(F4 v) => new F4(MathF.Tan(0.5f * v.x), MathF.Tan(0.5f * v.y), MathF.Tan(0.5f * v.z), 0);

        /// <summary>An angle to a muscle value (v / -min below 0, v / max above).</summary>
        public static float LimitProject(float min, float max, float v) =>
            v < 0f ? (min < 0f ? -v / min : 0f) : (max > 0f ? v / max : 0f);

        /// <summary>A muscle value to an angle.</summary>
        public static float LimitUnproject(float min, float max, float v) => v < 0f ? -v * min : v * max;
    }
}
