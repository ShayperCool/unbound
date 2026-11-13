using System;

namespace UnityEngine
{
    /// <summary>
    /// The rotation U V^T of a 3x3 matrix's singular value decomposition A = U S V^T (McAdams,
    /// Selle, Tamstorf, Teran and Sifakis, "Computing the Singular Value Decomposition of 3x3
    /// matrices with minimal branching and elementary floating point operations", 2011): five
    /// sweeps of approximate Jacobi rotations on A^T A give V, the columns of A V sorted by
    /// decreasing length and a Givens QR factorization give U. Matrix4x4.rotation uses it for
    /// reflections and degenerate scales. Single precision in the order of the Unity 6000.0.71f1
    /// Editor (tests/projects/floatrounding, native/matrixDecompose): a matrix product sums its
    /// terms as x + (y + z), a squared length as (x^2 + y^2) + (z^2 + w^2).
    /// </summary>
    internal static class PolarRotation
    {
        const int Sweeps = 5;
        // Above this ratio of the squared off-diagonal to the squared diagonal difference, a Jacobi
        // rotation takes the fixed eighth turn (3 + 2 sqrt(2)).
        const float ApproximationLimit = 5.82842712474619f;
        const float CosEighthTurn = 0.923879532511287f;
        const float SinEighthTurn = 0.38268343236509f;
        const float HalfSqrt2 = 0.707106781186548f;
        const float MinimumLength = 1e-15f;

        /// <summary>Replaces the column-major 3x3 matrix <paramref name="a"/> by the rotation of its polar decomposition.</summary>
        internal static void ToMatrix(Span<float> a)
        {
            Span<float> s = stackalloc float[9];
            Span<float> m = stackalloc float[9];
            Span<float> t = stackalloc float[9];
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++)
                    s[j * 3 + k] = a[k * 3] * a[j * 3] + (a[k * 3 + 1] * a[j * 3 + 1] + a[k * 3 + 2] * a[j * 3 + 2]);
            Quaternion v = Quaternion.identity;
            for (int i = 0; i < Sweeps; i++)
            {
                v = Jacobi(s, v, 0, 1, new Quaternion(0f, 0f, 1f, 1f), m, t);
                v = Jacobi(s, v, 1, 2, new Quaternion(1f, 0f, 0f, 1f), m, t);
                v = Jacobi(s, v, 2, 0, new Quaternion(0f, 1f, 0f, 1f), m, t);
            }

            // b = A V, columns sorted by decreasing length; each swap negates one column so V stays a rotation.
            FromQuaternion(v, m);
            Span<float> b = s;
            Multiply(a, m, b);
            float l0 = LengthSquared(b, 0), l1 = LengthSquared(b, 1), l2 = LengthSquared(b, 2);
            if (l0 < l1)
            {
                SwapNegate(b, 0, 1);
                v = Multiply(v, new Quaternion(0f, 0f, HalfSqrt2, HalfSqrt2));
                (l0, l1) = (l1, l0);
            }
            if (l0 < l2)
            {
                SwapNegate(b, 0, 2);
                v = Multiply(v, new Quaternion(0f, -HalfSqrt2, 0f, HalfSqrt2));
                l2 = l0;
            }
            if (l1 < l2)
            {
                SwapNegate(b, 1, 2);
                v = Multiply(v, new Quaternion(HalfSqrt2, 0f, 0f, HalfSqrt2));
            }

            // U from Givens rotations zeroing b(1, 0), b(2, 0) and b(2, 1); only the entries they read are formed.
            Quaternion u = Givens(b[0], b[1], new Quaternion(0f, 0f, 1f, 1f));
            FromQuaternion(Conjugate(u), m);
            Vector3 r0 = Multiply(m, new Vector3(b[0], b[1], b[2]));
            Vector3 r1 = Multiply(m, new Vector3(b[3], b[4], b[5]));
            Quaternion q = Givens(r0.x, r0.z, new Quaternion(0f, -1f, 0f, 1f));
            u = Multiply(u, q);
            FromQuaternion(Conjugate(q), m);
            r1 = Multiply(m, r1);
            u = Multiply(u, Givens(r1.y, r1.z, new Quaternion(1f, 0f, 0f, 1f)));
            FromQuaternion(Multiply(u, Conjugate(v)), a);
        }

