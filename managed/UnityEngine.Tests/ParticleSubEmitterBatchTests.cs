using System;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A sub-emitter emission starts as its own batch of four-slot groups, as a step's
    /// births do: laid out from the first multiple of four after the child's live particles,
    /// its last particles then fill the gap before it (Unity 6000.0.71f1,
    /// tests/projects/particlemeshshape texture/clip/deathsub, tests/projects/particlerandom
    /// birthlife/batch). Unity documents no buffer order; these are measured layouts.</summary>
    public class ParticleSubEmitterBatchTests : IDisposable
    {
        readonly string m_Version = ProjectInfo.UnityVersion;

        public ParticleSubEmitterBatchTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose() => ProjectInfo.UnityVersion = m_Version;

        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("particles");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 99u;
            var main = ps.main; main.startLifetime = 10f; main.startSpeed = 0f; main.playOnAwake = false;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            configure(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A parent with one particle and a Manual sub-emitter whose child emits a burst
        /// of three, after <paramref name="before"/> particles set on the child.</summary>
        static uint[] ChildSeedsAfterTrigger(int before)
        {
            var child = Make(c => c.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)3) }));
            var parent = Make(p =>
            {
                var subs = p.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
            });
            try
            {
                parent.Simulate(0f, false, true, false);
                child.Simulate(0f, false, true, false);
                parent.SetParticles(new[] { new ParticleSystem.Particle { startLifetime = 10f, remainingLifetime = 10f, startSize = 1f, randomSeed = 1 } });
                child.SetParticles(Enumerable.Range(0, before).Select(i => new ParticleSystem.Particle
                {
                    startLifetime = 10f, remainingLifetime = 10f, startSize = 1f, randomSeed = (uint)(1000 + i),
                }).ToArray());
                parent.TriggerSubEmitter(0);
                var particles = new ParticleSystem.Particle[child.particleCount];
                child.GetParticles(particles);
                return particles.Select(p => p.randomSeed).ToArray();
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
                Object.DestroyImmediate(child.gameObject);
            }
        }

        [Fact]
        public void ChildrenTheCatchUpEndsLeaveWithoutDisplacingTheirBatch()
        {
            // A long Simulate call: a parent particle moving at 1 unit/s along z carries a Birth
            // child of random lifetimes (0.5 to 3 s) at rest. Every child left lies where the
            // parent was when it was born: z = 5 - age, within a step.
            var child = Make(c =>
            {
                var m = c.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 3f);
                m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 1000;
                var e = c.emission; e.rateOverTime = 100f;
            });
            var parent = Make(p =>
            {
                var m = p.main; m.startSpeed = 1f; m.simulationSpace = ParticleSystemSimulationSpace.World;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var subs = p.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
            });
            child.transform.SetParent(parent.transform, false);
            try
            {
                parent.Simulate(5f, true, true, true);
                var particles = new ParticleSystem.Particle[child.particleCount];
                int n = child.GetParticles(particles);
                Assert.True(n > 50, $"{n} children");
                foreach (var p in particles.Take(n))
                {
                    float age = p.startLifetime - p.remainingLifetime;
                    Assert.True(p.remainingLifetime > 0f, $"spent child at z {p.position.z}");
                    Assert.True(Mathf.Abs(p.position.z - (5f - age)) < 0.05f, $"z {p.position.z}, age {age}");
                }
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void AnEmissionAfterAnUnalignedCountFillsTheGapWithItsLastParticles()
        {
            var alone = ChildSeedsAfterTrigger(0);
            Assert.Equal(3, alone.Length);
            // Two particles set before: the batch starts at slot 4; its last two fill slots 2
            // and 3 and its first stays at slot 4.
            var after = ChildSeedsAfterTrigger(2);
            Assert.Equal(new uint[] { 1000, 1001, alone[1], alone[2], alone[0] }, after);
            // Four set before: the batch is aligned and keeps its order.
            Assert.Equal(new uint[] { 1000, 1001, 1002, 1003, alone[0], alone[1], alone[2] }, ChildSeedsAfterTrigger(4));
        }
    }
}
