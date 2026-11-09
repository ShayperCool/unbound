using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A system whose emitted particles all died is stopped, not reset: non-restarting
    /// Simulate calls keep its clock running, also through the call its last particle dies in
    /// (Unity 6000.0.71f1, tests/projects/subemitters death/*/parent).</summary>
    public class ParticleStoppedClockTests : IDisposable
    {
        readonly string m_Version = ProjectInfo.UnityVersion;
        GameObject m_Object;

        public ParticleStoppedClockTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose()
        {
            if (m_Object != null) Object.DestroyImmediate(m_Object);
            ProjectInfo.UnityVersion = m_Version;
        }

        ParticleSystem Make()
        {
            m_Object = new GameObject("particles");
            m_Object.SetActive(false);
            var ps = m_Object.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.duration = 5f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            m_Object.SetActive(true);
            return ps;
        }

        [Fact]
        public void TheClockOfASystemWhoseEmittedParticlesDiedKeepsRunning()
        {
            var ps = Make();
            ps.Emit(new ParticleSystem.EmitParams { startLifetime = 0.123f }, 1);
            for (int step = 1; step <= 6; step++)
            {
                ps.Simulate(0.05f, true, false, false);
                Assert.Equal(0.05f * step, ps.time, 4);
            }
            Assert.Equal(0, ps.particleCount);
            Assert.False(ps.isPlaying);
        }

        [Fact]
        public void ItsRunningClockStartsNoEmission()
        {
            // A rate it never played with stays unused: the clock runs, nothing is emitted.
            var ps = Make();
            var emission = ps.emission; emission.rateOverTime = 20f;
            ps.Emit(new ParticleSystem.EmitParams { startLifetime = 0.05f }, 1);
            for (int step = 0; step < 6; step++)
            {
                ps.Simulate(0.05f, true, false, false);
                if (step > 0) Assert.Equal(0, ps.particleCount);
            }
            Assert.Equal(0.3f, ps.time, 4);
        }
    }
}
