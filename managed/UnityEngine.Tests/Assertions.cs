using System;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    internal static class Near
    {
        public static void Equal(float expected, float actual, float tolerance = 1e-5f)
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected:R}, got {actual:R} (tolerance {tolerance})");
        }

        public static void Equal(Vector2 expected, Vector2 actual, float tolerance = 1e-5f)
        {
            Assert.True(Math.Abs(expected.x - actual.x) <= tolerance && Math.Abs(expected.y - actual.y) <= tolerance,
                $"expected {expected.ToString("R")}, got {actual.ToString("R")}");
        }

        public static void Equal(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
        {
            Assert.True(Math.Abs(expected.x - actual.x) <= tolerance && Math.Abs(expected.y - actual.y) <= tolerance &&
                        Math.Abs(expected.z - actual.z) <= tolerance,
                $"expected {expected.ToString("R")}, got {actual.ToString("R")}");
        }

        public static void Equal(Color expected, Color actual, float tolerance = 1e-5f)
        {
            Assert.True(Math.Abs(expected.r - actual.r) <= tolerance && Math.Abs(expected.g - actual.g) <= tolerance &&
                        Math.Abs(expected.b - actual.b) <= tolerance && Math.Abs(expected.a - actual.a) <= tolerance,
                $"expected {expected.ToString("R")}, got {actual.ToString("R")}");
        }

        /// <summary>Same rotation (q and -q are the same rotation).</summary>
        public static void SameRotation(Quaternion expected, Quaternion actual, float toleranceDegrees = 1e-3f)
        {
            float angle = Quaternion.Angle(expected, actual);
            Assert.True(angle <= toleranceDegrees, $"expected {expected.ToString("R")}, got {actual.ToString("R")} ({angle} degrees apart)");
        }

        /// <summary>Same components (sign included).</summary>
        public static void Equal(Quaternion expected, Quaternion actual, float tolerance = 1e-5f)
        {
            Assert.True(Math.Abs(expected.x - actual.x) <= tolerance && Math.Abs(expected.y - actual.y) <= tolerance &&
                        Math.Abs(expected.z - actual.z) <= tolerance && Math.Abs(expected.w - actual.w) <= tolerance,
                $"expected {expected.ToString("R")}, got {actual.ToString("R")}");
        }

        public static void Equal(Matrix4x4 expected, Matrix4x4 actual, float tolerance = 1e-5f)
        {
            for (int i = 0; i < 16; i++)
                Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"element {i}: expected\n{expected.ToString("R")}got\n{actual.ToString("R")}");
        }
    }
}
