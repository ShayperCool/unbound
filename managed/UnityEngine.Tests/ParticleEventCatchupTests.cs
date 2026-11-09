using System;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A Death child a Simulate step emits has lived the call time left at the start of
    /// that step, in whole steps of Time.fixedDeltaTime (Unity 6000.0.71f1,
    /// tests/projects/subemitters timing/*).</summary>
    public class ParticleEventCatchupTests : IDisposable
    {
        readonly string m_Version = ProjectInfo.UnityVersion;
        readonly float m_Fixed = Time.fixedDeltaTime;
        readonly float m_MaxDelta = Time.maximumParticleDeltaTime;
        GameObject m_Parent;

        public ParticleEventCatchupTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Time.maximumParticleDeltaTime = 0.03f;
        }

        public void Dispose()
        {
            if (m_Parent != null) Object.DestroyImmediate(m_Parent);
            ProjectInfo.UnityVersion = m_Version;
            Time.fixedDeltaTime = m_Fixed;
            Time.maximumParticleDeltaTime = m_MaxDelta;
        }

        static ParticleSystem Make(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.duration = 5f; main.startLifetime = 2f; main.startSpeed = 1f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            return ps;
        }

        /// <summary>Four parents dying in the four 0.025 s steps of one 0.1 s Simulate call; the
        /// ages of their Death children afterwards, ascending.</summary>
        float[] ChildAges(float fixedDeltaTime)
        {
            Time.fixedDeltaTime = fixedDeltaTime;
            var parent = Make("parent", null);
            m_Parent = parent.gameObject;
            var child = Make("child", parent.transform);
            var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World;
            child.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            parent.gameObject.SetActive(true);
            child.gameObject.SetActive(true);
            for (int k = 0; k < 4; k++)
                parent.Emit(new ParticleSystem.EmitParams { startLifetime = 0.01f + 0.025f * k, velocity = Vector3.up }, 1);
            parent.Simulate(0.1f, true, false, false);
            var particles = new ParticleSystem.Particle[8];
            int n = child.GetParticles(particles);
            Object.DestroyImmediate(m_Parent);
            return particles.Take(n).Select(p => p.startLifetime - p.remainingLifetime).OrderBy(a => a).ToArray();
        }

        [Fact]
        public void DeathChildrenAgeInWholeFixedStepsFromTheirStepsStart()
        {
            Assert.Equal(new[] { 0.02f, 0.04f, 0.06f, 0.1f }, ChildAges(0.02f), new FloatNear(1e-4f));
            Assert.Equal(new[] { 0.02f, 0.05f, 0.07f, 0.1f }, ChildAges(0.01f), new FloatNear(1e-4f));
        }

        sealed class FloatNear : System.Collections.Generic.IEqualityComparer<float>
        {
            readonly float m_Tolerance;
            public FloatNear(float tolerance) => m_Tolerance = tolerance;
            public bool Equals(float a, float b) => Math.Abs(a - b) <= m_Tolerance;
            public int GetHashCode(float v) => 0;
        }
    }
}
