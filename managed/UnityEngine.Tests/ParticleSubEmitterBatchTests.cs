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

        [Theory]
        [InlineData(0.5f, 3f, 100f)]
        [InlineData(0.001f, 1f, 500f)]
        public void ChildrenTheCatchUpEndsLeaveWithoutDisplacingTheirBatch(float shortest, float longest, float rate)
        {
            // A long Simulate call: a parent particle moving at 1 unit/s along z carries a Birth
            // child of random lifetimes at rest (some dying at birth, some in the catch-up).
            // Every child left lies where the parent was when it was born: z = 5 - age, within a
            // step, whichever lanes of its batch stayed.
            var child = Make(c =>
            {
                var m = c.main; m.startLifetime = new ParticleSystem.MinMaxCurve(shortest, longest);
                m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 5000;
                var e = c.emission; e.rateOverTime = rate;
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
        public void ADeathCycleBackIntoASystemKeepsTheOuterPassDeaths()
        {
            // A's dying particles emit into B, whose dying particles emit back into A while A's
            // own deaths are still being removed: no spent particle of A stays behind.
            var a = Make(s =>
            {
                var m = s.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 1f); m.maxParticles = 5000;
                // Its burst is what B's Death sub-emitter emits back into it.
                s.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)3) });
            });
            var b = Make(s =>
            {
                var m = s.main; m.maxParticles = 5000;
                s.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)2) });
            });
            var aSubs = a.subEmitters; aSubs.enabled = true;
            aSubs.AddSubEmitter(b, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritLifetime);
            var bSubs = b.subEmitters; bSubs.enabled = true;
            bSubs.AddSubEmitter(a, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            try
            {
                a.Simulate(0f, false, true, false);
                a.Emit(40);
                var buffer = new ParticleSystem.Particle[5000];
                int spent = 0;
                for (int call = 0; call < 30; call++)
                {
                    a.Simulate(0.03f, false, false, false);
                    int n = a.GetParticles(buffer);
                    for (int i = 0; i < n; i++)
                        if (buffer[i].remainingLifetime <= 0f) spent++;
                }
                Assert.Equal(0, spent);
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
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
