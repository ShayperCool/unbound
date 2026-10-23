using System;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class QuaternionTests
    {
        private const float S = 0.70710677f;

        [Fact]
        public void EulerUsesZxyOrder()
        {
            Quaternion q = Quaternion.Euler(30f, 45f, 60f);
            Quaternion composed = Quaternion.AngleAxis(45f, Vector3.up) * Quaternion.AngleAxis(30f, Vector3.right) *
                                  Quaternion.AngleAxis(60f, Vector3.forward);
            Near.Equal(composed, q, 1e-6f);
        }

        [Fact]
        public void EulerKnownValues()
        {
            Near.Equal(new Quaternion(0f, 1f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f), 1e-6f);
            Near.Equal(new Quaternion(S, 0f, 0f, S), Quaternion.Euler(90f, 0f, 0f), 1e-6f);
            Near.Equal(new Quaternion(0f, S, 0f, -S), Quaternion.Euler(0f, 270f, 0f), 1e-6f);
            Near.Equal(Vector3.right, Quaternion.Euler(0f, 90f, 0f) * Vector3.forward);
            Near.Equal(Vector3.down, Quaternion.Euler(90f, 0f, 0f) * Vector3.forward);
            Near.Equal(Vector3.up, Quaternion.Euler(0f, 0f, 90f) * Vector3.right);
        }

        [Theory]
        [InlineData(30f, 45f, 60f)]
        [InlineData(10f, 200f, 300f)]
        [InlineData(80f, 359f, 1f)]
        [InlineData(300f, 120f, 240f)]
        [InlineData(0f, 0f, 0f)]
        public void EulerAnglesRoundTrip(float x, float y, float z)
        {
            Vector3 e = Quaternion.Euler(x, y, z).eulerAngles;
            Near.Equal(new Vector3(x, y, z), e, 2e-3f);
        }

        [Fact]
        public void EulerAnglesAreInZeroTo360()
        {
            Near.Equal(new Vector3(330f, 315f, 300f), Quaternion.Euler(-30f, -45f, -60f).eulerAngles, 2e-3f);
            Vector3 e = Quaternion.Euler(0f, -0.001f, 0f).eulerAngles;
            Assert.True(e.y < 0f && e.y > -0.002f, "tiny negative angles stay negative, as in Unity: " + e.y);
        }

        [Fact]
        public void EulerAnglesInGimbalLockPutRotationIntoY()
        {
            Vector3 e = Quaternion.Euler(90f, 45f, 0f).eulerAngles;
            Near.Equal(new Vector3(90f, 45f, 0f), e, 2e-2f);
            Quaternion q = Quaternion.Euler(90f, 10f, 30f);
            Vector3 e2 = q.eulerAngles;
            Near.Equal(0f, e2.z, 1e-3f);
            Near.SameRotation(q, Quaternion.Euler(e2), 2e-2f);
            Quaternion q3 = Quaternion.Euler(-90f, 10f, 30f);
            Near.SameRotation(q3, Quaternion.Euler(q3.eulerAngles), 2e-2f);
        }

        [Fact]
        public void EulerAnglesSetter()
        {
            Quaternion q = Quaternion.identity;
            q.eulerAngles = new Vector3(0f, 90f, 0f);
            Near.Equal(Vector3.right, q * Vector3.forward);
        }

        [Fact]
        public void LookRotation()
        {
            Near.Equal(Quaternion.identity, Quaternion.LookRotation(Vector3.forward));
            Near.SameRotation(Quaternion.Euler(0f, 90f, 0f), Quaternion.LookRotation(Vector3.right));
            Near.Equal(new Quaternion(0f, 1f, 0f, 0f), Quaternion.LookRotation(Vector3.back), 1e-6f);
            Near.Equal(new Quaternion(-S, 0f, 0f, S), Quaternion.LookRotation(Vector3.up), 1e-6f);
            Near.Equal(new Quaternion(S, 0f, 0f, S), Quaternion.LookRotation(Vector3.down), 1e-6f);
            Assert.True(Quaternion.LookRotation(Vector3.zero).Equals(Quaternion.identity));

            Vector3 fwd = new Vector3(1f, 2f, 3f);
            Vector3 up = new Vector3(0f, 1f, 0.2f);
            Quaternion q = Quaternion.LookRotation(fwd, up);
            Near.Equal(fwd.normalized, q * Vector3.forward);
            Near.Equal(0f, Vector3.Dot(q * Vector3.right, up), 1e-5f);
            Assert.True(Vector3.Dot(q * Vector3.up, up) > 0f);
            Near.Equal(1f, Mathf.Sqrt(Quaternion.Dot(q, q)), 1e-6f);

            Quaternion v = Quaternion.identity;
            v.SetLookRotation(Vector3.left);
            Near.Equal(Vector3.left, v * Vector3.forward);
        }

        [Fact]
        public void FromToRotation()
        {
            Vector3 a = new Vector3(1f, 2f, -1f), b = new Vector3(-3f, 0.5f, 2f);
            Near.Equal(b.normalized, Quaternion.FromToRotation(a, b) * a.normalized);
            Near.Equal(Vector3.down, Quaternion.FromToRotation(Vector3.up, Vector3.down) * Vector3.up);
            Near.Equal(new Quaternion(1f, 0f, 0f, 0f), Quaternion.FromToRotation(Vector3.up, Vector3.down), 1e-6f);
            Near.Equal(Vector3.back, Quaternion.FromToRotation(Vector3.forward, Vector3.back) * Vector3.forward);
            Near.Equal(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, Vector3.up * 4f));
            Near.Equal(Quaternion.Euler(0f, 0f, 90f), Quaternion.FromToRotation(Vector3.right, Vector3.up), 1e-6f);
        }

        [Fact]
        public void AngleAxisRoundTrip()
        {
            Quaternion q = Quaternion.AngleAxis(30f, new Vector3(1f, 1f, 0f));
            q.ToAngleAxis(out float angle, out Vector3 axis);
            Near.Equal(30f, angle, 1e-3f);
            Near.Equal(new Vector3(1f, 1f, 0f).normalized, axis, 1e-5f);

            Quaternion.identity.ToAngleAxis(out float a0, out Vector3 ax0);
            Assert.Equal(0f, a0);
            Assert.Equal(Vector3.right, ax0);
            Assert.True(Quaternion.AngleAxis(45f, Vector3.zero).Equals(Quaternion.identity));
        }

        [Fact]
        public void SlerpLerpAndRotateTowards()
        {
            Near.SameRotation(Quaternion.Euler(0f, 45f, 0f), Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 0.5f));
            // Shortest path: from 0 to 270 goes through -45.
            Near.SameRotation(Quaternion.Euler(0f, -45f, 0f), Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, 270f, 0f), 0.5f));
            Near.SameRotation(Quaternion.Euler(0f, 90f, 0f), Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 2f));
            Near.SameRotation(Quaternion.Euler(0f, 180f, 0f), Quaternion.SlerpUnclamped(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 2f));
            Near.SameRotation(Quaternion.Euler(0f, 45f, 0f), Quaternion.Lerp(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 0.5f));
            Near.SameRotation(Quaternion.Euler(0f, -45f, 0f), Quaternion.Lerp(Quaternion.identity, Quaternion.Euler(0f, 270f, 0f), 0.5f));

            Quaternion r = Quaternion.RotateTowards(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), 30f);
            Near.Equal(30f, Quaternion.Angle(Quaternion.identity, r), 1e-2f);
            Assert.True(Quaternion.RotateTowards(Quaternion.identity, Quaternion.Euler(0f, 10f, 0f), 30f) == Quaternion.Euler(0f, 10f, 0f));
        }

        [Fact]
        public void AngleAndEquality()
        {
            Near.Equal(90f, Quaternion.Angle(Quaternion.identity, Quaternion.Euler(0f, 90f, 0f)), 1e-3f);
            Quaternion q = Quaternion.Euler(10f, 20f, 30f);
            Quaternion neg = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            Assert.False(q == neg); // the dot product is -1
            Near.Equal(0f, Quaternion.Angle(q, neg));
            Assert.True(q == Quaternion.Euler(10f, 20f, 30.01f));
            Assert.False(q.Equals(Quaternion.Euler(10f, 20f, 30.01f)));
            Assert.False(q == Quaternion.Euler(10f, 20f, 31f));
        }

        [Fact]
        public void InverseAndComposition()
        {
            Quaternion q = Quaternion.Euler(10f, 20f, 30f);
            Near.SameRotation(Quaternion.identity, q * Quaternion.Inverse(q));
            Near.Equal(Vector3.back, Quaternion.Euler(0f, 90f, 0f) * (Quaternion.Euler(0f, 90f, 0f) * Vector3.forward));
            Near.Equal(Vector3.back, (Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 90f, 0f)) * Vector3.forward);
            Quaternion n = new Quaternion(0f, 0f, 0f, 2f).normalized;
            Assert.True(n.Equals(Quaternion.identity));
            Assert.True(Quaternion.Normalize(new Quaternion(0f, 0f, 0f, 0f)).Equals(Quaternion.identity));
        }

        [Fact]
        public void ToStringUsesFiveDecimals()
        {
            Assert.Equal("(0.00000, 0.00000, 0.00000, 1.00000)", Quaternion.identity.ToString());
            Assert.Equal("(0.0, 0.0, 0.0, 1.0)", Quaternion.identity.ToString("F1"));
        }
    }
}
