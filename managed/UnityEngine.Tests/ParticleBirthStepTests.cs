using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleBirthStepTests
    {
        // Unity 6000.0.71f1, tests/projects/particlerandom birth controls.
        [Theory]
        [InlineData(0.02f, 0.14f)]
        [InlineData(0.05f, 0.115f)]
        [InlineData(0.1f, 0.055f)]
        public void BirthStartsAtTheParentOriginAndFinishesItsOwnCallTime(float dt, float remaining)
        {
            WithSystems((parent, child) =>
            {
                parent.Emit(new ParticleSystem.EmitParams { position = new Vector3(1, 2, 3),
                    velocity = new Vector3(2, 3, 4) }, 1);
                Assert.Equal(0, child.particleCount);
                parent.Simulate(dt, true, false, false);
                var p = Read(child);
                Near(1f, p.position.x); Near(2f, p.position.y); Near(3f, p.position.z);
                Near(remaining, p.remainingLifetime);
                Assert.Equal(2449477499u, p.randomSeed);
                parent.Simulate(0.02f, true, false, false);
                Near(remaining - 0.02f, Read(child).remainingLifetime);
                parent.Simulate(0.2f, true, false, false);
                Assert.Equal(0, child.particleCount);
            });
        }

        [Fact]
        public void BirthGravityUsesTheNewbornStepThenFixedCatchupSteps()
        {
            WithSystems((parent, child) =>
            {
                var cm = child.main; cm.startSpeed = 2f; cm.gravityModifier = 1f;
                var e = parent.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                parent.Simulate(0.05f, false, true, false);
                var p = Read(child);
                Near(-0.02771324f, p.position.y); Near(0.13f, p.position.z);
                Near(-0.63765f, p.velocity.y); Near(0.115f, p.remainingLifetime);
                parent.Simulate(0.02f, false, true, false);
                Near(0.14f, Read(child).remainingLifetime);
            });
        }

        [Fact]
        public void RateBirthKeepsEachSeedWithItsAgeAcrossChildExpiry()
        {
            WithSystems((parent, child) =>
            {
                var e = parent.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                e = child.emission; e.SetBursts(Array.Empty<ParticleSystem.Burst>()); e.rateOverTime = 40f;
                foreach (float dt in new[] { 0.05f, 0.02f, 0.1f, 0.2f })
                    parent.Simulate(dt, true, dt == 0.05f, false);
                var particles = new ParticleSystem.Particle[16];
                Assert.Equal(6, child.GetParticles(particles));
                var bySeed = new Dictionary<uint, ParticleSystem.Particle>();
                for (int i = 0; i < 6; i++) bySeed.Add(particles[i].randomSeed, particles[i]);
                var seeds = new uint[] { 2572794258u, 477157185u, 3331272010u, 2395422893u, 1684420392u, 305042235u };
                for (int i = 0; i < seeds.Length; i++)
                {
                    Assert.True(bySeed.ContainsKey(seeds[i]));
                    Near(1.125f + 0.125f * i, bySeed[seeds[i]].position.z);
                }
                Near(0.0178572051f, bySeed[seeds[0]].remainingLifetime);
                Near(0.14f, bySeed[seeds[5]].remainingLifetime);
            });
        }

        [Theory]
        [InlineData(ParticleSystemRingBufferMode.PauseUntilReplaced)]
        [InlineData(ParticleSystemRingBufferMode.LoopUntilReplaced)]
        public void ExpiredBirthSkipDoesNotRemoveExistingRingParticles(ParticleSystemRingBufferMode mode)
        {
            WithSystems((parent, child) =>
            {
                var m = child.main; m.startLifetime = 0.05f; m.ringBufferMode = mode;
                var e = parent.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                parent.Simulate(0.1f, true, true, false);
                Assert.Equal(0, child.particleCount);
                child.Emit(1);
                parent.Simulate(0.2f, true, false, false);
                Assert.Equal(1, child.particleCount);
            });
        }

        [Fact]
        public void RateAndBurstBirthsShareStartSamplesAndKeepDistinctOriginsAndAges()
        {
            WithSystems((parent, child) =>
            {
                var e = parent.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                e = child.emission; e.rateOverTime = 40f;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1), new ParticleSystem.Burst(0.04f, (short)1) });
                parent.Simulate(0.05f, true, true, false);
                var particles = new ParticleSystem.Particle[4];
                Assert.Equal(4, child.GetParticles(particles));
                var bySeed = new Dictionary<uint, ParticleSystem.Particle>();
                foreach (var p in particles) bySeed.Add(p.randomSeed, p);
                var seeds = new uint[] { 2449477499u, 2360342416u, 896934279u, 326708070u };
                var positions = new float[] { 0.125f, 0f, 0.25f, 0.2f };
                var lifetimes = new float[] { 0.14f, 0.115f, 0.16f, 0.15f };
                for (int i = 0; i < seeds.Length; i++)
                {
                    Assert.True(bySeed.ContainsKey(seeds[i]));
                    Near(positions[i], bySeed[seeds[i]].position.z);
                    Near(lifetimes[i], bySeed[seeds[i]].remainingLifetime);
                }
            });
        }

        static ParticleSystem.Particle Read(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[1];
            Assert.Equal(1, ps.GetParticles(particles));
            return particles[0];
        }

        [Fact]
        public void InfiniteParentLifetimeKeepsItsExistingRateClock()
        {
            WithSystems((parent, child) =>
            {
                // startLifetime explicitly supports PositiveInfinity. Subtracting
                // its exported lifetime from itself must not poison the rate carry.
                var main = parent.main; main.startLifetime = float.PositiveInfinity;
                var emission = parent.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                emission = child.emission; emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                emission.rateOverTime = 10f;
                parent.Simulate(0.15f, true, true, false);
                Assert.True(child.particleCount > 0);
                var particles = new ParticleSystem.Particle[16];
                int count = child.GetParticles(particles);
                for (int i = 0; i < count; i++)
                {
                    Assert.True(float.IsFinite(particles[i].remainingLifetime));
                    Assert.True(float.IsFinite(particles[i].position.z));
                }
            });
        }

        // Independent constant-lifetime captures in particlerandom birthlife/zero and long.
        [Theory]
        [InlineData(0.413f, 0.813f, 0.315444f)]
        [InlineData(1.613f, 0.413f, 0.655844f)]
        public void BirthLifetimeUsesTheParentsRemainingSecondsAtItsIntegrationStep(
            float parentLife, float childLife, float inheritedLife)
        {
            WithSystems((parent, child) =>
            {
                var m = parent.main; m.startLifetime = parentLife;
                m = child.main; m.startLifetime = childLife;
                var emission = parent.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var sub = parent.subEmitters;
                sub.SetSubEmitterProperties(0, ParticleSystemSubEmitterProperties.InheritLifetime);
                parent.Simulate(0.05f, true, true, false);
                var particle = Read(child);
                Near(inheritedLife, particle.startLifetime);
                Near(inheritedLife - 0.065f, particle.remainingLifetime);
                Assert.Equal(2449477499u, particle.randomSeed);
                // Assignment stays fixed while the parent continues ageing.
                parent.Simulate(0.05f, true, false, false);
                Near(inheritedLife, Read(child).startLifetime);
                Near(inheritedLife - 0.115f, Read(child).remainingLifetime);
            });
        }

        [Theory]
        [InlineData(0.413f, 10f, 2)]
        [InlineData(1.613f, 10f, 1)]
        [InlineData(0.613f, 10f, 2)]
        [InlineData(0.613f, 120f, 13)]
        public void BirthRateBoundariesKeepTheMeasuredCountsWithDifferentParentLifetimes(
            float parentLife, float rate, int expectedCount)
        {
            WithSystems((parent, child) =>
            {
                var m = parent.main; m.startLifetime = parentLife;
                m = child.main; m.startLifetime = 0.813f;
                var emission = parent.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                emission = child.emission; emission.rateOverTime = rate;
                parent.Simulate(0.05f, true, true, false);
                parent.Simulate(0.05f, true, false, false);
                Assert.Equal(expectedCount, child.particleCount);
            });
        }

        static void Near(float expected, float actual) => Assert.InRange(Math.Abs(expected - actual), 0f, 0.000002f);

        static void WithSystems(Action<ParticleSystem, ParticleSystem> run)
        {
            string version = ProjectInfo.UnityVersion;
            float maximum = Time.maximumParticleDeltaTime, fixedDelta = Time.fixedDeltaTime;
            var gravity = Physics.gravity;
            var go = new GameObject("birth parent");
            go.SetActive(false);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.maximumParticleDeltaTime = 0.03f; Time.fixedDeltaTime = 0.02f;
                Physics.gravity = new Vector3(0f, -9.81f, 0f);
                var parent = go.AddComponent<ParticleSystem>();
                var cg = new GameObject("birth child"); cg.transform.SetParent(go.transform, false);
                var child = cg.AddComponent<ParticleSystem>();
                foreach (var ps in new[] { parent, child })
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var m = ps.main; m.playOnAwake = false; m.startLifetime = ps == parent ? 0.4f : 0.18f;
                    m.startSpeed = ps == parent ? 5f : 0f; m.gravityModifier = 0f;
                    var s = ps.shape; s.enabled = false;
                    var e = ps.emission; e.rateOverTime = 0f;
                    ps.randomSeed = ps == parent ? 12345u : 12346u;
                }
                child.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var sub = parent.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
                go.SetActive(true);
                run(parent, child);
            }
            finally
            {
                Object.DestroyImmediate(go);
                ProjectInfo.UnityVersion = version;
                Time.maximumParticleDeltaTime = maximum; Time.fixedDeltaTime = fixedDelta;
                Physics.gravity = gravity;
            }
        }
    }
}
