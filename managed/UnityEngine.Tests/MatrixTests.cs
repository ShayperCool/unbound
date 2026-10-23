using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class MatrixTests
    {
        private static readonly Vector3 Pos = new Vector3(1f, -2f, 3f);
        private static readonly Quaternion Rot = Quaternion.Euler(20f, 40f, 60f);
        private static readonly Vector3 Scl = new Vector3(2f, 3f, 0.5f);

        [Fact]
        public void FieldsAreColumnMajorInUnityOrder()
        {
            Assert.Equal(64, Marshal.SizeOf<Matrix4x4>());
            Assert.Equal(4, (int)Marshal.OffsetOf<Matrix4x4>("m10"));
            Assert.Equal(16, (int)Marshal.OffsetOf<Matrix4x4>("m01"));
            Assert.Equal(60, (int)Marshal.OffsetOf<Matrix4x4>("m33"));
            Matrix4x4 m = Matrix4x4.Translate(Pos);
            Assert.Equal(Pos.x, m[0, 3]);
            Assert.Equal(Pos.x, m[12]);
            Assert.Equal(Pos.y, m.m13);
            Assert.Equal(new Vector4(Pos.x, Pos.y, Pos.z, 1f), m.GetColumn(3));
            Assert.Equal(new Vector4(1f, 0f, 0f, Pos.x), m.GetRow(0));
            Assert.Equal("Invalid matrix index!", Assert.Throws<IndexOutOfRangeException>(() => m[16]).Message);
        }

        [Fact]
        public void TrsTransformsScaleThenRotateThenTranslate()
        {
            Matrix4x4 m = Matrix4x4.TRS(Pos, Rot, Scl);
            Vector3 p = new Vector3(0.5f, 1f, -2f);
            Vector3 expected = Pos + Rot * Vector3.Scale(Scl, p);
            Near.Equal(expected, m.MultiplyPoint3x4(p), 1e-5f);
            Near.Equal(expected, m.MultiplyPoint(p), 1e-5f);
            Near.Equal(Rot * Vector3.Scale(Scl, p), m.MultiplyVector(p), 1e-5f);
            Near.Equal(m, Matrix4x4.Translate(Pos) * Matrix4x4.Rotate(Rot) * Matrix4x4.Scale(Scl), 1e-5f);
            Matrix4x4 set = default;
            set.SetTRS(Pos, Rot, Scl);
            Near.Equal(m, set, 0f);
            Assert.Equal(Pos, m.GetPosition());
        }

        [Fact]
        public void InverseUndoesTheMatrix()
        {
            Matrix4x4 m = Matrix4x4.TRS(Pos, Rot, Scl);
            Near.Equal(Matrix4x4.identity, m * m.inverse, 1e-5f);
            Near.Equal(Matrix4x4.identity, m.inverse * m, 1e-5f);

            Matrix4x4 p = Matrix4x4.Perspective(60f, 1.5f, 0.3f, 100f);
            Near.Equal(Matrix4x4.identity, p * Matrix4x4.Inverse(p), 1e-4f);

            Matrix4x4 general = new Matrix4x4(new Vector4(2f, 1f, 0f, 1f), new Vector4(0f, 3f, 1f, 0f), new Vector4(1f, 0f, 4f, 2f), new Vector4(0f, 1f, 0f, 1f));
            Near.Equal(Matrix4x4.identity, general * general.inverse, 1e-5f);

            Assert.Equal(Matrix4x4.zero, Matrix4x4.Scale(new Vector3(1f, 0f, 1f)).inverse);
        }

        [Fact]
        public void Inverse3DAffine()
        {
            Matrix4x4 m = Matrix4x4.TRS(Pos, Rot, Scl);
            Matrix4x4 result = Matrix4x4.identity;
            Assert.True(Matrix4x4.Inverse3DAffine(m, ref result));
            Near.Equal(m.inverse, result, 1e-5f);
            Matrix4x4 singular = Matrix4x4.Scale(Vector3.zero);
            Assert.False(Matrix4x4.Inverse3DAffine(singular, ref result));
            Assert.Equal(singular, result);
        }

        [Fact]
        public void DeterminantAndTranspose()
        {
            Near.Equal(3f, Matrix4x4.TRS(Pos, Rot, new Vector3(1f, 3f, 1f)).determinant, 1e-5f);
            Near.Equal(24f, Matrix4x4.Determinant(Matrix4x4.Scale(new Vector3(2f, 3f, 4f))));
            Matrix4x4 m = Matrix4x4.TRS(Pos, Rot, Scl);
            Assert.Equal(m.m12, m.transpose.m21);
            Assert.Equal(m, Matrix4x4.Transpose(m.transpose));
        }

        [Fact]
        public void RotationAndLossyScale()
        {
            Matrix4x4 m = Matrix4x4.TRS(Pos, Rot, Scl);
            Near.SameRotation(Rot, m.rotation, 1e-3f);
            Near.Equal(Scl, m.lossyScale, 1e-5f);
            Matrix4x4 mirrored = Matrix4x4.TRS(Pos, Rot, new Vector3(-2f, 3f, 0.5f));
            Near.Equal(new Vector3(-2f, 3f, 0.5f), mirrored.lossyScale, 1e-5f);
            Near.SameRotation(Rot, mirrored.rotation, 1e-3f);
        }

        [Fact]
        public void ValidTrsAndIdentity()
        {
            Assert.True(Matrix4x4.TRS(Pos, Rot, Scl).ValidTRS());
            Assert.False(Matrix4x4.Perspective(60f, 1f, 0.1f, 10f).ValidTRS());
            Matrix4x4 shear = Matrix4x4.identity;
            shear.m01 = 1f;
            Assert.False(shear.ValidTRS());
            Assert.True(Matrix4x4.identity.isIdentity);
            Assert.False(Matrix4x4.Translate(Vector3.one).isIdentity);
        }

        [Fact]
        public void PerspectiveAndDecomposeProjection()
        {
            Matrix4x4 p = Matrix4x4.Perspective(60f, 2f, 0.3f, 1000f);
            Near.Equal(1f / Mathf.Tan(30f * Mathf.Deg2Rad), p.m11, 1e-5f);
            Near.Equal(-1f, p.m32);
            FrustumPlanes fp = p.decomposeProjection;
            float top = 0.3f * Mathf.Tan(30f * Mathf.Deg2Rad);
            Near.Equal(0.3f, fp.zNear, 1e-4f);
            Near.Equal(1000f, fp.zFar, 1f);
            Near.Equal(top, fp.top, 1e-5f);
            Near.Equal(-top, fp.bottom, 1e-5f);
            Near.Equal(2f * top, fp.right, 1e-5f);
            Near.Equal(p, Matrix4x4.Frustum(fp), 1e-3f);

            // Clip-space depth of the near plane is -1 and of the far plane is +1 (OpenGL style).
            Vector3 nearPoint = p.MultiplyPoint(new Vector3(0f, 0f, -0.3f));
            Near.Equal(-1f, nearPoint.z, 1e-4f);
        }

        [Fact]
        public void OrthoAndDecompose()
        {
            Matrix4x4 o = Matrix4x4.Ortho(-4f, 4f, -3f, 3f, 0.5f, 50f);
            FrustumPlanes fp = o.decomposeProjection;
            Near.Equal(-4f, fp.left, 1e-4f);
            Near.Equal(4f, fp.right, 1e-4f);
            Near.Equal(-3f, fp.bottom, 1e-4f);
            Near.Equal(3f, fp.top, 1e-4f);
            Near.Equal(0.5f, fp.zNear, 1e-4f);
            Near.Equal(50f, fp.zFar, 1e-3f);
            Near.Equal(new Vector3(1f, 1f, -1f), o.MultiplyPoint(new Vector3(4f, 3f, -0.5f)), 1e-5f);
        }

        [Fact]
        public void LookAtIsAnObjectToWorldMatrix()
        {
            Vector3 from = new Vector3(1f, 2f, 3f), to = new Vector3(-2f, 0f, 5f);
            Matrix4x4 m = Matrix4x4.LookAt(from, to, Vector3.up);
            Near.Equal(Matrix4x4.TRS(from, Quaternion.LookRotation(to - from, Vector3.up), Vector3.one), m, 1e-5f);
            Near.Equal((to - from).normalized, m.MultiplyVector(Vector3.forward), 1e-5f);
        }

        [Fact]
        public void TransformPlane()
        {
            Plane plane = new Plane(Vector3.up, 0f);
            Plane moved = Matrix4x4.Translate(new Vector3(0f, 2f, 0f)).TransformPlane(plane);
            Near.Equal(Vector3.up, moved.normal);
            Near.Equal(-2f, moved.distance, 1e-5f);
        }

        [Fact]
        public void SetColumnAndRow()
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.SetColumn(1, new Vector4(1f, 2f, 3f, 4f));
            Assert.Equal(new Vector4(1f, 2f, 3f, 4f), m.GetColumn(1));
            m.SetRow(2, new Vector4(5f, 6f, 7f, 8f));
            Assert.Equal(new Vector4(5f, 6f, 7f, 8f), m.GetRow(2));
            Assert.Equal(6f, m.m21);
        }

        [Fact]
        public void EqualityAndToString()
        {
            Matrix4x4 a = Matrix4x4.identity;
            Matrix4x4 b = Matrix4x4.identity;
            b.m03 = 1e-7f;
            Assert.True(a == b);
            Assert.False(a.Equals(b));
            Assert.Equal("1.00000\t0.00000\t0.00000\t0.00000\n0.00000\t1.00000\t0.00000\t0.00000\n0.00000\t0.00000\t1.00000\t0.00000\n0.00000\t0.00000\t0.00000\t1.00000\n",
                Matrix4x4.identity.ToString());
        }

        [Fact]
        public void MatrixTimesVector4()
        {
            Matrix4x4 m = Matrix4x4.TRS(Pos, Rot, Scl);
            Vector4 v = m * new Vector4(1f, 0f, 0f, 1f);
            Near.Equal(m.MultiplyPoint3x4(Vector3.right), (Vector3)v, 1e-5f);
            Near.Equal(1f, v.w);
        }
    }
}
