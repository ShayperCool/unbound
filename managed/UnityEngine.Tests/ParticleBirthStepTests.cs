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

        // Raw accumulator and count observations from the two birthclock captures.
        // High rate controls distinguish reconstructing percent age from seconds age.
        [Theory]
        [InlineData(0.413f, 10f, 0.25f, 0.5f, 0.75f, 0f, 1)]
        [InlineData(0.613f, 10f, 0.25f, 0.5f, 0.75f, 0f, 1)]
        [InlineData(1.013f, 10f, 0.249999985f, 0.49999997f, 0.74999994f, 0.99999994f, 0)]
        [InlineData(1.613f, 10f, 0.249999985f, 0.49999997f, 0.75f, 0.9999999f, 0)]
        [InlineData(0.413f, 120f, 0f, 0f, 0.999999762f, 0.9999995f, 11)]
        [InlineData(0.613f, 120f, 0f, 0f, 0.000000238418579f, 0f, 12)]
        [InlineData(1.013f, 120f, 0.999999762f, 0.9999995f, 0.9999993f, 0.999999046f, 11)]
        [InlineData(1.613f, 120f, 0.999999762f, 0.9999995f, 0.999999762f, 0.9999988f, 11)]
        public void BirthClockRetainsMeasuredAccumulatorsAndBoundaryCounts(float lifetime, float rate,
            float first, float second, float third, float fourth, int count)
        {
            WithSystems((parent, child) =>
            {
                var main = parent.main; main.startLifetime = lifetime;
                main = child.main; main.startLifetime = 10f;
                var emission = parent.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                emission = child.emission; emission.rateOverTime = rate;
                emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                var accumulator = typeof(ParticleSystem.Particle).GetField("m_EmitAccumulator0",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                int step = 0;
                foreach (float expected in new[] { first, second, third, fourth })
                {
                    parent.Simulate(0.025f, true, step++ == 0, false);
                    Assert.Equal(expected, (float)accumulator.GetValue(Read(parent)));
                }
                Assert.Equal(count, child.particleCount);
                if (lifetime == 0.613f) Assert.Equal(0.613000035f, Read(parent).startLifetime);
            });
        }

        [Fact]
        public void StopAfterTheLastParentDiesDoesNotRestartBirthEmission()
        {
            WithSystems((parent, child) =>
            {
                var main = parent.main; main.startLifetime = 0.413f; main.loop = false; main.duration = 0.05f;
                var emission = parent.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                parent.Simulate(1f, true, true, false);
                Assert.Equal(0, parent.particleCount);
                Assert.False(parent.isPlaying);
                Assert.True(parent.isPaused);
                parent.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                foreach (float dt in new[] { 0.05f, 0.2f, 0.5f, 1f, 2f })
                {
                    parent.Simulate(dt, true, false, false);
                    Assert.Equal(0, parent.particleCount);
                    Assert.Equal(0, child.particleCount);
                }
            });
        }

        // Unity 6000.0.71f1, tests/projects/subemitters birth/distance/*.
        [Fact]
        public void BirthRateOverDistanceEmitsAlongParentsPathAndSharesAccumulatorWithRateOverTime()
        {
            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = 10f; ce.rateOverTime = 0f;
                parent.transform.position = new Vector3(1f, 2f, 3f);
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                parent.Simulate(0.05f, true, false, false);
                Assert.Equal(0, child.particleCount);
                parent.Simulate(0.05f, true, false, false);
                var p = Read(child);
                Near(2.165f, p.position.y);
                Near(1.948f, p.remainingLifetime);
                for (int s = 2; s <= 6; s++) parent.Simulate(0.05f, true, false, false);
                Assert.Equal(7, child.particleCount);
                var all = new ParticleSystem.Particle[16];
                int n = child.GetParticles(all);
                Array.Sort(all, 0, n, Comparer<ParticleSystem.Particle>.Create((a, b) => a.position.y.CompareTo(b.position.y)));
                Near(2.72f, all[n - 1].position.y);
                Near(1.993f, all[n - 1].remainingLifetime);
            });
        }

        [Fact]
        public void BirthRateOverDistanceAndTimeShareAccumulatorAndMultiSubstepSpacing()
        {
            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = 25f; ce.rateOverTime = 45f;
                parent.transform.position = new Vector3(1f, 2f, 3f);
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                parent.Simulate(0.05f, true, false, false);
                var all = new ParticleSystem.Particle[8];
                Assert.Equal(4, child.GetParticles(all));
                Array.Sort(all, 0, 4, Comparer<ParticleSystem.Particle>.Create((a, b) => a.position.y.CompareTo(b.position.y)));
                float[] expectedY = { 2.081667f, 2.085f, 2.103333f, 2.1075f };
                float[] expectedRem = { 1.964667f, 1.968f, 1.976333f, 1.9805f };
                for (int i = 0; i < 4; i++)
                {
                    Assert.InRange(Math.Abs(expectedY[i] - all[i].position.y), 0f, 0.0001f);
                    Assert.InRange(Math.Abs(expectedRem[i] - all[i].remainingLifetime), 0f, 0.0001f);
                }
            });
        }

        [Fact]
        public void BirthRateOverDistanceUsesWorldSpeedRespectsDurationAndIgnoresDisabledModule()
        {
            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f; pm.scalingMode = ParticleSystemScalingMode.Local;
                parent.transform.position = new Vector3(1f, 2f, 3f);
                parent.transform.localScale = new Vector3(1f, 2f, 1f);
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                cm.loop = false; cm.duration = 0.25f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = 10f; ce.rateOverTime = 0f; ce.enabled = false;
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 1f, 0f), startLifetime = 5f }, 1);
                for (int s = 0; s < 7; s++) parent.Simulate(0.05f, true, false, false);
                // World speed 2 (local 1 * scale 2) emits four children before non-looping 0.25 s duration ends exclusively.
                Assert.Equal(4, child.particleCount);
            });
        }

        [Fact]
        public void BirthRateOverDistanceEmitsNothingForStillParent()
        {
            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = 10f; ce.rateOverTime = 0f;
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = Vector3.zero, startLifetime = 5f }, 1);
                for (int s = 0; s < 6; s++) parent.Simulate(0.05f, true, false, false);
                Assert.Equal(0, child.particleCount);
            });
        }

        [Fact]
        public void BirthRateOverDistanceEmitsInPlayedFramesWithoutCatchup()
        {
            float delta = Time.s_DeltaTime, unscaled = Time.s_UnscaledDeltaTime;
            bool inFixed = Time.s_InFixedTimeStep;
            int frame = Time.s_FrameCount;
            try
            {
                WithSystems((parent, child) =>
                {
                    Time.s_DeltaTime = 0.05f; Time.s_UnscaledDeltaTime = 0.05f; Time.s_InFixedTimeStep = false;
                    var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f; pm.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                    var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    ce.rateOverDistance = 10f; ce.rateOverTime = 0f;
                    parent.transform.position = new Vector3(1f, 2f, 3f);
                    parent.Play();
                    parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                    for (int f = 0; f < 2; f++)
                    {
                        Unbound.Runtime.ParticleSystemManager.UpdateAll();
                        Unbound.Runtime.ParticleSystemManager.CompleteUpdate();
                        Unbound.Runtime.ParticleSystemManager.EndUpdateAll();
                        Time.s_FrameCount++;
                    }
                    var p = Read(child);
                    Near(2.125f, p.position.y);
                    Near(1.988f, p.remainingLifetime);
                });
            }
            finally
            {
                Unbound.Runtime.ParticleSystemManager.EndUpdateAll();
                Time.s_DeltaTime = delta; Time.s_UnscaledDeltaTime = unscaled;
                Time.s_InFixedTimeStep = inFixed; Time.s_FrameCount = frame;
            }
        }

        [Fact]
        public void BirthRateOverDistanceMultiChildSingleSubstepSpacingAndVelocityOverLifetime()
        {
            WithSystems((parent, child) =>
            {
                // Multi-child single substep: rate 45/unit * speed 3 * dt 0.025 = 3.375 -> 3 children in one substep.
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = 45f; ce.rateOverTime = 0f;
                parent.transform.position = new Vector3(1f, 2f, 3f);
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 3f, 0f), startLifetime = 5f }, 1);
                parent.Simulate(0.025f, true, false, false);
                var all = new ParticleSystem.Particle[8];
                Assert.Equal(3, child.GetParticles(all));
                Array.Sort(all, 0, 3, Comparer<ParticleSystem.Particle>.Create((a, b) => a.remainingLifetime.CompareTo(b.remainingLifetime)));
                // Uniform spacing dt / toEmit = 1 / (45 * 3) = 1 / 135 s between siblings in the same substep.
                float expectedSpacing = 1f / (45f * 3f);
                float s01 = all[1].remainingLifetime - all[0].remainingLifetime;
                float s12 = all[2].remainingLifetime - all[1].remainingLifetime;
                Assert.InRange(Math.Abs(s01 - expectedSpacing), 0f, 0.0001f);
                Assert.InRange(Math.Abs(s12 - expectedSpacing), 0f, 0.0001f);
            });

            // VelocityOverLifetime.speedModifier = 0 still emits from simVelocity and interpolates position by simVelocity.
            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var vol = parent.velocityOverLifetime; vol.enabled = true; vol.speedModifier = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = 10f; ce.rateOverTime = 0f;
                parent.transform.position = new Vector3(1f, 2f, 3f);
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                parent.Simulate(0.05f, true, false, false);
                Assert.Equal(0, child.particleCount);
                parent.Simulate(0.05f, true, false, false);
                var p = Read(child);
                Near(2.015f, p.position.y);
                Near(1.948f, p.remainingLifetime);
            });
        }

        [Fact]
        public void BirthRateOverDistanceCurveModeTinyThresholdAndUnmeasuredRelease()
        {
            // Looping child evaluates rateOverDistance curve at t = 0 (Linear(0,0,1,1) -> 0); non-looping at to / duration.
            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                cm.loop = true;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = new ParticleSystem.MinMaxCurve(10f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                ce.rateOverTime = 0f;
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                for (int s = 0; s < 10; s++) parent.Simulate(0.05f, true, false, false);
                Assert.Equal(0, child.particleCount);
            });

            WithSystems((parent, child) =>
            {
                var pm = parent.main; pm.startLifetime = 5f; pm.startSpeed = 0f;
                var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                cm.loop = false; cm.duration = 0.5f;
                var ce = child.emission; ce.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ce.rateOverDistance = new ParticleSystem.MinMaxCurve(20f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                ce.rateOverTime = 0f;
                parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                for (int s = 0; s < 10; s++) parent.Simulate(0.05f, true, false, false);
                Assert.True(child.particleCount > 0);
            });

            // Unmeasured release still runs rateOverDistance cleanly (no release-pinned crash or drop).
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var go = new GameObject("unmeasured birth parent");
                go.SetActive(false);
                try
                {
                    var parent = go.AddComponent<ParticleSystem>();
                    var cg = new GameObject("unmeasured birth child"); cg.transform.SetParent(go.transform, false);
                    var child = cg.AddComponent<ParticleSystem>();
                    foreach (var ps in new[] { parent, child })
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var m = ps.main; m.playOnAwake = false; m.startLifetime = 5f; m.startSpeed = 0f; m.gravityModifier = 0f;
                        var s = ps.shape; s.enabled = false;
                        var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    }
                    var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 2.013f; cm.startSpeed = 1f;
                    var ce = child.emission; ce.rateOverDistance = 10f;
                    var sub = parent.subEmitters; sub.enabled = true;
                    sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
                    go.SetActive(true);
                    parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = new Vector3(0f, 2f, 0f), startLifetime = 5f }, 1);
                    for (int s = 0; s < 6; s++) parent.Simulate(0.05f, true, false, false);
                    Assert.True(child.particleCount >= 5);
                }
                finally { Object.DestroyImmediate(go); }
            }
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
