using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Death and Manual (TriggerSubEmitter) sub-emitters as the 6000.0.71f1 subemitters
    /// probe measures them (tests/projects/subemitters): the first burst in the list, its count once;
    /// its probability; a disabled module or an index past the last slot emits nothing, silently; a
    /// child in two Death slots emits once; InheritLifetime multiplies by the parent's remaining
    /// seconds; the child's frame follows the parent's direction of travel. The 6000.0 Manual (Sub
    /// Emitters module) documents only that these conditions "support only a single burst"; the
    /// rest are measurements of that release, not documented guarantees.</summary>
    public class ParticleEventSubEmitterTests
    {
        internal static ParticleSystem Make(Action<ParticleSystem> configure, Transform parent = null)
        {
            var go = new GameObject("particles");
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.startLifetime = 2.013f; main.startSpeed = 1f;
            main.gravityModifier = 0f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            configure?.Invoke(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A non-emitting parent with a world-space child (under it) emitting
        /// <paramref name="bursts"/> as its only sub-emitter.</summary>
        internal static ParticleSystem Linked(ParticleSystemSubEmitterType type, ParticleSystem.Burst[] bursts, out ParticleSystem child,
            ParticleSystemSubEmitterProperties properties = ParticleSystemSubEmitterProperties.InheritNothing)
        {
            var parent = Make(null);
            child = Make(c =>
            {
                var m = c.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
                c.emission.SetBursts(bursts);
            }, parent.transform);
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, type, properties);
            return parent;
        }

        internal static ParticleSystem.Particle Particle(float lifetime, Vector3 velocity = default) => new ParticleSystem.Particle
        {
            position = Vector3.zero, velocity = velocity, startLifetime = lifetime, remainingLifetime = lifetime,
            startSize = 1f, startColor = new Color32(255, 255, 255, 255), randomSeed = 12345,
        };

        static ParticleSystem.Particle[] ParticlesOf(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles;
        }

        static void Near(Vector3 expected, Vector3 actual) =>
            Assert.True((expected - actual).magnitude <= 1e-4f, $"expected {expected}, got {actual}");

        static int TriggerOnce(ParticleSystem.Burst[] bursts)
        {
            var parent = Linked(ParticleSystemSubEmitterType.Manual, bursts, out var child);
            try
            {
                var particle = Particle(5f);
                parent.TriggerSubEmitter(0, ref particle);
                return child.particleCount;
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void TheFirstBurstEmitsItsCountOnceWhateverItsCyclesAndTime()
        {
            Assert.Equal(2, TriggerOnce(new[] { new ParticleSystem.Burst(0f, 2, 3, 0.1f) }));
            Assert.Equal(2, TriggerOnce(new[] { new ParticleSystem.Burst(0f, 2, 0, 0.1f) }));
            Assert.Equal(2, TriggerOnce(new[] { new ParticleSystem.Burst(0.3f, 2), new ParticleSystem.Burst(0f, 4) }));
        }

        [Fact]
        public void TheFirstBurstsProbabilityApplies()
        {
            var never = new ParticleSystem.Burst(0f, 3) { probability = 0f };
            Assert.Equal(0, TriggerOnce(new[] { never }));
        }

        [Fact]
        public void ADisabledModuleOrAMissingSlotEmitsNothingSilently()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Manual, new[] { new ParticleSystem.Burst(0f, 2) }, out var child);
            try
            {
                var particle = Particle(5f);
                parent.TriggerSubEmitter(3, ref particle);
                parent.TriggerSubEmitter(-1);
                parent.TriggerSubEmitter(1, new List<ParticleSystem.Particle> { particle });
                Assert.Equal(0, child.particleCount);
                var subs = parent.subEmitters; subs.enabled = false;
                parent.TriggerSubEmitter(0, ref particle);
                Assert.Equal(0, child.particleCount);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void AChildInTwoDeathSlotsEmitsOnceFromTheFirst()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Death, new[] { new ParticleSystem.Burst(0f, 3) }, out var child);
            try
            {
                var subs = parent.subEmitters;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor);
                parent.SetParticles(new[] { Particle(.013f) }, 1);
                parent.Simulate(.025f, false, false, false);
                Assert.Equal(0, parent.particleCount);
                Assert.Equal(3, child.particleCount);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void InheritLifetimeMultipliesByTheParentsRemainingSeconds()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Manual, new[] { new ParticleSystem.Burst(0f, 1) }, out var child,
                ParticleSystemSubEmitterProperties.InheritLifetime);
            try
            {
                var particle = Particle(4.013f);
                particle.remainingLifetime = 3.863f;
                parent.TriggerSubEmitter(0, ref particle);
                var born = ParticlesOf(child)[0];
                Assert.Equal(2.013f * 3.863f, born.startLifetime, 4);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void TheChildsFrameFollowsTheParentsDirectionOfTravel()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Manual, new[] { new ParticleSystem.Burst(0f, 1) }, out var child);
            try
            {
                var rising = Particle(5f, new Vector3(0f, 2f, 0f));
                parent.TriggerSubEmitter(0, ref rising);
                var still = Particle(5f);
                parent.TriggerSubEmitter(0, ref still);
                var particles = ParticlesOf(child);
                Array.Sort(particles, (a, b) => a.velocity.y.CompareTo(b.velocity.y));
                Near(new Vector3(0f, 0f, 1f), particles[0].velocity);
                Near(new Vector3(0f, 1f, 0f), particles[1].velocity);

                // A shifted zero-size box: its offset turns with the frame (+Z to the travel, +Y
                // towards +Z): (0.5, 0.25, 0.125) along +X lands at (0.125, 0.5, 0.25).
                child.Clear();
                var shape = child.shape; shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = Vector3.zero;
                shape.position = new Vector3(0.5f, 0.25f, 0.125f);
                var sideways = Particle(5f, new Vector3(2f, 0f, 0f));
                parent.TriggerSubEmitter(0, ref sideways);
                var born = ParticlesOf(child)[0];
                Near(new Vector3(0.125f, 0.5f, 0.25f), born.position);
                Near(new Vector3(1f, 0f, 0f), born.velocity);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }
    }

    /// <summary>Death and Manual sub-emitters in the frame's particle update
    /// (ParticleSystemManager.UpdateAll), as the 6000.0.71f1 subemitters probe measures them
    /// (played/death, played/finish, played/stopped). These tests set the global clock, so they run
    /// with the other frame tests.</summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleEventSubEmitterFrameTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly float m_MaximumParticleDeltaTime = Time.s_MaximumParticleDeltaTime;
        readonly List<Object> m_Objects = new List<Object>();

        public ParticleEventSubEmitterFrameTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_UnscaledDeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            Time.s_MaximumParticleDeltaTime = 0.03f;
        }

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_UnscaledDeltaTime = m_UnscaledDeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
            Time.s_MaximumParticleDeltaTime = m_MaximumParticleDeltaTime;
        }

        /// <summary>A one-shot parent (always simulated) with a Death or Manual child bursting 2
        /// particles that live <paramref name="childLife"/>; the parent's stop action disables it.</summary>
        ParticleSystem OneShot(ParticleSystemSubEmitterType type, float childLife, out ParticleSystem child)
        {
            var parent = ParticleEventSubEmitterTests.Linked(type, new[] { new ParticleSystem.Burst(0f, 2) }, out child);
            m_Objects.Add(parent.gameObject);
            var main = parent.main;
            main.loop = false;
            main.duration = 0.05f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.stopAction = ParticleSystemStopAction.Disable;
            var childMain = child.main;
            childMain.startLifetime = childLife;
            childMain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            return parent;
        }

        static void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ParticleSystemManager.UpdateAll();
                ParticleSystemManager.CompleteUpdate();
            }
        }

        [Fact]
        public void DeathChildrenStartAtTheFramesEndPlayAndHoldTheParentsStop()
        {
            var parent = OneShot(ParticleSystemSubEmitterType.Death, 0.05f, out var child);
            parent.Play();
            // The particle dies in the third frame, when the 0.05 s duration ends.
            parent.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = Vector3.up, startLifetime = 0.053f }, 1);
            Frames(3);
            Assert.Equal(0, parent.particleCount);
            Assert.False(parent.isPlaying);
            Assert.True(parent.IsAlive(true));
            Assert.Equal(2, child.particleCount);
            Assert.True(child.isPlaying);
            var born = new ParticleSystem.Particle[2];
            child.GetParticles(born);
            Assert.Equal(born[0].startLifetime, born[0].remainingLifetime);
            Assert.True(parent.gameObject.activeSelf);
            Frames(2);
            Assert.Equal(2, child.particleCount);
            Assert.True(parent.gameObject.activeSelf);
            Frames(1);
            Assert.Equal(0, child.particleCount);
            Assert.False(parent.gameObject.activeSelf);
        }

        [Fact]
        public void ParticlesTriggeredAfterTheParentStoppedWaitForItToPlay()
        {
            var parent = OneShot(ParticleSystemSubEmitterType.Manual, 1f, out var child);
            var main = parent.main; main.stopAction = ParticleSystemStopAction.None;
            var particle = ParticleEventSubEmitterTests.Particle(5f, Vector3.up);
            parent.TriggerSubEmitter(0, ref particle);
            Assert.Equal(2, child.particleCount);
            Frames(3);
            var particles = new ParticleSystem.Particle[2];
            child.GetParticles(particles);
            Assert.Equal(1f, particles[0].remainingLifetime);
            Assert.False(child.isPlaying);

            parent.Play();
            Frames(1);
            child.GetParticles(particles);
            Assert.Equal(1f - Dt, particles[0].remainingLifetime, 5);
            Assert.True(child.isPlaying);
        }
    }
}
