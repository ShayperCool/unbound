using System;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The burst a Death or Manual sub-emitter emits. 6000.0 Scripting API,
    /// SubEmittersModule.death0 remarks: the sub-emitter "uses the first set of emission burst
    /// properties to spawn a sub-Particle System. If the system does not contain burst properties,
    /// then the system emits nothing; if the system contains more than one set of burst
    /// properties, then it ignores the additional properties." ParticleSystem.TriggerSubEmitter and
    /// ParticleSystemSubEmitterType.Manual say nothing about bursts; the Manual's Sub Emitters
    /// module page lists Manual with Death among the conditions that "support only a single burst
    /// in the Bursts list", so the Manual cases pin Unbound's choice (the Death rule), not measured
    /// Unity behaviour. AddSubEmitter's emitProbability: "0 is never and 1 is always".</summary>
    public class ParticleDeathSubEmitterBurstTests
    {
        static readonly ParticleSystem.Burst[] OneThenThree = { new ParticleSystem.Burst(0f, (short)1), new ParticleSystem.Burst(0f, (short)3) };

        /// <summary>A still, stopped system on a fresh object, configured before it becomes active.</summary>
        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("particles");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.startLifetime = 10f; main.startSpeed = 0f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            configure(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A non-emitting parent with <paramref name="child"/> (parented under it, emitting
        /// <paramref name="bursts"/>) as its only sub-emitter, of <paramref name="type"/>.</summary>
        static ParticleSystem Linked(ParticleSystemSubEmitterType type, ParticleSystem.Burst[] bursts, out ParticleSystem child, float emitProbability = 1f)
        {
            var parent = Make(system => { var e = system.emission; e.enabled = false; });
            child = Make(system => system.emission.SetBursts(bursts));
            child.transform.SetParent(parent.transform, false);
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, type, ParticleSystemSubEmitterProperties.InheritNothing, emitProbability);
            return parent;
        }

        static ParticleSystem.Particle Parent(float lifetime) => new ParticleSystem.Particle
        {
            position = Vector3.zero, velocity = Vector3.zero, startLifetime = lifetime, remainingLifetime = lifetime,
            startSize = 1f, startColor = new Color32(255, 255, 255, 255), randomSeed = 12345,
        };

        /// <summary>Gives the stopped parent one particle with .013 s left and runs one .025 s step
        /// without children, in which that particle dies (the path of the trigger lifetime
        /// natural_death_child control in ParticleSystemTests).</summary>
        static void DieOnce(ParticleSystem parent)
        {
            parent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            parent.SetParticles(new[] { Parent(.013f) }, 1);
            parent.Simulate(.025f, false, false, false);
            Assert.Equal(0, parent.particleCount);
        }

        static void Done(ParticleSystem parent) => Object.DestroyImmediate(parent.gameObject);

        [Fact]
        public void ADeathSubEmitterEmitsOnlyItsChildsFirstBurst()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Death, OneThenThree, out var child);
            try
            {
                DieOnce(parent);
                Assert.Equal(1, child.particleCount);
            }
            finally { Done(parent); }
        }

        [Fact]
        public void ADeathSubEmitterWithoutBurstsEmitsNothing()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Death, Array.Empty<ParticleSystem.Burst>(), out var child);
            try
            {
                DieOnce(parent);
                Assert.Equal(0, child.particleCount);
            }
            finally { Done(parent); }
        }

        [Fact]
        public void TriggerSubEmitterEmitsOnlyTheManualChildsFirstBurst()
        {
            var parent = Linked(ParticleSystemSubEmitterType.Manual, OneThenThree, out var child);
            try
            {
                var particle = Parent(10f);
                parent.TriggerSubEmitter(0, ref particle);
                Assert.Equal(1, child.particleCount);
                parent.TriggerSubEmitter(0, ref particle);
                Assert.Equal(2, child.particleCount);
            }
            finally { Done(parent); }
        }

        [Theory]
        [InlineData(ParticleSystemSubEmitterType.Death)]
        [InlineData(ParticleSystemSubEmitterType.Manual)]
        public void ASubEmitterWithEmitProbabilityZeroEmitsNothing(ParticleSystemSubEmitterType type)
        {
            var parent = Linked(type, OneThenThree, out var child, 0f);
            try
            {
                if (type == ParticleSystemSubEmitterType.Death) DieOnce(parent);
                else
                {
                    var particle = Parent(10f);
                    parent.TriggerSubEmitter(0, ref particle);
                }
                Assert.Equal(0, child.particleCount);
            }
            finally { Done(parent); }
        }
    }
}
