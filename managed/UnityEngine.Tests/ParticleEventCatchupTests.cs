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

        /// <summary>The direction of a Manual child turned against its parent, the parent moving
        /// along <paramref name="travel"/>.</summary>
        static Vector3 ManualChildVelocity(Vector3 travel, Vector3 childEuler)
        {
            var parent = Make("parent", null);
            var child = Make("child", parent.transform);
            child.transform.localRotation = Quaternion.Euler(childEuler);
            var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World;
            child.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
            parent.gameObject.SetActive(true);
            child.gameObject.SetActive(true);
            try
            {
                parent.Emit(new ParticleSystem.EmitParams { velocity = travel, startLifetime = 5f }, 1);
                parent.TriggerSubEmitter(0);
                var particles = new ParticleSystem.Particle[1];
                Assert.Equal(1, child.GetParticles(particles));
                return particles[0].velocity;
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Theory]
        // Unity 6000.0.71f1, tests/projects/subemitters direction/*: the frame looks along the
        // travel with its up between the child's forward and up axes, then the child's rotation
        // applies in it (two orthogonal child directions need not stay orthogonal).
        [InlineData(0f, 1f, 0f, 0f, 90f, 0f, 0f, 0f, 1f)]
        [InlineData(1f, 0f, 0f, 0f, 90f, 0f, 0f, 0f, -1f)]
        [InlineData(0f, 0f, 1f, 0f, 90f, 0f, 0f, -1f, 0f)]
        [InlineData(0f, 1f, 0f, -90f, 0f, 0f, 0f, 0f, -1f)]
        [InlineData(1f, 0f, 0f, -90f, 0f, 0f, 0f, 1f, 0f)]
        [InlineData(0.6f, 0.8f, 0f, -90f, 0f, 0f, -0.1187f, 0.089f, -0.9889f)]
        public void ARotatedChildTurnsWithinTheTravelFrame(float tx, float ty, float tz, float ex, float ey, float ez, float vx, float vy, float vz)
        {
            var v = ManualChildVelocity(new Vector3(tx, ty, tz) * 2f, new Vector3(ex, ey, ez));
            Assert.True((v - new Vector3(vx, vy, vz)).magnitude < 1e-3f, v.ToString("F4"));
        }

        [Fact]
        public void ACollisionChildStartsFromTheBouncedParentAtItsStepsEnd()
        {
            // A parent falling at 1 unit/s 0.0125 above a plane bounces mid-step (0.025 s steps)
            // and ends the step 0.0135 above it moving up; its Collision child starts there,
            // facing up (tests/projects/subemitters event/collision/0).
            var parent = Make("parent", null);
            m_Parent = parent.gameObject;
            var plane = new GameObject("plane").transform;
            plane.SetParent(parent.transform, false);
            var child = Make("child", parent.transform);
            var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startSpeed = 0f;
            child.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var c = parent.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
            c.bounce = 1f; c.dampen = 0f; c.lifetimeLoss = 0f;
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
            parent.gameObject.SetActive(true);
            child.gameObject.SetActive(true);
            parent.Emit(new ParticleSystem.EmitParams { position = new Vector3(0f, 0.0125f, 0f), velocity = Vector3.down, startLifetime = 5f, startSize = 0.001f }, 1);
            parent.Simulate(0.025f, true, false, false);
            var particles = new ParticleSystem.Particle[1];
            Assert.Equal(1, child.GetParticles(particles));
            Assert.Equal(0.0135f, particles[0].position.y, 4);
        }

        [Fact]
        public void APrewarmsCollisionChildrenTurnToTheBouncedTravel()
        {
            // A prewarmed parent turned to emit downwards bounces off a plane; its Collision
            // children (unrotated against it, so their own +Z points down too) leave along the
            // bounced travel, upwards, as in Simulate and played frames.
            var parent = Make("parent", null);
            m_Parent = parent.gameObject;
            parent.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var pm = parent.main; pm.prewarm = true; pm.duration = 1f; pm.startSpeed = 2f; pm.startSize = 0.01f;
            var pe = parent.emission; pe.rateOverTime = 10f;
            var plane = new GameObject("plane").transform;
            plane.SetParent(parent.transform, false);
            plane.position = new Vector3(0f, -0.5f, 0f);
            plane.rotation = Quaternion.identity;
            var c = parent.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
            c.bounce = 1f; c.dampen = 0f; c.lifetimeLoss = 0f;
            var child = Make("child", parent.transform);
            var cm = child.main; cm.simulationSpace = ParticleSystemSimulationSpace.World; cm.startLifetime = 5f;
            child.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
            parent.gameObject.SetActive(true);
            child.gameObject.SetActive(true);
            parent.Play();
            var particles = new ParticleSystem.Particle[64];
            int n = child.GetParticles(particles);
            Assert.True(n > 0);
            Assert.All(particles.Take(n), p => Assert.True(p.velocity.y > 0.9f, p.velocity.ToString("F3")));
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