        // One approximate Jacobi rotation in the (p, q) plane: s = Q^T s Q, returns v Q.
        static Quaternion Jacobi(Span<float> s, Quaternion v, int p, int q, Quaternion mask, Span<float> m, Span<float> t)
        {
            float difference = s[p * 3 + p] - s[q * 3 + q];
            float ch = difference + difference;
            float sh = s[p * 3 + q];
            Quaternion g = ApproximationLimit * sh * sh < ch * ch
                ? Masked(sh, ch, mask)
                : Masked(SinEighthTurn, CosEighthTurn, mask);
            g = Normalize(g);
            FromQuaternion(g, m);
            // t = Q^T s: the rows of Q are its columns' components.
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++)
                    t[j * 3 + k] = m[k * 3] * s[j * 3] + (m[k * 3 + 1] * s[j * 3 + 1] + m[k * 3 + 2] * s[j * 3 + 2]);
            Multiply(t, m, s);
            return Multiply(v, g);
        }

        static Quaternion Givens(float p, float q, Quaternion mask)
        {
            float length = MathF.Sqrt(p * p + q * q);
            float sh = length > MinimumLength ? q : 0f;
            float ch = MathF.Max(length, MinimumLength) + MathF.Abs(p);
            if (p < 0f)
                (sh, ch) = (ch, sh);
            return Normalize(Masked(sh, ch, mask));
        }

        static Quaternion Masked(float sh, float ch, Quaternion mask) => new Quaternion(sh * mask.x, sh * mask.y, sh * mask.z, ch * mask.w);

        static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        static Quaternion Normalize(Quaternion q)
        {
            float inverse = 1f / MathF.Sqrt(q.x * q.x + q.y * q.y + (q.z * q.z + q.w * q.w));
            return new Quaternion(inverse * q.x, inverse * q.y, inverse * q.z, inverse * q.w);
        }

        static Quaternion Multiply(Quaternion a, Quaternion b) => new Quaternion(
            -(a.z * b.y - a.y * b.z - a.w * b.x - a.x * b.w),
            -(a.x * b.z - a.z * b.x - a.w * b.y - a.y * b.w),
            -(a.y * b.x - a.w * b.z - a.z * b.w - a.x * b.y),
            a.w * b.w - a.x * b.x - a.z * b.z - a.y * b.y);

        static float LengthSquared(ReadOnlySpan<float> m, int c) => m[c * 3] * m[c * 3] + m[c * 3 + 1] * m[c * 3 + 1] + (m[c * 3 + 2] * m[c * 3 + 2] + 0f);

        // Column a takes column b, column b takes the negated column a.
        static void SwapNegate(Span<float> m, int a, int b)
        {
            for (int i = 0; i < 3; i++)
            {
                float negated = -m[a * 3 + i];
                m[a * 3 + i] = m[b * 3 + i];
                m[b * 3 + i] = negated;
            }
        }

        // c = a b, column-major 3x3.
        static void Multiply(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> c)
        {
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 3; k++)
                    c[j * 3 + k] = a[k] * b[j * 3] + (a[3 + k] * b[j * 3 + 1] + a[6 + k] * b[j * 3 + 2]);
        }

        static Vector3 Multiply(ReadOnlySpan<float> m, Vector3 v) => new Vector3(
            m[0] * v.x + (m[3] * v.y + m[6] * v.z),
            m[1] * v.x + (m[4] * v.y + m[7] * v.z),
            m[2] * v.x + (m[5] * v.y + m[8] * v.z));

        // The rotation matrix of a unit quaternion, each entry 1 or 0 plus two doubled products.
        internal static void FromQuaternion(Quaternion q, Span<float> m)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            m[0] = y * (-2f * y) + z * (-2f * z) + 1f;
            m[1] = x * (2f * y) + w * (2f * z) + 0f;
            m[2] = w * (-2f * y) + x * (2f * z) + 0f;
            m[3] = w * (-2f * z) + y * (2f * x) + 0f;
            m[4] = z * (-2f * z) + x * (-2f * x) + 1f;
            m[5] = y * (2f * z) + w * (2f * x) + 0f;
            m[6] = z * (2f * x) + w * (2f * y) + 0f;
            m[7] = w * (-2f * x) + z * (2f * y) + 0f;
            m[8] = x * (-2f * x) + y * (-2f * y) + 1f;
        }
    }
}
