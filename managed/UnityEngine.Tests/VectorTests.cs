using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class VectorTests
    {
        [Fact]
        public void EqualityOperatorIsApproximateEqualsIsExact()
        {
            var a = new Vector3(1f, 2f, 3f);
            var b = new Vector3(1f, 2f, 3.000001f);
            Assert.True(a == b);
            Assert.False(a.Equals(b));
            Assert.False(a == new Vector3(1f, 2f, 3.0001f));
            // Squared distance must be below 9.99999944E-11.
            Assert.True(Vector3.zero == new Vector3(9.9e-6f, 0f, 0f));
            Assert.False(Vector3.zero == new Vector3(1.0e-5f, 0f, 0f));

            Assert.True(new Vector2(1f, 1f) == new Vector2(1f, 1.000001f));
            Assert.True(new Vector4(1f, 1f, 1f, 1f) == new Vector4(1f, 1f, 1f, 1.000001f));
            Assert.False(new Vector4(1f, 1f, 1f, 1f).Equals(new Vector4(1f, 1f, 1f, 1.000001f)));
        }

        [Fact]
        public void NormalizeReturnsZeroForTinyVectors()
        {
            Assert.Equal(Vector3.zero, new Vector3(1e-6f, 0f, 0f).normalized);
            Near.Equal(new Vector3(0.6f, 0.8f, 0f), new Vector3(3f, 4f, 0f).normalized);
            var v = new Vector2(1e-6f, 0f);
            v.Normalize();
            Assert.True(v.Equals(Vector2.zero));
            Assert.True(Vector4.Normalize(new Vector4(0f, 0f, 0f, 2f)).Equals(new Vector4(0f, 0f, 0f, 1f)));
        }

        [Fact]
        public void AnglesAndSignedAngles()
        {
            Near.Equal(90f, Vector3.Angle(Vector3.right, Vector3.up), 1e-4f);
            Near.Equal(90f, Vector3.SignedAngle(Vector3.right, Vector3.up, Vector3.forward), 1e-4f);
            Near.Equal(-90f, Vector3.SignedAngle(Vector3.up, Vector3.right, Vector3.forward), 1e-4f);
            Near.Equal(0f, Vector3.Angle(Vector3.zero, Vector3.up));
            Near.Equal(90f, Vector2.SignedAngle(Vector2.right, Vector2.up), 1e-4f);
            Near.Equal(-45f, Vector2.SignedAngle(Vector2.up, new Vector2(1f, 1f)), 1e-4f);
        }

        [Fact]
        public void ProjectionsAndReflection()
        {
            Near.Equal(new Vector3(3f, 0f, 0f), Vector3.Project(new Vector3(3f, 4f, 5f), new Vector3(2f, 0f, 0f)));
            Near.Equal(new Vector3(0f, 4f, 5f), Vector3.ProjectOnPlane(new Vector3(3f, 4f, 5f), Vector3.right * 2f));
            Assert.Equal(Vector3.zero, Vector3.Project(Vector3.one, Vector3.zero));
            Near.Equal(new Vector3(1f, 1f, 0f), Vector3.Reflect(new Vector3(1f, -1f, 0f), Vector3.up));
            Near.Equal(new Vector2(1f, 1f), Vector2.Reflect(new Vector2(1f, -1f), Vector2.up));
            Assert.Equal(new Vector2(-2f, 1f), Vector2.Perpendicular(new Vector2(1f, 2f)));
            Near.Equal(new Vector3(0f, 0f, 1f), Vector3.Cross(Vector3.right, Vector3.up));
        }

        [Fact]
        public void ClampMagnitudeAndMoveTowards()
        {
            Near.Equal(new Vector3(0.6f, 0.8f, 0f), Vector3.ClampMagnitude(new Vector3(3f, 4f, 0f), 1f));
            Assert.Equal(new Vector3(0.3f, 0.4f, 0f), Vector3.ClampMagnitude(new Vector3(0.3f, 0.4f, 0f), 1f));
            Near.Equal(new Vector3(1f, 0f, 0f), Vector3.MoveTowards(Vector3.zero, new Vector3(10f, 0f, 0f), 1f));
            Assert.Equal(new Vector3(10f, 0f, 0f), Vector3.MoveTowards(Vector3.zero, new Vector3(10f, 0f, 0f), 20f));
            Near.Equal(new Vector2(0f, 2f), Vector2.MoveTowards(Vector2.zero, new Vector2(0f, 5f), 2f));
        }

        [Fact]
        public void SlerpInterpolatesDirectionAndLength()
        {
            Vector3 r = Vector3.Slerp(Vector3.right * 2f, Vector3.up, 0.5f);
            Near.Equal(1.5f, r.magnitude, 1e-5f);
            Near.Equal(45f, Vector3.Angle(Vector3.right, r), 1e-3f);
            // Opposite vectors still rotate through 90 degrees at the halfway point.
            Vector3 o = Vector3.Slerp(Vector3.right, Vector3.left, 0.5f);
            Near.Equal(1f, o.magnitude, 1e-5f);
            Near.Equal(90f, Vector3.Angle(Vector3.right, o), 1e-3f);
            Near.Equal(Vector3.up * 3f, Vector3.Slerp(Vector3.up, Vector3.up * 5f, 0.5f));
        }

        [Fact]
        public void RotateTowardsLimitsAngleAndMagnitude()
        {
            Vector3 r = Vector3.RotateTowards(Vector3.right, Vector3.up * 3f, Mathf.PI / 4f, 0f);
            Near.Equal(new Vector3(Mathf.Sqrt(0.5f), Mathf.Sqrt(0.5f), 0f), r);
            Vector3 full = Vector3.RotateTowards(Vector3.right, Vector3.up * 3f, Mathf.PI, 1f);
            Near.Equal(new Vector3(0f, 2f, 0f), full, 1e-5f);
        }

        [Fact]
        public void OrthoNormalizeBuildsOrthonormalBasis()
        {
            Vector3 n = new Vector3(0f, 2f, 0f), t = new Vector3(1f, 1f, 0f), b = new Vector3(1f, 1f, 1f);
            Vector3.OrthoNormalize(ref n, ref t, ref b);
            Near.Equal(Vector3.up, n);
            Near.Equal(Vector3.right, t);
            Near.Equal(Vector3.forward, b);
            Vector3 n2 = Vector3.up, t2 = Vector3.up * 3f;
            Vector3.OrthoNormalize(ref n2, ref t2);
            Near.Equal(0f, Vector3.Dot(n2, t2));
            Near.Equal(1f, t2.magnitude);
        }

        [Fact]
        public void SmoothDampVectorConverges()
        {
            Vector3 current = Vector3.zero, velocity = Vector3.zero, target = new Vector3(5f, -3f, 2f);
            for (int i = 0; i < 300; i++)
                current = Vector3.SmoothDamp(current, target, ref velocity, 0.2f, Mathf.Infinity, 1f / 60f);
            Near.Equal(target, current, 1e-3f);
            Vector2 c2 = Vector2.zero, v2 = Vector2.zero;
            for (int i = 0; i < 300; i++)
                c2 = Vector2.SmoothDamp(c2, Vector2.one, ref v2, 0.2f, Mathf.Infinity, 1f / 60f);
            Near.Equal(Vector2.one, c2, 1e-3f);
        }

        [Fact]
        public void IndexersThrowUnityMessages()
        {
            var v = new Vector3(1f, 2f, 3f);
            Assert.Equal(3f, v[2]);
            var ex = Assert.Throws<IndexOutOfRangeException>(() => v[3]);
            Assert.Equal("Invalid Vector3 index!", ex.Message);
            Assert.Equal("Invalid Vector2 index!", Assert.Throws<IndexOutOfRangeException>(() => new Vector2()[2]).Message);
            Assert.Equal("Invalid Vector2Int index addressed: 5!", Assert.Throws<IndexOutOfRangeException>(() => new Vector2Int()[5]).Message);
        }

        [Fact]
        public void ToStringUsesInvariantCultureAndDefaultPrecision()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("(1.23, 2.00, -3.46)", new Vector3(1.234f, 2f, -3.456f).ToString());
                Assert.Equal("(1.2, 2.0, -3.5)", new Vector3(1.234f, 2f, -3.456f).ToString("F1"));
                Assert.Equal("(0.50, 0.25)", new Vector2(0.5f, 0.25f).ToString());
                Assert.Equal("(1.00, 2.00, 3.00, 4.00)", new Vector4(1f, 2f, 3f, 4f).ToString());
                Assert.Equal("(1, -2)", new Vector2Int(1, -2).ToString());
                Assert.Equal("(1, 2, 3)", new Vector3Int(1, 2, 3).ToString());
                Assert.Equal("(1,5, 2,0)", new Vector2(1.5f, 2f).ToString("F1", new CultureInfo("de-DE")));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void ConversionsBetweenVectorTypes()
        {
            Vector3 v3 = new Vector2(1f, 2f);
            Assert.Equal(new Vector3(1f, 2f, 0f), v3);
            Vector2 v2 = new Vector3(1f, 2f, 3f);
            Assert.Equal(new Vector2(1f, 2f), v2);
            Vector4 v4 = new Vector3(1f, 2f, 3f);
            Assert.Equal(new Vector4(1f, 2f, 3f, 0f), v4);
            Vector2 fromInt = new Vector2Int(3, 4);
            Assert.Equal(new Vector2(3f, 4f), fromInt);
            Assert.Equal(new Vector3Int(3, 4, 0), (Vector3Int)new Vector2Int(3, 4));
            Assert.Equal(new Vector2Int(3, 4), (Vector2Int)new Vector3Int(3, 4, 5));
            Vector3 fromInt3 = new Vector3Int(1, 2, 3);
            Assert.Equal(new Vector3(1f, 2f, 3f), fromInt3);
        }

        [Fact]
        public void IntVectors()
        {
            Assert.Equal(new Vector2Int(2, -1), Vector2Int.CeilToInt(new Vector2(1.2f, -1.7f)));
            Assert.Equal(new Vector2Int(1, -2), Vector2Int.FloorToInt(new Vector2(1.2f, -1.7f)));
            Assert.Equal(new Vector3Int(2, 2, -2), Vector3Int.RoundToInt(new Vector3(2.5f, 1.6f, -2.4f)));
            Assert.Equal(5f, new Vector2Int(3, 4).magnitude);
            Assert.Equal(25, new Vector2Int(3, 4).sqrMagnitude);
            Assert.Equal(5f, Vector2Int.Distance(Vector2Int.zero, new Vector2Int(3, -4)));
            Assert.Equal(new Vector2Int(1, 1), new Vector2Int(3, 3) / 2);
            var c = new Vector3Int(5, -5, 2);
            c.Clamp(new Vector3Int(0, 0, 0), new Vector3Int(3, 3, 3));
            Assert.Equal(new Vector3Int(3, 0, 2), c);
        }

        [Fact]
        public void FieldLayoutMatchesUnity()
        {
            Assert.Equal(12, Marshal.SizeOf<Vector3>());
            Assert.Equal(8, (int)Marshal.OffsetOf<Vector3>("z"));
            Assert.Equal(16, Marshal.SizeOf<Quaternion>());
            Assert.Equal(12, (int)Marshal.OffsetOf<Quaternion>("w"));
            Assert.Equal(8, Marshal.SizeOf<Vector2Int>());
        }

        [Fact]
        public void HashCodesAreStable()
        {
            Assert.Equal(new Vector3(1f, 2f, 3f).GetHashCode(), new Vector3(1f, 2f, 3f).GetHashCode());
            Assert.Equal(1f.GetHashCode() ^ (2f.GetHashCode() << 2), new Vector2(1f, 2f).GetHashCode());
        }
    }
}
