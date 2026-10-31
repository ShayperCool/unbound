using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleLifetimeTests
    {
        static ParticleSystem Make()
        {
            var go = new GameObject("lifetime"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.startLifetime = 10; main.startSpeed = 0;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle Particle(int id, float life, float start = 10) => new ParticleSystem.Particle {
            randomSeed = (uint)id, position = new Vector3(id, id + 1, -id),
            startLifetime = start, remainingLifetime = life
        };

        public static IEnumerable<object[]> Lifetimes()
        {
            foreach (float start in new[] { .001f, 1f, 10f, 1000000f })
            foreach (float life in new[] { -1f, -.01f, -1e-7f, -float.Epsilon, -0f, 0f, float.Epsilon, 1e-7f, .01f, 1f })
                yield return new object[] { start, life, BitConverter.SingleToInt32Bits(life) };
        }

        [Theory]
        [MemberData(nameof(Lifetimes))]
        public void InputLifetimeDeterminesImmediateRemovalBeforeAgeEncoding(float start, float life, int bits)
        {
            var ps = Make();
            try
            {
                var input = new[] { Particle(321, life, start) };
                Assert.Equal(life, input[0].remainingLifetime);
                Assert.Equal(bits, BitConverter.SingleToInt32Bits(input[0].remainingLifetime));
                ps.SetParticles(input);
                Assert.Equal(life, input[0].remainingLifetime);
                Assert.Equal(life > 0 ? 1 : 0, ps.particleCount);
                var back = new ParticleSystem.Particle[1];
                Assert.Equal(ps.particleCount, ps.GetParticles(back));
                if (ps.particleCount != 0)
                {
                    Assert.Equal(321u, back[0].randomSeed);
                    Assert.InRange(back[0].remainingLifetime, 0, start);
                    ps.SetParticles(back);
                    Assert.Equal(back[0].remainingLifetime > 0 ? 1 : 0, ps.particleCount);
                }
                Assert.True(ps.isStopped);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // Buffer order measured independently in Native, including a zero at input index 1.
        static readonly int[][][] Expected = {
            new[] { new[] { 200, 205, 202, 203, 204 }, new[] { 204, 205, 202, 203 }, new[] { 200, 204, 205, 203 }, new[] { 200, 204, 202, 203 }, new[] { 203, 204 } },
            new[] { new[] { 200, 203, 202 }, new[] { 202, 203 }, new[] { 200, 203 }, new[] { 200, 202 }, Array.Empty<int>() },
            new[] { new[] { 100, 200, 203, 202 }, new[] { 100, 202, 203 }, new[] { 100, 200, 203 }, new[] { 100, 200, 202 }, new[] { 100 } }
        };

        [Fact]
        public void OffsetWriteRetainsEncodedZeroInUntouchedPrefixUntilRoundtrip()
        {
            var ps = Make();
            try
            {
                ps.SetParticles(new[] { Particle(400, float.Epsilon), Particle(401, 5), Particle(402, 5) });
                ps.SetParticles(new[] { Particle(403, 5) }, 1, 2);
                var back = new ParticleSystem.Particle[3];
                Assert.Equal(3, ps.GetParticles(back));
                Assert.Equal(new uint[] { 400, 401, 403 }, back.Select(p => p.randomSeed));
                Assert.Equal(0f, back[0].remainingLifetime);
                ps.SetParticles(Array.Empty<ParticleSystem.Particle>(), 0, 1);
                Assert.Equal(1, ps.GetParticles(back));
                Assert.Equal(400u, back[0].randomSeed);
                ps.SetParticles(back, 1);
                Assert.Equal(0, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        public static IEnumerable<object[]> Compactions()
        {
            for (int overload = 0; overload < 6; overload++)
            for (int state = 0; state < 3; state++)
            for (int mask = 0; mask < 5; mask++) yield return new object[] { overload, state, mask };
        }

        [Theory]
        [InlineData(1, new uint[] { 500, 505, 501, 502, 504 })]
        [InlineData(2, new uint[] { 500, 501, 505, 501, 502, 504 })]
        [InlineData(3, new uint[] { 500, 501, 502, 505, 501, 502, 504 })]
        public void OffsetCompactionUsesGroupsAlignedToTheFullBuffer(int offset, uint[] expected)
        {
            var ps = Make();
            try
            {
                ps.SetParticles(Enumerable.Range(0, offset+7).Select(i => Particle(500+i, 5)).ToArray());
                var input = Enumerable.Range(0, 6).Select(i => Particle(500+i, i == 0 || i == 3 ? -.01f : 5)).ToArray();
                ps.SetParticles(input, 6, offset);
                var back = new ParticleSystem.Particle[expected.Length];
                Assert.Equal(expected.Length, ps.GetParticles(back));
                Assert.Equal(expected, back.Select(p => p.randomSeed));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [MemberData(nameof(Compactions))]
        public void RemovalKeepsMeasuredBufferOrderCustomDataAndPlaybackState(int overload, int state, int mask)
        {
            var ps = Make();
            try
            {
                ps.SetParticles(Enumerable.Range(100, 6).Select(id => Particle(id, 8)).ToArray());
                foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                    ps.SetCustomParticleData(Enumerable.Range(0, 6).Select(i => new Vector4(100+i, (int)stream+1, i, -i)).ToList(), stream);
                if (state != 0) ps.Play(false);
                if (state == 2) ps.Pause(false);
                var flags = (ps.isPlaying, ps.isEmitting, ps.isStopped, ps.isPaused, ps.time);
                int last = overload % 3 == 0 ? 5 : 3;
                var input = Enumerable.Range(0, 6).Select(i => Particle(200+i,
                    mask == 1 && i == 0 || mask == 2 && i == 2 || mask == 3 && i == last || mask == 4 && (i == 0 || i == 2 || i == last)
                        ? -.01f : i == 1 ? 0 : 5)).ToArray();
                if (overload == 0) ps.SetParticles(input);
                if (overload == 1) ps.SetParticles(input, 4);
                if (overload == 2) ps.SetParticles(input, 4, 1);
                if (overload >= 3) using (var a = new NativeArray<ParticleSystem.Particle>(input, Allocator.Temp))
                {
                    if (overload == 3) ps.SetParticles(a);
                    if (overload == 4) ps.SetParticles(a, 4);
                    if (overload == 5) ps.SetParticles(a, 4, 1);
                }
                var expected = Expected[overload % 3][mask];
                Assert.Equal(expected.Length, ps.particleCount);
                var back = new ParticleSystem.Particle[expected.Length];
                Assert.Equal(expected.Length, ps.GetParticles(back));
                Assert.Equal(expected.Select(x => (uint)x), back.Select(p => p.randomSeed));
                for (int i = 0; i < back.Length; i++) Assert.Equal(new Vector3(expected[i], expected[i]+1, -expected[i]), back[i].position);
                foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                {
                    var custom = new List<Vector4>(); Assert.Equal(expected.Length, ps.GetCustomParticleData(custom, stream));
                    for (int i = 0; i < expected.Length; i++)
                    {
                        int slot = expected[i] < 200 ? expected[i]-100 : expected[i]-200+(overload % 3 == 2 ? 1 : 0);
                        Assert.Equal(new Vector4(100+slot, (int)stream+1, slot, -slot), custom[i]);
                    }
                }
                Assert.Equal(flags, (ps.isPlaying, ps.isEmitting, ps.isStopped, ps.isPaused, ps.time));
                ps.SetParticles(back);
                Assert.Equal(expected.Length, ps.GetParticles(back));
                Assert.Equal(expected.Select(x => (uint)x), back.Select(p => p.randomSeed));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
