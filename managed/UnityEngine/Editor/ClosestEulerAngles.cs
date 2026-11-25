using System;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// <see cref="AnimationUtility.GetClosestEuler"/>: a rotation's angles about the axes of a
    /// rotation order (the first letter applied first), the set of the two that make it which lies
    /// nearer the hint, as the Unity 6000.0.71f1 Editor answers it (tests/projects/animinternal
    /// <c>euler.*</c>):
    /// <list type="bullet">
    /// <item>the angles come from the rotation as given (it is not normalized), in single precision,
    /// arctangents from <see cref="Atan"/>; where the middle angle's sine squared reaches
    /// <see cref="GimbalSineSquared"/>, the last angle is zero and the first takes the whole turn
    /// about the first axis;</item>
    /// <item>each angle is rounded to a thousandth of a degree, then moved by whole turns next to the
    /// hint's; the other set (first and last a half turn on, middle mirrored) likewise; the set
    /// with the smaller squared distance to the hint wins, the first on a tie;</item>
    /// <item>a rotation within <see cref="SameRotationDegrees"/> of the hint's own rotation gives
    /// the hint itself.</item>
    /// </list>
    /// Unmeasured: an angle within the last bit of a half-thousandth of a degree (about one call in
    /// a thousand on random rotations) may land on the neighbouring thousandth. A zero rotation (no
    /// angles) gives the Linux Editor's answer: each undefined angle reads -2147483.75 degrees before it
    /// is moved next to the hint.
    /// </summary>
    internal static class ClosestEulerAngles
    {
        /// <summary>Middle-angle sine squared from which the angles are taken in gimbal lock.</summary>
        const float GimbalSineSquared = 0.99999595f;
        /// <summary>Rotations nearer than this (degrees) to the hint's give the hint back.</summary>
        const double SameRotationDegrees = 0.001;
        const float ToDegrees = 57.29578f;
        const float HalfPi = 1.5707964f;
        const float Pi = 3.1415927f;
        /// <summary>The value an undefined (NaN) angle rounds to.</summary>
        const float UndefinedRounded = -2147483648f;

        internal static Vector3 Of(Quaternion q, Vector3 hint, RotationOrder order)
        {
            if (order < RotationOrder.OrderXYZ || order > RotationOrder.OrderZYX) throw new ArgumentOutOfRangeException(nameof(order));
            var (i, j, k) = Transform.RotationAxes(order);
            var radians = Angles(q, order);
            var angles = new Vector3();
            for (int a = 0; a < 3; a++)
                angles[a] = RoundHalfEven(radians[a] * ToDegrees * 1000f) / 1000f;
            var nearest = Nearest(angles, hint, order);
            if (AngleToHint(q, hint, i, j, k) < SameRotationDegrees) return hint;
            return nearest;
        }

        /// <summary>Of the two sets of angles that make the rotation <paramref name="euler"/> makes
        /// (the other: first and last a half turn on, middle mirrored), each angle moved by whole
        /// turns next to the hint's, the set with the smaller squared distance to the hint, the
        /// first on a tie (GameObjectRecorder unrolls Euler angles key by key with it:
        /// tests/projects/animrecorder <c>euler.*</c>).</summary>
        internal static Vector3 Nearest(Vector3 euler, Vector3 hint, RotationOrder order)
        {
            int j = Transform.RotationAxes(order).Middle;
            Vector3 angles = euler, other = new Vector3(euler.x + 180f, euler.y + 180f, euler.z + 180f);
            other[j] = -other[j];
            float distance = 0f, otherDistance = 0f;
            for (int a = 0; a < 3; a++)
            {
                angles[a] = Near(angles[a], hint[a]);
                other[a] = Near(other[a], hint[a]);
                float d = angles[a] - hint[a], e = other[a] - hint[a];
                distance += d * d;
                otherDistance += e * e;
            }
            return otherDistance >= distance ? angles : other;

            static float Near(float degrees, float hint) => degrees + RoundHalfEven((hint - degrees) / 360f) * 360f;
        }

        /// <summary>Half-even rounding to a whole number; an undefined value reads as
        /// <see cref="UndefinedRounded"/>.</summary>
        static float RoundHalfEven(float v) => float.IsNaN(v) ? UndefinedRounded : MathF.Round(v, MidpointRounding.ToEven);

        /// <summary>The rotation's angles (radians) about x, y and z for the order.</summary>
        static Vector3 Angles(Quaternion q, RotationOrder order)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            // Doubled products with w, doubled cross products, squares.
            float xw = x * w * 2f, yw = y * w * 2f, zw = z * w * 2f;
            float xy = x * y * 2f, yz = y * z * 2f, zx = z * x * 2f;
            float xx = x * x, yy = y * y, zz = z * z, ww = w * w;
            float sine, n1, d1, n3, d3, g0, g1, g2, g3, gimbalNumerator;
            switch ((int)order)
            {
                case 0: // XYZ
                    sine = zx - yw; n1 = yz + xw; d1 = zz + ww - yy - xx; n3 = xy + zw; d3 = xx + ww - yy - zz;
                    g0 = zx; g1 = yw; g2 = xy; g3 = zw; gimbalNumerator = zx * zw + yw * xy;
                    break;
                case 1: // XZY
                    sine = xy + zw; n1 = xw - yz; d1 = yy + ww - zz - xx; n3 = yw - zx; d3 = xx + ww - yy - zz;
                    g0 = xy; g1 = zw; g2 = zx; g3 = yw; gimbalNumerator = xy * yw + zw * zx;
                    break;
                case 2: // YZX
                    sine = xy - zw; n1 = zx + yw; d1 = xx + ww - zz - yy; n3 = yz + xw; d3 = yy + ww - xx - zz;
                    g0 = xy; g1 = zw; g2 = yz; g3 = xw; gimbalNumerator = xy * xw + zw * yz;
                    break;
                case 3: // YXZ
                    sine = yz + xw; n1 = yw - zx; d1 = zz + ww - xx - yy; n3 = zw - xy; d3 = yy + ww - zz - xx;
                    g0 = xy; g1 = zw; g2 = yz; g3 = xw; gimbalNumerator = xy * xw + yz * zw;
                    break;
                case 4: // ZXY
                    sine = yz - xw; n1 = xy + zw; d1 = yy + ww - xx - zz; n3 = zx + yw; d3 = zz + ww - xx - yy;
                    g0 = zx; g1 = yw; g2 = yz; g3 = xw; gimbalNumerator = xw * zx + yw * yz;
                    break;
                default: // ZYX
                    sine = zx + yw; n1 = zw - xy; d1 = xx + ww - yy - zz; n3 = xw - yz; d3 = zz + ww - yy - xx;
                    g0 = zx; g1 = yw; g2 = yz; g3 = xw; gimbalNumerator = zx * xw + yw * yz;
                    break;
            }
            var (first, middleAxis, last) = Transform.RotationAxes(order);
            // Orders whose middle axis follows the first cyclically (XYZ, YZX, ZXY) take the middle
            // angle with the opposite sign.
            bool even = (middleAxis - first + 3) % 3 == 1;
            float a1, a2, a3;
            if (sine * sine < GimbalSineSquared)
            {
                a1 = Atan2(n1, d1);
                a2 = Asin(sine);
                a3 = Atan2(n3, d3);
            }
            else
            {
                // Gimbal lock: the first angle takes the turn, the last none.
                float clamped = float.IsNaN(sine) ? sine : Math.Clamp(sine, -1f, 1f);
                float numerator = gimbalNumerator + gimbalNumerator;
                float denominator = (g1 * g1 - g0 * g0) + (g3 * g3 - g2 * g2);
                a1 = Atan2(numerator, denominator);
                a2 = Asin(clamped);
                a3 = 0f;
            }
            var result = new Vector3();
            result[first] = a1;
            result[middleAxis] = even ? -a2 : a2;
            result[last] = a3;
            return result;
        }

        /// <summary>Arcsine as the arctangent of sine / cosine.</summary>
        static float Asin(float s)
        {
            float t = s * (1f / MathF.Sqrt(1f - s * s));
            float a = Atan(MathF.Abs(t));
            return float.IsNegative(s) ? -a : a;
        }

        /// <summary>The quadrant-aware arctangent of n / d, from <see cref="Atan"/>: a denominator
        /// with its sign bit set (-0 too) takes the angle's supplement, a numerator's sign bit the
        /// sign.</summary>
        static float Atan2(float n, float d)
        {
            float a = Atan(MathF.Abs(n / d));
            if (float.IsNegative(d)) a = Pi - a;
            return float.IsNegative(n) ? -a : a;
        }

        /// <summary>Single-precision arctangent of t (t ≥ 0) as the Editor's angles follow it: an
        /// odd polynomial of degree 13 on [0, 1], above 1 the complement of the reciprocal's.</summary>
        static float Atan(float t)
        {
            const float c0 = 0.99999946f, c1 = 0.3332646f, c2 = 0.19881524f, c3 = 0.13487266f,
                c4 = 0.08387136f, c5 = 0.037013143f, c6 = 0.007862508f;
            bool large = t > 1f;
            float x = large ? 1f / t : t;
            float x2 = x * x, x4 = x2 * x2, x6 = x2 * x4, x8 = x2 * x6, x10 = x2 * x8, x12 = x2 * x10;
            float p = c0 - x2 * c1;
            p = x4 * c2 + p;
            p -= x6 * c3;
            p = x8 * c4 + p;
            p -= x10 * c5;
            p = x12 * c6 + p;
            p *= x;
            return large ? HalfPi - p : p;
        }

        /// <summary>The angle (degrees) between the rotation and the hint's rotation for the order
        /// (the first axis applied first).</summary>
        static double AngleToHint(Quaternion q, Vector3 hint, int i, int j, int k)
        {
            double length = Math.Sqrt((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w);
            if (!(length > 0) || double.IsInfinity(length)) return double.NaN;
            Span<double> h = stackalloc double[4];
            Span<double> axisK = stackalloc double[4], axisJ = stackalloc double[4], axisI = stackalloc double[4];
            Axis(axisK, k, hint[k]);
            Axis(axisJ, j, hint[j]);
            Axis(axisI, i, hint[i]);
            Span<double> kj = stackalloc double[4];
            Multiply(axisK, axisJ, kj);
            Multiply(kj, axisI, h);
            double dot = Math.Abs((q.x * h[0] + q.y * h[1] + q.z * h[2] + q.w * h[3]) / length);
            return 2 * Math.Acos(Math.Min(1.0, dot)) * (180 / Math.PI);

            static void Axis(Span<double> v, int axis, float degrees)
            {
                double half = degrees * (Math.PI / 360);
                v.Clear();
                v[axis] = Math.Sin(half);
                v[3] = Math.Cos(half);
            }

            static void Multiply(ReadOnlySpan<double> a, ReadOnlySpan<double> b, Span<double> r)
            {
                r[0] = a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1];
                r[1] = a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0];
                r[2] = a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3];
                r[3] = a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2];
            }
        }
    }
}
