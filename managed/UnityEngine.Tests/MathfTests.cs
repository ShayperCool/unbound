using System;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class MathfTests
    {
        [Fact]
        public void EpsilonIsSmallestPositiveFloat()
        {
            Assert.Equal(float.Epsilon, Mathf.Epsilon);
            Assert.Equal(0.017453292f, Mathf.Deg2Rad);
            Assert.Equal(57.29578f, Mathf.Rad2Deg);
        }

        [Theory]
        [InlineData(1f, 1.0000001f, true)]
        [InlineData(1000f, 1000.0009f, true)]
        [InlineData(1000f, 1000.002f, false)]
        [InlineData(0f, 1e-6f, false)]
        [InlineData(0f, 1e-44f, true)]
        [InlineData(0f, 0f, true)]
        public void ApproximatelyUsesRelativeTolerance(float a, float b, bool expected)
        {
            Assert.Equal(expected, Mathf.Approximately(a, b));
        }

        [Fact]
        public void RepeatPingPongAndAngles()
        {
            Near.Equal(2f, Mathf.Repeat(-1f, 3f));
            Near.Equal(0.5f, Mathf.Repeat(3.5f, 3f));
            Near.Equal(2f, Mathf.PingPong(4f, 3f));
            Near.Equal(1f, Mathf.PingPong(1f, 3f));
            Near.Equal(90f, Mathf.DeltaAngle(1080f, 90f));
            Near.Equal(-20f, Mathf.DeltaAngle(10f, 350f));
            Near.Equal(180f, Mathf.DeltaAngle(0f, 180f));
            Near.Equal(360f, Mathf.LerpAngle(350f, 10f, 0.5f));
            Near.Equal(-5f, Mathf.MoveTowardsAngle(10f, 350f, 15f));
            Near.Equal(350f, Mathf.MoveTowardsAngle(10f, 350f, 25f));
            Near.Equal(0f, Mathf.InverseLerp(5f, 5f, 3f));
            Near.Equal(1f, Mathf.InverseLerp(0f, 2f, 5f));
            Near.Equal(1.5f, Mathf.MoveTowards(1f, 3f, 0.5f));
            Near.Equal(3f, Mathf.MoveTowards(1f, 3f, 5f));
            Near.Equal(0.5f, Mathf.SmoothStep(0f, 1f, 0.5f));
            Near.Equal(0.15625f, Mathf.SmoothStep(0f, 1f, 0.25f));
        }

        [Fact]
        public void RoundingIsBankers()
        {
            Assert.Equal(2, Mathf.RoundToInt(2.5f));
            Assert.Equal(4, Mathf.RoundToInt(3.5f));
            Assert.Equal(-2, Mathf.RoundToInt(-2.5f));
            Assert.Equal(2f, Mathf.Round(2.5f));
            Assert.Equal(-3, Mathf.FloorToInt(-2.5f));
            Assert.Equal(-2, Mathf.CeilToInt(-2.5f));
            Assert.Equal(1f, Mathf.Sign(0f));
            Assert.Equal(-1f, Mathf.Sign(-0.1f));
        }

        [Fact]
        public void PowersOfTwo()
        {
            Assert.Equal(4, Mathf.ClosestPowerOfTwo(5));
            Assert.Equal(8, Mathf.ClosestPowerOfTwo(7));
            Assert.Equal(8, Mathf.NextPowerOfTwo(5));
            Assert.Equal(8, Mathf.NextPowerOfTwo(8));
            Assert.Equal(1, Mathf.NextPowerOfTwo(1));
            Assert.True(Mathf.IsPowerOfTwo(64));
            Assert.False(Mathf.IsPowerOfTwo(65));
        }

        [Fact]
        public void MinMaxParams()
        {
            Assert.Equal(0f, Mathf.Max(new float[0]));
            Assert.Equal(0, Mathf.Min(new int[0]));
            Assert.Equal(7f, Mathf.Max(1f, 7f, 3f));
            Assert.Equal(-2, Mathf.Min(4, -2, 3));
        }

        [Fact]
        public void ColorSpaceConversions()
        {
            Near.Equal(0.21404114f, Mathf.GammaToLinearSpace(0.5f), 1e-6f);
            Near.Equal(0.5f, Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(0.5f)), 1e-5f);
            Near.Equal(0.01f / 12.92f, Mathf.GammaToLinearSpace(0.01f), 1e-7f);
            Assert.Equal(1f, Mathf.GammaToLinearSpace(1f));
            Assert.Equal(0f, Mathf.LinearToGammaSpace(-1f));
        }

        [Fact]
        public void HalfRoundTrip()
        {
            Assert.Equal((ushort)0x3C00, Mathf.FloatToHalf(1f));
            Assert.Equal(-2.5f, Mathf.HalfToFloat(Mathf.FloatToHalf(-2.5f)));
        }

        [Fact]
        public void SmoothDampConvergesWithoutOvershoot()
        {
            float current = 0f, velocity = 0f;
            const float dt = 1f / 60f;
            float previous = current;
            for (int i = 0; i < 180; i++)
            {
                current = Mathf.SmoothDamp(current, 10f, ref velocity, 0.3f, Mathf.Infinity, dt);
                Assert.True(current <= 10f, "overshoot");
                Assert.True(current >= previous, "not monotonic");
                previous = current;
            }
            Near.Equal(10f, current, 1e-3f);
            Near.Equal(0f, velocity, 1e-2f);
        }

        [Fact]
        public void SmoothDampRespectsMaxSpeed()
        {
            float current = 0f, velocity = 0f;
            const float dt = 0.02f;
            for (int i = 0; i < 50; i++)
            {
                float before = current;
                current = Mathf.SmoothDamp(current, 1000f, ref velocity, 0.5f, 5f, dt);
                Assert.True((current - before) / dt <= 5f * 1.01f);
            }
        }

        [Fact]
        public void SmoothDampFirstStepMatchesFormula()
        {
            // omega = 2 / 0.5 = 4; x = 0.4; exp = 1 / (1 + 0.4 + 0.48*0.16 + 0.235*0.064)
            float velocity = 0f;
            float result = Mathf.SmoothDamp(0f, 1f, ref velocity, 0.5f, Mathf.Infinity, 0.1f);
            double exp = 1.0 / (1.0 + 0.4 + 0.48 * 0.16 + 0.235 * 0.064);
            double temp = (0 + 4.0 * -1.0) * 0.1;
            double expectedOut = 1.0 + (-1.0 + temp) * exp;
            Near.Equal((float)expectedOut, result, 1e-6f);
            Near.Equal((float)((0 - 4.0 * temp) * exp), velocity, 1e-5f);
        }

        [Fact]
        public void SmoothDampAngleTakesShortestWay()
        {
            float velocity = 0f;
            float result = Mathf.SmoothDampAngle(350f, 10f, ref velocity, 0.1f, Mathf.Infinity, 0.02f);
            Assert.True(result > 350f, $"expected to move up past 350, got {result}");
        }

        [Fact]
        public void PerlinNoiseRangeAndLattice()
        {
            Near.Equal(0.4652731f, Mathf.PerlinNoise(0f, 0f), 1e-6f);
            Near.Equal(0.4652731f, Mathf.PerlinNoise(3f, 7f), 1e-6f);
            float min = 1f, max = 0f;
            for (int i = 0; i < 4000; i++)
            {
                float v = Mathf.PerlinNoise(i * 0.137f, i * 0.071f + 0.5f);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            Assert.True(min > -0.15f && max < 1.15f, $"range [{min}, {max}]");
            Assert.True(max - min > 0.5f, $"range too small [{min}, {max}]");
            // Continuity.
            Assert.True(Math.Abs(Mathf.PerlinNoise(1.5f, 2.5f) - Mathf.PerlinNoise(1.5001f, 2.5f)) < 1e-3f);
            float n1 = Mathf.PerlinNoise1D(0.3f);
            Assert.InRange(n1, 0f, 1f);
        }

        [Fact]
        public void ColorTemperature()
        {
            Color warm = Mathf.CorrelatedColorTemperatureToRGB(1500f);
            Near.Equal(1f, warm.r);
            Assert.True(warm.b < 0.2f);
            Color daylight = Mathf.CorrelatedColorTemperatureToRGB(6500f);
            Assert.True(daylight.r > 0.9f && daylight.g > 0.9f && daylight.b > 0.9f, daylight.ToString());
            Color cold = Mathf.CorrelatedColorTemperatureToRGB(20000f);
            Near.Equal(1f, cold.b);
            Assert.True(cold.r < 0.8f);
        }

        [Fact]
        public void GammaFunction()
        {
            Near.Equal(-0.25f, Mathf.Gamma(-0.5f, 1f, 2f));
            Near.Equal(3f, Mathf.Gamma(3f, 1f, 2f));
        }
    }
}
