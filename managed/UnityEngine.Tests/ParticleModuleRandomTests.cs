using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleModuleRandomTests
    {
        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("module-random");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.randomSeed = 12345u;
            Assert.False(ps.useAutoRandomSeed);
            var main = ps.main;
            main.playOnAwake = false; main.duration = 1f; main.loop = true;
            main.startLifetime = 10f; main.startSpeed = 2f;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });
            configure(ps);
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.MinMaxCurve Two => new ParticleSystem.MinMaxCurve(0f, 1f);

        [Fact]
        public void VelocityUsesIndependentSequentialAxisDraws()
        {
            var ps = Make(s =>
            {
                var v = s.velocityOverLifetime; v.enabled = true; v.x = Two; v.y = Two; v.z = Two;
            });
            try
            {
                ps.Simulate(.05f, true, true, false);
                var particles = new ParticleSystem.Particle[6];
                Assert.Equal(6, ps.GetParticles(particles));
                // Unity 6000.0.71f1, particlerandom vol/two/12345/0.
                Assert.Equal(692788716u, particles[0].randomSeed);
                Assert.Equal(.367764145f, particles[0].animatedVelocity.x, 5);
                Assert.Equal(.245457083f, particles[0].animatedVelocity.y, 5);
                Assert.Equal(.159179226f, particles[0].animatedVelocity.z, 5);
                ps.Simulate(.05f, true, false, false);
                var again = new ParticleSystem.Particle[6];
                Assert.Equal(6, ps.GetParticles(again));
                for (int i = 0; i < 6; i++) Assert.Equal(particles[i].animatedVelocity, again[i].animatedVelocity);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void CustomComponentsUseSeparateSeedsInBothStreams()
        {
            var ps = Make(s =>
            {
                var c = s.customData; c.enabled = true;
                c.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                c.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 4);
                for (int k = 0; k < 4; k++) c.SetVector(ParticleSystemCustomData.Custom1, k, Two);
                c.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Color);
                c.SetColor(ParticleSystemCustomData.Custom2, new ParticleSystem.MinMaxGradient(Color.clear, Color.white));
            });
            try
            {
                ps.Simulate(.05f, true, true, false);
                var values = new List<Vector4>();
                Assert.Equal(6, ps.GetCustomParticleData(values, ParticleSystemCustomData.Custom1));
                Assert.Equal(.756026268f, values[0].x, 5);
                Assert.Equal(.420107782f, values[0].y, 5);
                Assert.Equal(.99173224f, values[0].z, 5);
                Assert.Equal(.0007387401f, values[0].w, 5);
                Assert.Equal(6, ps.GetCustomParticleData(values, ParticleSystemCustomData.Custom2));
                Assert.Equal(.07828546f, values[0].x, 5);
                Assert.Equal(values[0].x, values[0].w);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }


        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BurstStreamContinuesAcrossLoopBoundaries(bool probability)
        {
            var ps = Make(s =>
            {
                var e = s.emission;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, (short)(probability ? 1 : 6), 0, .1f)
                    { probability = probability ? .5f : 1f } });
            });
            try
            {
                // Unity 6000.0.71f1, particlerandom burst/{count,probability}/12345.
                int[] expected = probability
                    ? new[] { 1, 1, 2, 3, 3, 3, 4, 5, 6, 7, 8, 8 }
                    : new[] { 5, 6, 8, 14, 20, 20, 24, 26, 27, 31, 36, 37 };
                for (int i = 0; i < expected.Length; i++)
                {
                    ps.Simulate(.1f, true, i == 0, false);
                    Assert.Equal(expected[i], ps.particleCount);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void RestartPreparesChildrenBeforeTheirParentEmits()
        {
            var ps = Make(s => { var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) }); });
            try
            {
                ps.gameObject.SetActive(false);
                var go = new GameObject("child"); go.transform.SetParent(ps.transform, false);
                var child = go.AddComponent<ParticleSystem>();
                child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                child.randomSeed = 12346u;
                var m = child.main; m.playOnAwake = false; m.startLifetime = 10f; m.startSpeed = 0f;
                var shape = child.shape; shape.enabled = false;
                var e = child.emission; e.rateOverTime = 0f;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var sub = ps.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing, .5f);
                ps.gameObject.SetActive(true);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    ps.Simulate(.05f, true, true, false);
                    var particles = new ParticleSystem.Particle[12];
                    Assert.Equal(5, child.GetParticles(particles));
                    Assert.Equal(2449477499u, particles[0].randomSeed);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ImportedParticlesInitializeForceWithoutPlayOrEmit()
        {
            Action<ParticleSystem> configure = s =>
            {
                var f = s.forceOverLifetime; f.enabled = true; f.randomized = true;
                f.x = Two; f.y = Two; f.z = Two;
                var emission = s.emission; emission.enabled = false;
            };
            var dormant = Make(configure);
            var played = Make(configure);
            float delta = Time.s_DeltaTime;
            float unscaled = Time.s_UnscaledDeltaTime;
            try
            {
                Time.s_DeltaTime = .02f; Time.s_UnscaledDeltaTime = .02f;
                var imported = new[] { new ParticleSystem.Particle
                    { randomSeed = 7u, startLifetime = 10f, remainingLifetime = 10f, startSize = 1f } };
                dormant.SetParticles(imported); played.SetParticles(imported);
                played.Play(false);
                Assert.False(dormant.isPlaying);
                Assert.True(dormant.FrameUpdate()); Assert.True(played.FrameUpdate());
                var actual = new ParticleSystem.Particle[1];
                var expected = new ParticleSystem.Particle[1];
                Assert.Equal(1, dormant.GetParticles(actual)); Assert.Equal(1, played.GetParticles(expected));
                Assert.NotEqual(0f, expected[0].velocity.x);
                Assert.Equal(expected[0].velocity, actual[0].velocity);
            }
            finally
            {
                Time.s_DeltaTime = delta; Time.s_UnscaledDeltaTime = unscaled;
                Object.DestroyImmediate(dormant.gameObject); Object.DestroyImmediate(played.gameObject);
            }
        }

        [Fact]
        public void PlaybackStateRestoresRandomizedForceLanes()
        {
            var ps = Make(s =>
            {
                var f = s.forceOverLifetime; f.enabled = true; f.randomized = true;
                f.x = Two; f.y = Two; f.z = Two;
            });
            try
            {
                ps.Simulate(.05f, true, true, false);
                var state = ps.GetPlaybackState();
                var saved = new ParticleSystem.Particle[6];
                Assert.Equal(6, ps.GetParticles(saved));
                Assert.Equal(.025946154f, saved[0].velocity.x, 5);
                ps.Simulate(.05f, true, false, false);
                var expected = new ParticleSystem.Particle[6];
                Assert.Equal(6, ps.GetParticles(expected));
                ps.SetPlaybackState(state); ps.SetParticles(saved);
                ps.Simulate(.05f, true, false, false);
                var actual = new ParticleSystem.Particle[6];
                Assert.Equal(6, ps.GetParticles(actual));
                for (int i = 0; i < 6; i++) Assert.Equal(expected[i].velocity, actual[i].velocity);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
