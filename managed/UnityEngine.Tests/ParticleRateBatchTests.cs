using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleRateBatchTests
    {
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
