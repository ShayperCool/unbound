using System;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>One simulation model for deaths, seeds and lifetimes, as the 6000.0.71f1
    /// particlerandom probe measures it (model/ records): dead particles leave in four-slot groups,
    /// high to low; the module samples of a step do not depend on which particles die; event
    /// sub-emitter counts salt with the parent system's emission generator; automatic seeding
    /// reports and uses the playback's seed; every start lifetime takes the measured reciprocal;
    /// InheritLifetime multiplies by the parent's remaining seconds whatever its lifetime mode.
    /// These are measurements of that release, not documented guarantees.</summary>
    public sealed class ParticleSimulationModelTests : IDisposable
    {
        readonly string m_Version = ProjectInfo.UnityVersion;

        public ParticleSimulationModelTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose() => ProjectInfo.UnityVersion = m_Version;

        static ParticleSystem Quiet(uint seed, Action<ParticleSystem> configure = null)
        {
            return ParticleEventSubEmitterTests.Make(ps =>
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false; ps.randomSeed = seed;
                var m = ps.main; m.maxParticles = 1000; m.startSpeed = 0f;
                ps.emission.SetBursts(new ParticleSystem.Burst[0]);
                configure?.Invoke(ps);
            });
        }

        static ParticleSystem.Particle At(uint seed, float remaining, Vector3 velocity = default) => new ParticleSystem.Particle
        {
            position = Vector3.zero, velocity = velocity, startLifetime = 1f, remainingLifetime = remaining,
            startSize = 1f, startColor = new Color32(255, 255, 255, 255), randomSeed = seed,
        };

        static ParticleSystem.Particle[] ParticlesOf(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles;
        }

        /// <summary>Starts a playback, then replaces its particles with <paramref name="particles"/>.</summary>
        static void Seed(ParticleSystem ps, ParticleSystem.Particle[] particles)
        {
            ps.Simulate(0.01f, true, true, false);
            ps.SetParticles(particles, particles.Length);
        }

        [Fact]
        public void DeathsLeaveInFourSlotGroupsHighToLow()
        {
            var ps = Quiet(4242);
            try
            {
                // Slots 14, 16 and 18 die. Block 12 takes the last particle (23) into 14; block 16
                // removes 18 first (22 moves in), then 16 (21 moves in). A plain ascending sweep
                // would leave 22 in slot 16 and 21 in slot 18.
                var particles = Enumerable.Range(0, 24)
                    .Select(i => At(1000u + (uint)i, i == 14 || i == 16 || i == 18 ? 0.005f : 5f)).ToArray();
                Seed(ps, particles);
                ps.Simulate(0.02f, true, false, false);
                var seeds = ParticlesOf(ps).Select(p => (int)(p.randomSeed - 1000u)).ToArray();
                Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 23, 15, 21, 17, 22, 19, 20 }, seeds);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ASwappedInDeadParticleIsRemovedWhenItsGroupIsRevisited()
        {
            var ps = Quiet(4242);
            try
            {
                // Slot 1 takes the dead last particle (7), the group is revisited and slot 1
                // takes the next survivor (6).
                var particles = Enumerable.Range(0, 8)
                    .Select(i => At(1000u + (uint)i, i == 1 || i == 7 ? 0.005f : 5f)).ToArray();
                Seed(ps, particles);
                ps.Simulate(0.02f, true, false, false);
                var seeds = ParticlesOf(ps).Select(p => (int)(p.randomSeed - 1000u)).ToArray();
                Assert.Equal(new[] { 0, 6, 2, 3, 4, 5 }, seeds);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SurvivorsTakeTheSameModuleSamplesWhateverDies(bool speedModifier)
        {
            // Randomized force (persistent lanes, three draws per group) or the speed
            // modifier (the group's first seed): survivors' velocities by seed must not
            // change when other particles of the buffer die in the same step.
            Vector3[] Run(Func<int, float> remaining)
            {
                var ps = Quiet(987654321u, s =>
                {
                    if (speedModifier)
                    {
                        var v = s.velocityOverLifetime; v.enabled = true;
                        v.speedModifier = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                    }
                    else
                    {
                        var f = s.forceOverLifetime; f.enabled = true; f.randomized = true;
                        f.x = f.y = f.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
                    }
                });
                try
                {
                    var particles = Enumerable.Range(0, 13)
                        .Select(i => At(5000u + (uint)i * 7919u, remaining(i), new Vector3(1f, 2f, 3f))).ToArray();
                    Seed(ps, particles);
                    ps.Simulate(0.02f, true, false, false);
                    var result = new Vector3[13];
                    foreach (var p in ParticlesOf(ps)) result[(p.randomSeed - 5000u) / 7919u] = p.position;
                    return result;
                }
                finally { Object.DestroyImmediate(ps.gameObject); }
            }
            var all = Run(_ => 5f);
            var some = Run(i => i % 3 == 0 ? 0.005f : 5f);
            for (int i = 0; i < 13; i++)
                if (i % 3 != 0) Assert.Equal(all[i], some[i]);
        }

        static int DeathBurstCount(bool auto, uint seed, uint particleSeed, out uint reported)
        {
            var parent = Quiet(seed, ps =>
            {
                var m = ps.main; m.startLifetime = 1f;
            });
            parent.gameObject.SetActive(false);
            parent.useAutoRandomSeed = auto;
            var child = Quiet(99, c =>
            {
                var m = c.main; m.startLifetime = 10f;
                c.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(0f, 40f)) });
            });
            child.transform.SetParent(parent.transform, false);
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            parent.gameObject.SetActive(true);
            try
            {
                parent.Simulate(0.01f, true, true, false);
                reported = parent.randomSeed;
                parent.SetParticles(new[] { At(particleSeed, 0.005f) }, 1);
                parent.Simulate(0.02f, true, false, false);
                return child.particleCount;
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void DeathBurstCountsSaltWithTheEmissionGeneratorAfterAnEmittingStep()
        {
            // particlerandom model/autoseed/fixed: one emitting step has drawn once from the
            // generator of seed 12345, so the counts salt with 12345 × 1812433253 + 1.
            var expected = new[] { 3, 20, 29, 26, 23, 24, 38, 31 };
            for (int trial = 0; trial < expected.Length; trial++)
                Assert.Equal(expected[trial], DeathBurstCount(false, 12345u, 7919u * (uint)trial + 13u, out _));
        }

        [Fact]
        public void AutomaticSeedingReportsAndUsesThePlaybackSeed()
        {
            for (int trial = 0; trial < 8; trial++)
            {
                uint particleSeed = 7919u * (uint)trial + 13u;
                int count = DeathBurstCount(true, 12345u, particleSeed, out uint reported);
                Assert.NotEqual(12345u, reported);
                Assert.Equal(count, DeathBurstCount(false, reported, particleSeed, out _));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StartLifetimesTakeTheReciprocalEstimateWithOrWithoutSubEmitters(bool measured)
        {
            if (!measured) ProjectInfo.UnityVersion = "2022.3.0f1";
            var ps = Quiet(4242, s =>
            {
                var m = s.main; m.startLifetime = 0.613f;
                var e = s.emission; e.rateOverTime = 40f;
            });
            try
            {
                ps.Simulate(0.025f, true, true, false);
                var particles = ParticlesOf(ps);
                Assert.NotEmpty(particles);
                // particlerandom model/clock/: 0.613 exports as 0.613000035 in 6000.0.71f1; a
                // correctly rounded division gives 0.61299998.
                float expected = measured ? 0.613000035f : 1f / (1f / 0.613f);
                foreach (var p in particles) Assert.Equal(expected, p.startLifetime);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void InheritLifetimeUsesRemainingSecondsForANonConstantParentLifetime()
        {
            var parent = Quiet(12345, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.413f, 0.913f); m.startSpeed = 2f;
                ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            });
            parent.gameObject.SetActive(false);
            var child = Quiet(12346, c =>
            {
                var m = c.main; m.startLifetime = 0.813f;
                c.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            });
            child.transform.SetParent(parent.transform, false);
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritLifetime);
            parent.gameObject.SetActive(true);
            try
            {
                // One internal step (below maximumParticleDeltaTime): the child's multiplier is
                // the parent's remaining seconds after it, not the fraction of its life left.
                parent.Simulate(0.02f, true, true, false);
                var p = Assert.Single(ParticlesOf(parent));
                var c = Assert.Single(ParticlesOf(child));
                Assert.InRange(c.startLifetime, 0.813f * p.remainingLifetime - 1e-5f, 0.813f * p.remainingLifetime + 1e-5f);
                float fraction = 0.813f * p.remainingLifetime / p.startLifetime;
                Assert.True(Math.Abs(c.startLifetime - fraction) > 1e-3f);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Theory]
        // particlerandom model/lifetimesetter/: constant, range (each end on its own) and curve.
        [InlineData(ParticleSystemCurveMode.Constant, 0f, -1f, 0f, 0.0001f)]
        [InlineData(ParticleSystemCurveMode.Constant, 0f, 0.00005f, 0f, 0.0001f)]
        [InlineData(ParticleSystemCurveMode.TwoConstants, -0.5f, 1.5f, 0.0001f, 1.5f)]
        [InlineData(ParticleSystemCurveMode.TwoConstants, 1.5f, -0.5f, 1.5f, 0.0001f)]
        [InlineData(ParticleSystemCurveMode.TwoConstants, -2f, -1f, 0.0001f, 0.0001f)]
        [InlineData(ParticleSystemCurveMode.Curve, 0f, -1f, 0f, 0.0001f)]
        public void TheStartLifetimeSetterKeepsAtLeastATenthOfAMillisecond(ParticleSystemCurveMode mode, float min, float max,
            float keptMin, float keptMax)
        {
            var ps = Quiet(12345);
            try
            {
                var curve = mode == ParticleSystemCurveMode.Constant ? new ParticleSystem.MinMaxCurve(max)
                    : mode == ParticleSystemCurveMode.TwoConstants ? new ParticleSystem.MinMaxCurve(min, max)
                    : new ParticleSystem.MinMaxCurve(max, AnimationCurve.Linear(0f, -1f, 1f, 1f));
                var m = ps.main; m.startLifetime = curve;
                var kept = ps.main.startLifetime;
                Assert.Equal(mode, kept.mode);
                Assert.Equal(keptMin, kept.constantMin);
                Assert.Equal(keptMax, mode == ParticleSystemCurveMode.Curve ? kept.curveMultiplier : kept.constantMax);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void CompatFollowsAnotherReleaseString()
        {
            Assert.True(Compat.ParticleLifetimeReciprocalEstimate);
            ProjectInfo.UnityVersion = "2022.3.0f1";
            Assert.False(Compat.ParticleLifetimeReciprocalEstimate);
            Assert.Equal(2022, Compat.Major);
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Assert.True(Compat.ParticleLifetimeReciprocalEstimate);
            Assert.Equal((6000, 0, 71, 'f', 1), Compat.Release);
        }
    }
}
