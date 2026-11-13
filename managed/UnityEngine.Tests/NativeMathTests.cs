using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// The math Unity binds to native code: exact values the Unity 6000.0.71f1 Editor recorded on
    /// both Linux x86_64 and macOS arm64 (tests/projects/floatrounding), and the branches its
    /// algorithms take for nearly equal, opposite and degenerate input.
    /// </summary>
    public class NativeMathTests
    {
        static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        static uint[] Bits(params float[] values) => values.Select(BitConverter.SingleToUInt32Bits).ToArray();
        static Vector3 V(uint x, uint y, uint z) => new Vector3(F(x), F(y), F(z));

        [Fact]
        public void LookRotationMatchesEditor()
        {
            Quaternion q = Quaternion.LookRotation(V(0x3BD5AC85, 0x40C2D385, 0xBDDDF7B7), V(0x3D9FD27A, 0xC091931B, 0xC35A9D8E));
            Assert.Equal(new uint[] { 0xBF369F8A, 0x398161FB, 0xBA0534DD, 0x3F3366AE }, Bits(q.x, q.y, q.z, q.w));
        }

        [Fact]
        public void FromToRotationMatchesEditor()
        {
            Quaternion q = Quaternion.FromToRotation(V(0xC07C67FB, 0x41E9C50A, 0x3F772A49), V(0x3DF8ECFB, 0xBFA04947, 0x40CB28F1));
            Assert.Equal(new uint[] { 0x3F416226, 0x3DD06BA8, 0x3BB7DA5D, 0x3F25B46C }, Bits(q.x, q.y, q.z, q.w));
        }

        [Fact]
        public void InverseMatchesEditor()
        {
            uint[] input = { 0xBD822B24, 0x43DAB3A7, 0xC0296077, 0x3C3F6EB5, 0x433AD5D7, 0xBE5FAE72, 0x3B8461FB, 0xC3598483,
                             0x3BA31AF7, 0xC29E3D87, 0xC0283EC8, 0xC10DE625, 0xC30F150E, 0x43958659, 0x4093D175, 0x3EC36A4A };
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
                m[i] = F(input[i]);
            Matrix4x4 inverse = m.inverse;
            Assert.Equal(new uint[] { 0x3AFFFF72, 0x3A08A295, 0xBC55DAF9, 0xBBCEC1A1, 0x3AF9BC5E, 0x38969B33, 0xBAE6031C, 0x38C2C1D0,
                                      0xBD810128, 0x3C41F5AF, 0xBE945CC1, 0x3C7D9058, 0x3ADB7BE1, 0xBB87F85F, 0xBC37BFA9, 0xBBB195ED },
                Enumerable.Range(0, 16).Select(i => BitConverter.SingleToUInt32Bits(inverse[i])).ToArray());
        }

        [Fact]
        public void MirroringRotationMatchesEditor()
        {
            // TRS with scale (-0.0148, 0.0081, 2.37): a reflection, decomposed through the SVD.
            var m = Matrix4x4.TRS(V(0xBE2C3A41, 0x411F7771, 0xC0833195), new Quaternion(F(0x3F762E20), F(0xC276C40D), F(0xC0929B6A), F(0xBCE7BE3E)).normalized,
                V(0xBC732ADF, 0x3C049894, 0x40176A86));
            Quaternion q = m.rotation;
            Assert.Equal(new uint[] { 0x3F7F441F, 0x3C7EA913, 0x39EFBA02, 0xBD97A83C }, Bits(q.x, q.y, q.z, q.w));
            Vector3 s = m.lossyScale;
            Assert.Equal(new uint[] { 0xBC732ADD, 0x3C049894, 0x40176A84 }, Bits(s.x, s.y, s.z));
        }

        [Fact]
        public void LookRotationFallsBackToFromForward()
        {
            var forward = new Vector3(0.3f, -2f, 0.7f);
            // Up parallel to forward: no frame, so the shortest rotation from Z to forward.
            Assert.Equal(Quaternion.FromToRotation(Vector3.forward, forward), Quaternion.LookRotation(forward, forward * -3f));
            Assert.Equal(Quaternion.identity, Quaternion.LookRotation(Vector3.zero, Vector3.up));
            Assert.Equal(Quaternion.identity, Quaternion.LookRotation(forward * 1e-6f, Vector3.up));
            Assert.NotEqual(Quaternion.identity, Quaternion.LookRotation(forward * 1e-4f, Vector3.right));
        }

        [Fact]
        public void FromToRotationTurnsOppositeDirectionsHalfway()
        {
            var from = new Vector3(0.2f, 0.9f, -0.4f);
            Quaternion q = Quaternion.FromToRotation(from, from * -5f);
            Near.Equal(-from.normalized, q * from.normalized, 1e-5f);
            Near.Equal(0f, q.w, 1e-6f);
            Assert.Equal(Quaternion.identity, Quaternion.FromToRotation(from, from * 2f));
            Assert.Equal(Quaternion.identity, Quaternion.FromToRotation(from * 1e-6f, Vector3.up));
        }

        [Fact]
        public void AngleAxisNeedsAnAxisLongerThan1e6()
        {
            Assert.Equal(Quaternion.identity, Quaternion.AngleAxis(30f, new Vector3(0f, 1e-6f, 0f)));
            Quaternion q = Quaternion.AngleAxis(30f, new Vector3(0f, 2e-6f, 0f));
            Near.Equal(Quaternion.AngleAxis(30f, Vector3.up), q, 1e-6f);
        }

        [Fact]
        public void EulerAnglesPutGimbalLockIntoY()
        {
            Vector3 e = Quaternion.Euler(90f, 30f, 20f).eulerAngles;
            Assert.Equal(0f, e.z);
            Near.Equal(90f, e.x, 1e-3f);
            Near.Equal(10f, e.y, 1e-2f);
            // Below 1e-5 in length a rotation counts as identity.
            Assert.Equal(Vector3.zero, new Quaternion(1e-7f, 2e-7f, 0f, 3e-7f).eulerAngles);
        }

        [Fact]
        public void SlerpOfNearlyEqualRotationsIsNormalizedLerp()
        {
            Quaternion a = Quaternion.Euler(10f, 20f, 30f);
            Quaternion b = Quaternion.Euler(11f, 21f, 29f);
            Assert.True(Quaternion.Dot(a, b) >= 0.95f);
            Assert.Equal(Quaternion.Lerp(a, b, 0.3f), Quaternion.Slerp(a, b, 0.3f));
            // The other sign of b is the same rotation: both interpolate along the shorter arc.
            var negated = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            Assert.Equal(Quaternion.Slerp(a, b, 0.3f), Quaternion.Slerp(a, negated, 0.3f));
            Quaternion far = Quaternion.Euler(80f, 0f, 0f);
            Near.SameRotation(Quaternion.Euler(40f, 0f, 0f), Quaternion.Slerp(Quaternion.identity, far, 0.5f), 1e-3f);
        }

        [Fact]
        public void VectorSlerpAndRotateTowardsHandleOppositeAndParallel()
        {
            var v = new Vector3(0f, 0f, 2f);
            Vector3 half = Vector3.Slerp(v, v * -3f, 0.5f);
            Near.Equal(0f, half.z, 1e-5f);
            // The length moves linearly: halfway from 2 to 6.
            Near.Equal(4f, half.magnitude, 1e-5f);
            Assert.Equal(new Vector3(0f, 0f, 3f), Vector3.Slerp(v, v * 2f, 0.5f));
            Vector3 turned = Vector3.RotateTowards(v, v * -1f, Mathf.PI * 0.5f, 0f);
            Near.Equal(0f, turned.z, 1e-5f);
            Near.Equal(2f, turned.magnitude, 1e-5f);
            // Parallel: straight towards the target by at most the magnitude delta.
            Assert.Equal(new Vector3(0f, 0f, 2.5f), Vector3.RotateTowards(v, v * 3f, 1f, 0.5f));
            Assert.Equal(new Vector3(0f, 0f, 6f), Vector3.RotateTowards(v, v * 3f, 1f, 5f));
        }

        [Fact]
        public void InverseNeedsPivotingAndZeroesSingularMatrices()
        {
            var m = new Matrix4x4(new Vector4(0f, 1f, 0f, 0f), new Vector4(2f, 0f, 0f, 0f), new Vector4(0f, 0f, 0f, 3f), new Vector4(1f, 0f, 4f, 0f));
            Matrix4x4 product = m * m.inverse;
            for (int i = 0; i < 16; i++)
                Near.Equal(Matrix4x4.identity[i], product[i], 1e-6f);
            Matrix4x4 singular = m;
            singular.SetRow(1, Vector4.zero);
            Assert.Equal(Matrix4x4.zero, singular.inverse);
        }

        [Fact]
        public void RotationOfDegenerateScaleIsAUnitRotation()
        {
            Quaternion rotation = Quaternion.Euler(30f, 60f, 10f);
            Quaternion q = Matrix4x4.TRS(Vector3.one, rotation, new Vector3(2f, 0f, 2f)).rotation;
            Near.Equal(1f, Mathf.Sqrt(Quaternion.Dot(q, q)), 1e-6f);
            Near.SameRotation(rotation, q, 1e-2f);
            Near.SameRotation(rotation, Matrix4x4.TRS(Vector3.one, rotation, Vector3.one * 3f).rotation, 1e-3f);
        }

        [Fact]
        public void BoundsReturnPointsOnAndInsideUnchanged()
        {
            var box = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            Vector3 corner = box.max;
            Assert.Equal(0f, box.SqrDistance(corner));
            Assert.True(box.Contains(corner));
            Assert.Equal(corner, box.ClosestPoint(corner));
            var inside = new Vector3(1.1f, 2.3f, 3.7f);
            Assert.Equal(inside, box.ClosestPoint(inside));
            var outside = new Vector3(3f, 2f, -1f);
            Assert.False(box.Contains(outside));
            Assert.Equal(1f + 1f, box.SqrDistance(outside));
            Assert.Equal(new Vector3(2f, 2f, 0f), box.ClosestPoint(outside));
        }

        [Fact]
        public void RotateTowardsWithANegativeAngleSwingsAroundTheOppositeDirection()
        {
            // The Scripting API says the vector stops once it points away from the target; the
            // Editor's values (native/degenerate) keep turning by the angle about an axis
            // orthogonal to it, so the angle to the target alternates.
            var target = new Vector3(0.3f, -1.2f, 2f);
            Vector3 current = -target + Vector3.Cross(target, new Vector3(1f, 0.5f, 0.2f)) * 1e-3f;
            var angles = new List<float>();
            for (int i = 0; i < 4; i++)
            {
                current = Vector3.RotateTowards(current, target, -0.3f, 0.5f);
                angles.Add(Vector3.Angle(current, target));
            }
            Near.Equal(180f - 0.3f * Mathf.Rad2Deg, angles[0], 0.1f);
            Assert.True(angles[1] > 179.9f);
            Near.Equal(angles[0], angles[2], 0.01f);
            Near.Equal(target.magnitude, current.magnitude, 1e-4f);
        }

        [Fact]
        public void SqrDistanceOfANaNCoordinateFollowsTheHostEditor()
        {
            // The arm64 Editor's larger-of-two gives NaN for a NaN distance, the x86_64 Editor's the extent.
            var box = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            float squared = box.SqrDistance(new Vector3(float.NaN, 3f, 0f));
            if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
                Assert.True(float.IsNaN(squared));
            else
                Assert.Equal(4f, squared);
        }

        [Fact]
        public void LogsTheEditorsDiagnosticsForDegenerateOperands()
        {
            var logs = new List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            bool wasEnabled = logger.logEnabled;
            var filter = logger.filterLogType;
            logger.logHandler = new Capture(logs);
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                Assert.Equal(Quaternion.identity, Quaternion.LookRotation(Vector3.zero, Vector3.up));
                Assert.Equal(Quaternion.identity, Quaternion.LookRotationSilent(Vector3.zero, Vector3.up));
                Quaternion lerp = Quaternion.Lerp(default, default, 0.5f);
                Assert.True(float.IsNaN(lerp.w));
                _ = Matrix4x4.zero.rotation;
                _ = Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.zero).rotation;
                var box = new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one);
                Assert.True(box.Contains(Vector3.zero));
                _ = box.SqrDistance(Vector3.zero);
                _ = box.SqrDistanceUnchecked(Vector3.zero);
            }
            finally
            {
                logger.logHandler = previous;
                logger.logEnabled = wasEnabled;
                logger.filterLogType = filter;
            }
            Assert.Equal(new[]
            {
                "Log: Look rotation viewing vector is zero",
                "Assert: Assertion failed on expression: '!CompareApproximately(aScalar, 0.0F)'",
                "Assert: Assertion failed on expression: 'ValidTRS()'",
                "Assert: Invalid AABB *this",
                "Assert: Invalid AABB rkBox",
            }, logs.ToArray());
        }

        sealed class Capture : ILogHandler
        {
            readonly List<string> m_Logs;
            internal Capture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args) =>
                m_Logs.Add($"{type}: {(args == null || args.Length == 0 ? format : string.Format(format, args))}");
            public void LogException(Exception exception, UnityEngine.Object context) => m_Logs.Add("Exception: " + exception);
        }
    }
}
