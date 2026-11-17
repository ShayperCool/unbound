using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleRateBatchTests
    {
        /// <summary>A looping step longer than its loop emits its rate only for its advance within
        /// the loop (Unity 6000.0.71f1, tests/projects/particlerender prewarm/maxdelta/*): one
        /// 0.25 s step of a 0.1 s loop at 100 per second emits a 0.05 s window's births, five 0.05 s
        /// steps over the same time all 25 (the control); a step of two whole loops emits one loop;
        /// a non-looping system emits the same in either. Other releases keep emitting the whole
        /// step (their own path, unmeasured).</summary>
        [Fact]
        public void ALoopingStepLongerThanItsLoopEmitsForItsAdvanceWithinTheLoop()
        {
            Assert.InRange(Births(0.25f, true), 4, 5);
            Assert.Equal(25, Births(0.05f, true));
            Assert.InRange(Births(0.2f, true, 0.2f), 9, 10);
            Assert.Equal(Births(0.05f, false), Births(0.25f, false));
            Assert.InRange(Births(0.25f, true, release: "6000.0.0f1"), 24, 26);
        }

        /// <summary>A long step emits and carries the emission phase as its advance within the loop:
        /// steps just past one and three loops of a 1.5-birth loop go on as one that took only that
        /// advance (its particles' ages and the time; the emission phase changes no birth here, so
        /// it is not pinned by this test). Steps of whole loops follow the Editor's float ties
        /// instead (tests/projects/particlerender prewarm/maxdelta/0.2/*).</summary>
        [Theory]
        [InlineData(0.1f, 15f, 0.10001f, 0.00001f)]
        [InlineData(0.1f, 15f, 0.30001f, 0.00001f)]
        public void ALongStepGoesOnAsItsAdvanceWithinTheLoop(float duration, float rate, float longStep, float advance)
        {
            var (longAges, longTime) = Continue(duration, rate, longStep);
            var (shortAges, shortTime) = Continue(duration, rate, advance);
            Assert.Equal(shortAges, longAges);
            Assert.Equal(shortTime, longTime, 5);
        }

        /// <summary>One first step, then a hundred of 0.01 s: the ages (to 0.1 ms) of the particles
        /// born after the first step, and the final time.</summary>
        static (int[] ages, float time) Continue(float duration, float rate, float first)
        {
            string previousVersion = ProjectInfo.UnityVersion;
            float previousMax = Time.maximumParticleDeltaTime;
            var go = new GameObject("continue");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.maximumParticleDeltaTime = 0.5f; // each first call is one step
                var m = ps.main;
                m.playOnAwake = false; m.duration = duration; m.loop = true; m.startLifetime = 50f;
                var s = ps.shape; s.enabled = false;
                var e = ps.emission; e.rateOverTime = rate;
                go.SetActive(true);
                ps.Simulate(first, false, true, false);
                int start = ps.particleCount;
                for (int i = 0; i < 100; i++) ps.Simulate(0.01f, false, false, false);
                var particles = new ParticleSystem.Particle[ps.particleCount];
                ps.GetParticles(particles);
                var ages = new int[particles.Length - start];
                for (int i = 0; i < ages.Length; i++)
                {
                    var p = particles[start + i];
                    ages[i] = (int)Math.Round((p.startLifetime - p.remainingLifetime) * 10000f);
                }
                Array.Sort(ages);
                return (ages, ps.time);
            }
            finally
            {
                Object.DestroyImmediate(go);
                ProjectInfo.UnityVersion = previousVersion;
                Time.maximumParticleDeltaTime = previousMax;
            }
        }

        static int Births(float maxDelta, bool loop, float time = 0.25f, string release = "6000.0.71f1")
        {
            string previousVersion = ProjectInfo.UnityVersion;
            float previousMax = Time.maximumParticleDeltaTime;
            var go = new GameObject("long steps");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            try
            {
                ProjectInfo.UnityVersion = release;
                Time.maximumParticleDeltaTime = maxDelta;
                var m = ps.main;
                m.playOnAwake = false; m.duration = 0.1f; m.loop = loop; m.startLifetime = 5f;
                var s = ps.shape; s.enabled = false;
                var e = ps.emission; e.rateOverTime = 100f;
                go.SetActive(true);
                ps.Simulate(time, false, true, false);
                return ps.particleCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
                ProjectInfo.UnityVersion = previousVersion;
                Time.maximumParticleDeltaTime = previousMax;
            }
        }

        // Measured buffer contents and per-seed motion, Unity 6000.0.71f1,
        // tests/projects/particlerandom ratebatch/3/constant/plain, second step.
        [Fact]
        public void RateBatchKeepsEachSeedsStartValuesAndBirthAge()
        {
            string previousVersion = ProjectInfo.UnityVersion;
            float previousMax = Time.maximumParticleDeltaTime;
            var go = new GameObject("rate batch");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.maximumParticleDeltaTime = 1f;
                var m = ps.main;
                m.playOnAwake = false; m.duration = 0.05f; m.loop = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(1f, 3f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                ps.randomSeed = 12345u;
                var shape = ps.shape; shape.enabled = false;
                var e = ps.emission; e.rateOverTime = 162.5f;
                go.SetActive(true);
                ps.Simulate(0.02f, false, true, false);
                ps.Simulate(0.02f, false, false, false);
                var particles = new ParticleSystem.Particle[8];
                Assert.Equal(6, ps.GetParticles(particles));
                var seeds = new uint[] { 692788716u, 601952211u, 495557956u, 1487149351u, 1622567041u, 4101993118u };
                var lifetimes = new float[] { 2.79912114f, 1.01518655f, 2.68733f, 2.93243384f, 1.90199125f, 2.36804533f };
                var remaining = new float[] { 2.77758265f, 0.98749423f, 2.65348387f, 2.91704941f, 1.89891434f, 2.35881472f };
                var positions = new float[] { 0.0498579666f, 0.08349865f, 0.08757831f, 0.03639627f, 0.006133253f, 0.009344726f };
                for (int i = 0; i < 6; i++)
                {
                    Assert.Equal(seeds[i], particles[i].randomSeed);
                    Assert.InRange(Math.Abs(particles[i].startLifetime - lifetimes[i]), 0f, 0.000002f);
                    Assert.InRange(Math.Abs(particles[i].remainingLifetime - remaining[i]), 0f, 0.000002f);
                    Assert.InRange(Math.Abs(particles[i].position.z - positions[i]), 0f, 0.000002f);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                ProjectInfo.UnityVersion = previousVersion;
                Time.maximumParticleDeltaTime = previousMax;
            }
        }
    }
}
