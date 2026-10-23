using System.Collections.Generic;
using UnityEngine;
using Xunit;
using URandom = UnityEngine.Random;

namespace Unbound.Tests
{
    // Random is global state; keep its tests in one non-parallel collection.
    [Collection("UnityEngine.Random")]
    public class RandomTests
    {
        [Fact]
        public void SameSeedSameSequence()
        {
            URandom.InitState(42);
            var first = new List<float>();
            for (int i = 0; i < 16; i++)
                first.Add(URandom.value);
            URandom.InitState(42);
            for (int i = 0; i < 16; i++)
                Assert.Equal(first[i], URandom.value);

            URandom.State saved = URandom.state;
            int a = URandom.Range(0, 1000);
            float b = URandom.Range(-5f, 5f);
            URandom.state = saved;
            Assert.Equal(a, URandom.Range(0, 1000));
            Assert.Equal(b, URandom.Range(-5f, 5f));
        }

        [Fact]
        public void IntRangeExcludesMax()
        {
            URandom.InitState(7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 2000; i++)
            {
                int v = URandom.Range(0, 10);
                Assert.InRange(v, 0, 9);
                seen.Add(v);
            }
            Assert.Equal(10, seen.Count);
            Assert.Equal(5, URandom.Range(5, 5));
            for (int i = 0; i < 200; i++)
                Assert.InRange(URandom.Range(10, 0), 1, 10); // swapped: (max, min]
            for (int i = 0; i < 200; i++)
                Assert.InRange(URandom.Range(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
        }

        [Fact]
        public void FloatRangeIsInclusive()
        {
            URandom.InitState(3);
            for (int i = 0; i < 2000; i++)
            {
                Assert.InRange(URandom.Range(2f, 3f), 2f, 3f);
                Assert.InRange(URandom.value, 0f, 1f);
            }
            Assert.Equal(1.5f, URandom.Range(1.5f, 1.5f));
        }

        [Fact]
        public void GeometricSamples()
        {
            URandom.InitState(11);
            for (int i = 0; i < 500; i++)
            {
                Assert.True(URandom.insideUnitSphere.sqrMagnitude <= 1.0001f);
                Near.Equal(1f, URandom.onUnitSphere.magnitude, 1e-5f);
                Assert.True(URandom.insideUnitCircle.sqrMagnitude <= 1.0001f);
                Quaternion q = URandom.rotation;
                Near.Equal(1f, Mathf.Sqrt(Quaternion.Dot(q, q)), 1e-5f);
                Quaternion u = URandom.rotationUniform;
                Near.Equal(1f, Mathf.Sqrt(Quaternion.Dot(u, u)), 1e-5f);
            }
        }

        [Fact]
        public void ColorHsvRanges()
        {
            URandom.InitState(5);
            for (int i = 0; i < 200; i++)
            {
                Color c = URandom.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 0.5f, 0.25f, 0.25f);
                Near.Equal(0.5f, c.maxColorComponent, 1e-5f);
                Near.Equal(0.25f, c.a);
            }
            Color white = URandom.ColorHSV(0f, 1f, 0f, 0f, 1f, 1f);
            Near.Equal(Color.white, white);
        }

        [Fact]
        public void XorShiftStateExpansion()
        {
            // InitState expands the seed with x(n+1) = x(n) * 1812433253 + 1.
            URandom.InitState(1);
            URandom.State s = URandom.state;
            URandom.InitState(1);
            uint s0 = 1u, s1 = s0 * 1812433253u + 1u, s2 = s1 * 1812433253u + 1u, s3 = s2 * 1812433253u + 1u;
            uint t = s0 ^ (s0 << 11);
            uint next = s3 ^ (s3 >> 19) ^ t ^ (t >> 8);
            int expected = (int)(next % 1000u);
            Assert.Equal(expected, URandom.Range(0, 1000));
            URandom.state = s;
            Assert.Equal(expected, URandom.Range(0, 1000));
        }
    }
}
