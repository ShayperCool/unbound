using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>The six planes of a view frustum, as used by Matrix4x4.Frustum and decomposeProjection.</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct FrustumPlanes
    {
        public float left;
        public float right;
        public float bottom;
        public float top;
        public float zNear;
        public float zFar;
    }

    /// <summary>
    /// A standard 4x4 transformation matrix. Fields are named m{row}{column} and stored in column-major
    /// order. Points are column vectors: M * p.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Matrix4x4 : IEquatable<Matrix4x4>, IFormattable
    {
        public float m00;
        public float m10;
        public float m20;
        public float m30;

        public float m01;
        public float m11;
        public float m21;
        public float m31;

        public float m02;
        public float m12;
        public float m22;
        public float m32;

        public float m03;
        public float m13;
        public float m23;
        public float m33;

        private static readonly Matrix4x4 zeroMatrix = new Matrix4x4(
            new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0), new Vector4(0, 0, 0, 0));

        private static readonly Matrix4x4 identityMatrix = new Matrix4x4(
            new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1));

        public Matrix4x4(Vector4 column0, Vector4 column1, Vector4 column2, Vector4 column3)
        {
            m00 = column0.x; m01 = column1.x; m02 = column2.x; m03 = column3.x;
            m10 = column0.y; m11 = column1.y; m12 = column2.y; m13 = column3.y;
            m20 = column0.z; m21 = column1.z; m22 = column2.z; m23 = column3.z;
            m30 = column0.w; m31 = column1.w; m32 = column2.w; m33 = column3.w;
        }

        public float this[int row, int column]
        {
            get => this[row + column * 4];
            set => this[row + column * 4] = value;
        }

        /// <summary>Element by column-major index (0 = m00, 1 = m10, ..., 15 = m33).</summary>
        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m00;
                    case 1: return m10;
                    case 2: return m20;
                    case 3: return m30;
                    case 4: return m01;
                    case 5: return m11;
                    case 6: return m21;
                    case 7: return m31;
                    case 8: return m02;
                    case 9: return m12;
                    case 10: return m22;
                    case 11: return m32;
                    case 12: return m03;
                    case 13: return m13;
                    case 14: return m23;
                    case 15: return m33;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: m00 = value; break;
                    case 1: m10 = value; break;
                    case 2: m20 = value; break;
                    case 3: m30 = value; break;
                    case 4: m01 = value; break;
                    case 5: m11 = value; break;
                    case 6: m21 = value; break;
                    case 7: m31 = value; break;
                    case 8: m02 = value; break;
                    case 9: m12 = value; break;
                    case 10: m22 = value; break;
                    case 11: m32 = value; break;
                    case 12: m03 = value; break;
                    case 13: m13 = value; break;
                    case 14: m23 = value; break;
                    case 15: m33 = value; break;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
        }

        public override int GetHashCode() =>
            GetColumn(0).GetHashCode() ^ (GetColumn(1).GetHashCode() << 2) ^ (GetColumn(2).GetHashCode() >> 2) ^ (GetColumn(3).GetHashCode() >> 1);

        public override bool Equals(object other) => other is Matrix4x4 m && Equals(m);

        public bool Equals(Matrix4x4 other) =>
            GetColumn(0).Equals(other.GetColumn(0)) && GetColumn(1).Equals(other.GetColumn(1)) &&
            GetColumn(2).Equals(other.GetColumn(2)) && GetColumn(3).Equals(other.GetColumn(3));

        public static Matrix4x4 operator *(Matrix4x4 lhs, Matrix4x4 rhs)
        {
            Matrix4x4 res;
            res.m00 = lhs.m00 * rhs.m00 + lhs.m01 * rhs.m10 + lhs.m02 * rhs.m20 + lhs.m03 * rhs.m30;
            res.m01 = lhs.m00 * rhs.m01 + lhs.m01 * rhs.m11 + lhs.m02 * rhs.m21 + lhs.m03 * rhs.m31;
            res.m02 = lhs.m00 * rhs.m02 + lhs.m01 * rhs.m12 + lhs.m02 * rhs.m22 + lhs.m03 * rhs.m32;
            res.m03 = lhs.m00 * rhs.m03 + lhs.m01 * rhs.m13 + lhs.m02 * rhs.m23 + lhs.m03 * rhs.m33;

            res.m10 = lhs.m10 * rhs.m00 + lhs.m11 * rhs.m10 + lhs.m12 * rhs.m20 + lhs.m13 * rhs.m30;
            res.m11 = lhs.m10 * rhs.m01 + lhs.m11 * rhs.m11 + lhs.m12 * rhs.m21 + lhs.m13 * rhs.m31;
            res.m12 = lhs.m10 * rhs.m02 + lhs.m11 * rhs.m12 + lhs.m12 * rhs.m22 + lhs.m13 * rhs.m32;
            res.m13 = lhs.m10 * rhs.m03 + lhs.m11 * rhs.m13 + lhs.m12 * rhs.m23 + lhs.m13 * rhs.m33;

            res.m20 = lhs.m20 * rhs.m00 + lhs.m21 * rhs.m10 + lhs.m22 * rhs.m20 + lhs.m23 * rhs.m30;
            res.m21 = lhs.m20 * rhs.m01 + lhs.m21 * rhs.m11 + lhs.m22 * rhs.m21 + lhs.m23 * rhs.m31;
            res.m22 = lhs.m20 * rhs.m02 + lhs.m21 * rhs.m12 + lhs.m22 * rhs.m22 + lhs.m23 * rhs.m32;
            res.m23 = lhs.m20 * rhs.m03 + lhs.m21 * rhs.m13 + lhs.m22 * rhs.m23 + lhs.m23 * rhs.m33;

            res.m30 = lhs.m30 * rhs.m00 + lhs.m31 * rhs.m10 + lhs.m32 * rhs.m20 + lhs.m33 * rhs.m30;
            res.m31 = lhs.m30 * rhs.m01 + lhs.m31 * rhs.m11 + lhs.m32 * rhs.m21 + lhs.m33 * rhs.m31;
            res.m32 = lhs.m30 * rhs.m02 + lhs.m31 * rhs.m12 + lhs.m32 * rhs.m22 + lhs.m33 * rhs.m32;
            res.m33 = lhs.m30 * rhs.m03 + lhs.m31 * rhs.m13 + lhs.m32 * rhs.m23 + lhs.m33 * rhs.m33;
            return res;
        }

        public static Vector4 operator *(Matrix4x4 lhs, Vector4 vector)
        {
            Vector4 res;
            res.x = lhs.m00 * vector.x + lhs.m01 * vector.y + lhs.m02 * vector.z + lhs.m03 * vector.w;
            res.y = lhs.m10 * vector.x + lhs.m11 * vector.y + lhs.m12 * vector.z + lhs.m13 * vector.w;
            res.z = lhs.m20 * vector.x + lhs.m21 * vector.y + lhs.m22 * vector.z + lhs.m23 * vector.w;
            res.w = lhs.m30 * vector.x + lhs.m31 * vector.y + lhs.m32 * vector.z + lhs.m33 * vector.w;
            return res;
        }

        /// <summary>Approximate equality, column by column (see Vector4 ==).</summary>
        public static bool operator ==(Matrix4x4 lhs, Matrix4x4 rhs) =>
            lhs.GetColumn(0) == rhs.GetColumn(0) && lhs.GetColumn(1) == rhs.GetColumn(1) &&
            lhs.GetColumn(2) == rhs.GetColumn(2) && lhs.GetColumn(3) == rhs.GetColumn(3);

        public static bool operator !=(Matrix4x4 lhs, Matrix4x4 rhs) => !(lhs == rhs);

        public Vector4 GetColumn(int index)
        {
            switch (index)
            {
                case 0: return new Vector4(m00, m10, m20, m30);
                case 1: return new Vector4(m01, m11, m21, m31);
                case 2: return new Vector4(m02, m12, m22, m32);
                case 3: return new Vector4(m03, m13, m23, m33);
                default: throw new IndexOutOfRangeException("Invalid column index!");
            }
        }

        public Vector4 GetRow(int index)
        {
            switch (index)
            {
                case 0: return new Vector4(m00, m01, m02, m03);
                case 1: return new Vector4(m10, m11, m12, m13);
                case 2: return new Vector4(m20, m21, m22, m23);
                case 3: return new Vector4(m30, m31, m32, m33);
                default: throw new IndexOutOfRangeException("Invalid row index!");
            }
        }

        public Vector3 GetPosition() => new Vector3(m03, m13, m23);

        public void SetColumn(int index, Vector4 column)
        {
            this[0, index] = column.x;
            this[1, index] = column.y;
            this[2, index] = column.z;
            this[3, index] = column.w;
        }

        public void SetRow(int index, Vector4 row)
        {
            this[index, 0] = row.x;
            this[index, 1] = row.y;
            this[index, 2] = row.z;
            this[index, 3] = row.w;
        }

        /// <summary>Transforms a position, including the projective divide by w.</summary>
        public Vector3 MultiplyPoint(Vector3 point)
        {
            Vector3 res;
            res.x = m00 * point.x + m01 * point.y + m02 * point.z + m03;
            res.y = m10 * point.x + m11 * point.y + m12 * point.z + m13;
            res.z = m20 * point.x + m21 * point.y + m22 * point.z + m23;
            float w = m30 * point.x + m31 * point.y + m32 * point.z + m33;
            w = 1F / w;
            res.x *= w;
            res.y *= w;
            res.z *= w;
            return res;
        }

        /// <summary>Transforms a position by the affine part only (no projective divide).</summary>
        public Vector3 MultiplyPoint3x4(Vector3 point)
        {
            Vector3 res;
            res.x = m00 * point.x + m01 * point.y + m02 * point.z + m03;
            res.y = m10 * point.x + m11 * point.y + m12 * point.z + m13;
            res.z = m20 * point.x + m21 * point.y + m22 * point.z + m23;
            return res;
        }

        /// <summary>Transforms a direction (the upper 3x3 only).</summary>
        public Vector3 MultiplyVector(Vector3 vector)
        {
            Vector3 res;
            res.x = m00 * vector.x + m01 * vector.y + m02 * vector.z;
            res.y = m10 * vector.x + m11 * vector.y + m12 * vector.z;
            res.z = m20 * vector.x + m21 * vector.y + m22 * vector.z;
            return res;
        }

        /// <summary>Transforms a plane by this matrix (through its inverse transpose).</summary>
        public Plane TransformPlane(Plane plane)
        {
            Matrix4x4 inv = inverse;
            float x = plane.normal.x, y = plane.normal.y, z = plane.normal.z, w = plane.distance;
            float a = inv.m00 * x + inv.m10 * y + inv.m20 * z + inv.m30 * w;
            float b = inv.m01 * x + inv.m11 * y + inv.m21 * z + inv.m31 * w;
            float c = inv.m02 * x + inv.m12 * y + inv.m22 * z + inv.m32 * w;
            float d = inv.m03 * x + inv.m13 * y + inv.m23 * z + inv.m33 * w;
            return new Plane(new Vector3(a, b, c), d);
        }

        public static Matrix4x4 Scale(Vector3 vector)
        {
            Matrix4x4 m = identityMatrix;
            m.m00 = vector.x;
            m.m11 = vector.y;
            m.m22 = vector.z;
            return m;
        }

        public static Matrix4x4 Translate(Vector3 vector)
        {
            Matrix4x4 m = identityMatrix;
            m.m03 = vector.x;
            m.m13 = vector.y;
            m.m23 = vector.z;
            return m;
        }

        /// <summary>The rotation matrix of a unit quaternion.</summary>
        public static Matrix4x4 Rotate(Quaternion q) => TRS(Vector3.zero, q, Vector3.one);

        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s)
        {
            float x2 = q.x * 2F, y2 = q.y * 2F, z2 = q.z * 2F;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
            float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
            float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;

            Matrix4x4 m;
            m.m00 = (1F - (yy + zz)) * s.x;
            m.m10 = (xy + wz) * s.x;
            m.m20 = (xz - wy) * s.x;
            m.m30 = 0F;

            m.m01 = (xy - wz) * s.y;
            m.m11 = (1F - (xx + zz)) * s.y;
            m.m21 = (yz + wx) * s.y;
            m.m31 = 0F;

            m.m02 = (xz + wy) * s.z;
            m.m12 = (yz - wx) * s.z;
            m.m22 = (1F - (xx + yy)) * s.z;
            m.m32 = 0F;

            m.m03 = pos.x;
            m.m13 = pos.y;
            m.m23 = pos.z;
            m.m33 = 1F;
            return m;
        }

        public void SetTRS(Vector3 pos, Quaternion q, Vector3 s) => this = TRS(pos, q, s);

        public static Matrix4x4 zero => zeroMatrix;

        public static Matrix4x4 identity => identityMatrix;

        // ---- Inverse / determinant / transpose ---------------------------------------------

        public static float Determinant(Matrix4x4 m) => m.determinant;

        public float determinant
        {
            get
            {
                double a = m00, b = m01, c = m02, d = m03;
                double e = m10, f = m11, g = m12, h = m13;
                double i = m20, j = m21, k = m22, l = m23;
                double mm = m30, n = m31, o = m32, p = m33;
                double kp_lo = k * p - l * o;
                double jp_ln = j * p - l * n;
                double jo_kn = j * o - k * n;
                double ip_lm = i * p - l * mm;
                double io_km = i * o - k * mm;
                double in_jm = i * n - j * mm;
                return (float)(a * (f * kp_lo - g * jp_ln + h * jo_kn) -
                               b * (e * kp_lo - g * ip_lm + h * io_km) +
                               c * (e * jp_ln - f * ip_lm + h * in_jm) -
                               d * (e * jo_kn - f * io_km + g * in_jm));
            }
        }

        /// <summary>The inverse matrix; a singular matrix gives Matrix4x4.zero.</summary>
        public static Matrix4x4 Inverse(Matrix4x4 m)
        {
            // t is the transpose of m in row-major order (t[i, j] = m[j, i]); the inverse of the
            // transpose, written back column-major, is the inverse of m. Cofactors are built from
            // the 2x2 determinants of the top two and bottom two rows.
            double[,] t = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    t[i, j] = m[j, i];

            double s0 = t[0, 0] * t[1, 1] - t[1, 0] * t[0, 1];
            double s1 = t[0, 0] * t[1, 2] - t[1, 0] * t[0, 2];
            double s2 = t[0, 0] * t[1, 3] - t[1, 0] * t[0, 3];
            double s3 = t[0, 1] * t[1, 2] - t[1, 1] * t[0, 2];
            double s4 = t[0, 1] * t[1, 3] - t[1, 1] * t[0, 3];
            double s5 = t[0, 2] * t[1, 3] - t[1, 2] * t[0, 3];

            double c5 = t[2, 2] * t[3, 3] - t[3, 2] * t[2, 3];
            double c4 = t[2, 1] * t[3, 3] - t[3, 1] * t[2, 3];
            double c3 = t[2, 1] * t[3, 2] - t[3, 1] * t[2, 2];
            double c2 = t[2, 0] * t[3, 3] - t[3, 0] * t[2, 3];
            double c1 = t[2, 0] * t[3, 2] - t[3, 0] * t[2, 2];
            double c0 = t[2, 0] * t[3, 1] - t[3, 0] * t[2, 1];

            double det = s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
            if (det == 0.0 || double.IsNaN(det) || double.IsInfinity(det))
                return zeroMatrix;
            double inv = 1.0 / det;

            double[,] b = new double[4, 4];
            b[0, 0] = (t[1, 1] * c5 - t[1, 2] * c4 + t[1, 3] * c3) * inv;
            b[0, 1] = (-t[0, 1] * c5 + t[0, 2] * c4 - t[0, 3] * c3) * inv;
            b[0, 2] = (t[3, 1] * s5 - t[3, 2] * s4 + t[3, 3] * s3) * inv;
            b[0, 3] = (-t[2, 1] * s5 + t[2, 2] * s4 - t[2, 3] * s3) * inv;

            b[1, 0] = (-t[1, 0] * c5 + t[1, 2] * c2 - t[1, 3] * c1) * inv;
            b[1, 1] = (t[0, 0] * c5 - t[0, 2] * c2 + t[0, 3] * c1) * inv;
            b[1, 2] = (-t[3, 0] * s5 + t[3, 2] * s2 - t[3, 3] * s1) * inv;
            b[1, 3] = (t[2, 0] * s5 - t[2, 2] * s2 + t[2, 3] * s1) * inv;

            b[2, 0] = (t[1, 0] * c4 - t[1, 1] * c2 + t[1, 3] * c0) * inv;
            b[2, 1] = (-t[0, 0] * c4 + t[0, 1] * c2 - t[0, 3] * c0) * inv;
            b[2, 2] = (t[3, 0] * s4 - t[3, 1] * s2 + t[3, 3] * s0) * inv;
            b[2, 3] = (-t[2, 0] * s4 + t[2, 1] * s2 - t[2, 3] * s0) * inv;

            b[3, 0] = (-t[1, 0] * c3 + t[1, 1] * c1 - t[1, 2] * c0) * inv;
            b[3, 1] = (t[0, 0] * c3 - t[0, 1] * c1 + t[0, 2] * c0) * inv;
            b[3, 2] = (-t[3, 0] * s3 + t[3, 1] * s1 - t[3, 2] * s0) * inv;
            b[3, 3] = (t[2, 0] * s3 - t[2, 1] * s1 + t[2, 2] * s0) * inv;

            Matrix4x4 res = default;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    res[j, i] = (float)b[i, j];
            return res;
        }

        public Matrix4x4 inverse => Inverse(this);

        /// <summary>
        /// Inverts a matrix assumed to be affine (bottom row 0, 0, 0, 1). On failure (singular 3x3
        /// part) returns false and sets <paramref name="result"/> to a copy of the input.
        /// </summary>
        public static bool Inverse3DAffine(Matrix4x4 input, ref Matrix4x4 result)
        {
            double a = input.m00, b = input.m01, c = input.m02;
            double d = input.m10, e = input.m11, f = input.m12;
            double g = input.m20, h = input.m21, i = input.m22;
            double A = e * i - f * h;
            double B = -(d * i - f * g);
            double C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (det == 0.0 || double.IsNaN(det) || double.IsInfinity(det))
            {
                result = input;
                return false;
            }
            double inv = 1.0 / det;
            double r00 = A * inv, r01 = -(b * i - c * h) * inv, r02 = (b * f - c * e) * inv;
            double r10 = B * inv, r11 = (a * i - c * g) * inv, r12 = -(a * f - c * d) * inv;
            double r20 = C * inv, r21 = -(a * h - b * g) * inv, r22 = (a * e - b * d) * inv;
            double tx = input.m03, ty = input.m13, tz = input.m23;

            Matrix4x4 res;
            res.m00 = (float)r00; res.m01 = (float)r01; res.m02 = (float)r02;
            res.m10 = (float)r10; res.m11 = (float)r11; res.m12 = (float)r12;
            res.m20 = (float)r20; res.m21 = (float)r21; res.m22 = (float)r22;
            res.m03 = (float)-(r00 * tx + r01 * ty + r02 * tz);
            res.m13 = (float)-(r10 * tx + r11 * ty + r12 * tz);
            res.m23 = (float)-(r20 * tx + r21 * ty + r22 * tz);
            res.m30 = 0F; res.m31 = 0F; res.m32 = 0F; res.m33 = 1F;
            result = res;
            return true;
        }

        public static Matrix4x4 Transpose(Matrix4x4 m)
        {
            Matrix4x4 t;
            t.m00 = m.m00; t.m01 = m.m10; t.m02 = m.m20; t.m03 = m.m30;
            t.m10 = m.m01; t.m11 = m.m11; t.m12 = m.m21; t.m13 = m.m31;
            t.m20 = m.m02; t.m21 = m.m12; t.m22 = m.m22; t.m23 = m.m32;
            t.m30 = m.m03; t.m31 = m.m13; t.m32 = m.m23; t.m33 = m.m33;
            return t;
        }

        public Matrix4x4 transpose => Transpose(this);

        // ---- Decomposition -------------------------------------------------------------------

        /// <summary>The rotation part, with the scale removed (a reflection is assigned to the X axis).</summary>
        public Quaternion rotation
        {
            get
            {
                double[] c0 = { m00, m10, m20 };
                double[] c1 = { m01, m11, m21 };
                double[] c2 = { m02, m12, m22 };
                if (!NormalizeColumn(c0) || !NormalizeColumn(c1) || !NormalizeColumn(c2))
                    return Quaternion.identity;
                double det = c0[0] * (c1[1] * c2[2] - c2[1] * c1[2]) -
                             c1[0] * (c0[1] * c2[2] - c2[1] * c0[2]) +
                             c2[0] * (c0[1] * c1[2] - c1[1] * c0[2]);
                if (det < 0.0)
                {
                    c0[0] = -c0[0];
                    c0[1] = -c0[1];
                    c0[2] = -c0[2];
                }
                var m = new double[3, 3]
                {
                    { c0[0], c1[0], c2[0] },
                    { c0[1], c1[1], c2[1] },
                    { c0[2], c1[2], c2[2] },
                };
                return Quaternion.FromMatrix(m);
            }
        }

        private static bool NormalizeColumn(double[] c)
        {
            double mag = Math.Sqrt(c[0] * c[0] + c[1] * c[1] + c[2] * c[2]);
            if (mag < 1e-20)
                return false;
            c[0] /= mag;
            c[1] /= mag;
            c[2] /= mag;
            return true;
        }

        /// <summary>The scale part: the length of each basis column, negated on X when the matrix mirrors.</summary>
        public Vector3 lossyScale
        {
            get
            {
                Vector3 s = new Vector3(
                    new Vector3(m00, m10, m20).magnitude,
                    new Vector3(m01, m11, m21).magnitude,
                    new Vector3(m02, m12, m22).magnitude);
                float det3 = m00 * (m11 * m22 - m21 * m12) - m01 * (m10 * m22 - m20 * m12) + m02 * (m10 * m21 - m20 * m11);
                if (det3 < 0F)
                    s.x = -s.x;
                return s;
            }
        }

        /// <summary>True if this matrix is (approximately) the identity.</summary>
        public bool isIdentity
        {
            get
            {
                const float eps = Vector3.kEpsilon;
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        if (Math.Abs(this[r, c] - (r == c ? 1F : 0F)) > eps)
                            return false;
                return true;
            }
        }

        /// <summary>True if this matrix is a translation, rotation and (non-zero, unsheared) scale.</summary>
        public bool ValidTRS()
        {
            if (m30 != 0F || m31 != 0F || m32 != 0F || m33 != 1F)
                return false;
            if (!IsFinite(m00) || !IsFinite(m01) || !IsFinite(m02) || !IsFinite(m03) ||
                !IsFinite(m10) || !IsFinite(m11) || !IsFinite(m12) || !IsFinite(m13) ||
                !IsFinite(m20) || !IsFinite(m21) || !IsFinite(m22) || !IsFinite(m23))
                return false;
            Vector3 c0 = new Vector3(m00, m10, m20);
            Vector3 c1 = new Vector3(m01, m11, m21);
            Vector3 c2 = new Vector3(m02, m12, m22);
            float l0 = c0.magnitude, l1 = c1.magnitude, l2 = c2.magnitude;
            if (l0 < Vector3.kEpsilon || l1 < Vector3.kEpsilon || l2 < Vector3.kEpsilon)
                return false;
            const float tolerance = 1e-4f;
            return Math.Abs(Vector3.Dot(c0, c1)) <= tolerance * l0 * l1 &&
                   Math.Abs(Vector3.Dot(c0, c2)) <= tolerance * l0 * l2 &&
                   Math.Abs(Vector3.Dot(c1, c2)) <= tolerance * l1 * l2;
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>The frustum planes of a projection matrix (perspective or orthographic).</summary>
        public FrustumPlanes decomposeProjection
        {
            get
            {
                FrustumPlanes fp;
                bool perspective = m32 != 0F || m33 == 0F;
                if (perspective)
                {
                    float near = m23 / (m22 - 1F);
                    fp.zNear = near;
                    fp.zFar = m23 / (m22 + 1F);
                    fp.left = near * (m02 - 1F) / m00;
                    fp.right = near * (m02 + 1F) / m00;
                    fp.bottom = near * (m12 - 1F) / m11;
                    fp.top = near * (m12 + 1F) / m11;
                }
                else
                {
                    fp.zNear = (m23 + 1F) / m22;
                    fp.zFar = (m23 - 1F) / m22;
                    fp.left = (-1F - m03) / m00;
                    fp.right = (1F - m03) / m00;
                    fp.bottom = (-1F - m13) / m11;
                    fp.top = (1F - m13) / m11;
                }
                return fp;
            }
        }

        // ---- Projections -------------------------------------------------------------------

        /// <summary>An orthographic projection (OpenGL convention: view looks down -Z, clip z in [-1, 1]).</summary>
        public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float zNear, float zFar)
        {
            Matrix4x4 m = identityMatrix;
            float dx = right - left, dy = top - bottom, dz = zFar - zNear;
            m.m00 = 2F / dx;
            m.m11 = 2F / dy;
            m.m22 = -2F / dz;
            m.m03 = -(right + left) / dx;
            m.m13 = -(top + bottom) / dy;
            m.m23 = -(zFar + zNear) / dz;
            return m;
        }

        /// <summary>A perspective projection; <paramref name="fov"/> is the vertical field of view in degrees.</summary>
        public static Matrix4x4 Perspective(float fov, float aspect, float zNear, float zFar)
        {
            double cotangent = 1.0 / Math.Tan(fov * Mathf.Deg2Rad * 0.5);
            float deltaZ = zNear - zFar;
            Matrix4x4 m = zeroMatrix;
            m.m00 = (float)(cotangent / aspect);
            m.m11 = (float)cotangent;
            m.m22 = (zFar + zNear) / deltaZ;
            m.m23 = 2F * zNear * zFar / deltaZ;
            m.m32 = -1F;
            return m;
        }

        public static Matrix4x4 Frustum(float left, float right, float bottom, float top, float zNear, float zFar)
        {
            float dx = right - left, dy = top - bottom, dz = zFar - zNear;
            Matrix4x4 m = zeroMatrix;
            m.m00 = 2F * zNear / dx;
            m.m02 = (right + left) / dx;
            m.m11 = 2F * zNear / dy;
            m.m12 = (top + bottom) / dy;
            m.m22 = -(zFar + zNear) / dz;
            m.m23 = -2F * zFar * zNear / dz;
            m.m32 = -1F;
            return m;
        }

        public static Matrix4x4 Frustum(FrustumPlanes fp) => Frustum(fp.left, fp.right, fp.bottom, fp.top, fp.zNear, fp.zFar);

        /// <summary>
        /// An object-to-world matrix placed at <paramref name="from"/> whose Z axis points at
        /// <paramref name="to"/> (not a view matrix).
        /// </summary>
        public static Matrix4x4 LookAt(Vector3 from, Vector3 to, Vector3 up)
        {
            Vector3 z = to - from;
            float zMag = z.magnitude;
            Vector3 x = Vector3.Cross(up, z);
            float xMag = x.magnitude;
            if (zMag <= Vector3.kEpsilon || xMag <= Vector3.kEpsilon * Math.Max(1F, up.magnitude) * zMag)
                return TRS(from, Quaternion.LookRotation(z, up), Vector3.one);
            z /= zMag;
            x /= xMag;
            Vector3 y = Vector3.Cross(z, x);
            Matrix4x4 m;
            m.m00 = x.x; m.m01 = y.x; m.m02 = z.x; m.m03 = from.x;
            m.m10 = x.y; m.m11 = y.y; m.m12 = z.y; m.m13 = from.y;
            m.m20 = x.z; m.m21 = y.z; m.m22 = z.z; m.m23 = from.z;
            m.m30 = 0F; m.m31 = 0F; m.m32 = 0F; m.m33 = 1F;
            return m;
        }

        // ---- Formatting --------------------------------------------------------------------

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        /// <summary>Four tab-separated rows, each terminated by a newline; F5 by default.</summary>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F5");
            formatProvider = MathFormat.Provider(formatProvider);
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    if (c > 0)
                        sb.Append('\t');
                    sb.Append(this[r, c].ToString(format, formatProvider));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
