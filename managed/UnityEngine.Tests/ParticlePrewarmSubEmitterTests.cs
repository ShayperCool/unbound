using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Prewarm, the end of a Birth child's emission, sub-emitter start values and inheritance, and
    /// trails under a minimum vertex distance. The 6000.0 Scripting API (MainModule.prewarm) only
    /// says a prewarmed looping system starts "as though it had already emitted particles from a
    /// previous cycle". Measured in 6000.0.71f1 (tests/projects/particlerender prewarm/*,
    /// birthend/*, inherit/*; tests/projects/particletrails cap/*, mvd/*).
    /// </summary>
    [Collection("Particle culling frames")]
    public sealed class ParticlePrewarmSubEmitterTests : IDisposable
    {
        const float Dt = 0.05f;
        static readonly Vector3 kFar = new Vector3(4000f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly float m_MaximumParticleDeltaTime = Time.s_MaximumParticleDeltaTime;
        readonly int m_FrameCount = Time.s_FrameCount;
        readonly string m_Version = ProjectInfo.UnityVersion;
        readonly List<Object> m_Objects = new List<Object>();

        public ParticlePrewarmSubEmitterTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Time.s_DeltaTime = Dt;
            Time.s_UnscaledDeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            Time.s_MaximumParticleDeltaTime = 0.03f;
        }

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            ParticleSystemManager.EndUpdateAll();
            ProjectInfo.UnityVersion = m_Version;
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_UnscaledDeltaTime = m_UnscaledDeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
            Time.s_MaximumParticleDeltaTime = m_MaximumParticleDeltaTime;
            Time.s_FrameCount = m_FrameCount;
        }

        ParticleSystem Make(string name, Action<ParticleSystem> configure, Transform parent = null)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            else go.transform.position = kFar;
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 777u;
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = 256;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = ps.shape;
            shape.enabled = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            configure?.Invoke(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A looping prewarmed parent: one particle a loop, 0.7 s, moving at 1 unit/s.</summary>
        ParticleSystem Parent(Action<ParticleSystem> configure = null) => Make("parent", p =>
        {
            var m = p.main; m.loop = true; m.prewarm = true; m.startLifetime = 0.7f; m.startSpeed = 1f;
            var e = p.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            configure?.Invoke(p);
        });

        ParticleSystem Child(ParticleSystem parent, Action<ParticleSystem> configure = null,
            ParticleSystemSubEmitterType type = ParticleSystemSubEmitterType.Birth,
            ParticleSystemSubEmitterProperties properties = ParticleSystemSubEmitterProperties.InheritNothing)
        {
            var child = Make("child", c =>
            {
                var m = c.main; m.startLifetime = 3f; m.simulationSpace = ParticleSystemSimulationSpace.World;
                var e = c.emission; e.rateOverTime = 10f;
                configure?.Invoke(c);
            }, parent.transform);
            var subs = parent.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(child, type, properties);
            return child;
        }

        static void Frame()
        {
            ParticleSystemManager.UpdateAll();
            ParticleSystemManager.CompleteUpdate();
            ParticleSystemManager.EndUpdateAll();
            Time.s_FrameCount++;
        }

        static ParticleSystem.Particle[] Particles(ParticleSystem ps)
        {
            var buffer = new ParticleSystem.Particle[Math.Max(ps.particleCount, 1)];
            return buffer.Take(ps.GetParticles(buffer)).ToArray();
        }

        static float[] Ages(ParticleSystem ps) =>
            Particles(ps).Select(p => p.startLifetime - p.remainingLifetime).OrderBy(a => a).ToArray();

        static bool AtLoopEnd(float time, float duration) => time < 1e-4f || Mathf.Abs(time - duration) < 1e-4f;

        [Fact]
        public void APrewarmWithoutSubEmittersSimulatesOneLifetimeEndingOnALoopEnd()
        {
            // 2.5 s from 0.5 s into the loop: the loop starts pass at 0.5 and 1.5 s (World space:
            // a procedural system's prewarm is not modelled).
            var ps = Parent(p => { var m = p.main; m.startLifetime = 2.5f; m.simulationSpace = ParticleSystemSimulationSpace.World; });
            ps.Play();
            Assert.True(AtLoopEnd(ps.time, 1f), $"time {ps.time}");
            var ages = Ages(ps).Where(a => a > 0.5f).ToArray();
            Assert.Equal(2, ages.Length);
            Assert.Equal(1f, ages[0], 1);
            Assert.Equal(2f, ages[1], 1);
        }

        [Theory]
        [InlineData(3f, 3)]
        [InlineData(1f, 1)]
        [InlineData(2f, 2)]
        public void APrewarmKeepsBirthChildrenWithinTheirLifetimeOfTheEndUnaged(float childLifetime, int loops)
        {
            var parent = Parent();
            var child = Child(parent, c => { var m = c.main; m.startLifetime = childLifetime; });
            parent.Play();
            // Six children a parent particle (one each 0.1 s of its 0.7 s), from the parents of the
            // loops within the child's lifetime of the end, none aged by the rest of the prewarm.
            Assert.Equal(6 * loops, child.particleCount);
            Assert.All(Ages(child), a => Assert.InRange(a, 0f, Time.maximumParticleDeltaTime));
            Assert.True(child.isPlaying);
            Assert.False(child.isEmitting);
            Assert.Equal(1f, child.time, 4);
            // From the first update the children age with the frame.
            Frame();
            Assert.All(Ages(child), a => Assert.InRange(a, Dt, Dt + Time.maximumParticleDeltaTime));
        }

        [Fact]
        public void APrewarmChainAddsEachLifetimeAndStartsTheChain()
        {
            var parent = Parent();
            var child = Child(parent);
            var grandchild = Child(child, g => { var e = g.emission; e.rateOverTime = 5f; });
            parent.Play();
            // 0.7 + 3 + 3 s: the children of the last three loops; the grandchild plays, empty, at time 0.
            Assert.Equal(18, child.particleCount);
            Assert.True(grandchild.isPlaying);
            Assert.Equal(0, grandchild.particleCount);
            Assert.Equal(0f, grandchild.time);
        }

        [Fact]
        public void APrewarmEmitsDeathChildrenAtZeroAge()
        {
            var parent = Parent();
            var child = Child(parent, c =>
            {
                var e = c.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            }, ParticleSystemSubEmitterType.Death);
            parent.Play();
            Assert.Equal(9, child.particleCount);
            Assert.All(Ages(child), a => Assert.Equal(0f, a));
            Assert.All(Particles(child), p => Assert.Equal(0.7f, p.position.z - kFar.z, 1));
        }

        [Fact]
        public void OtherReleasesKeepTheOneLifetimePrewarm()
        {
            ProjectInfo.UnityVersion = "2022.3.10f1";
            var parent = Parent();
            var child = Child(parent);
            parent.Play();
            Assert.Equal(0f, parent.time);
            Assert.InRange(child.particleCount, 0, 6);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ABirthChildEmitsWithItsEmissionModuleDisabled(bool simulate)
        {
            var parent = Parent(p => { var m = p.main; m.prewarm = false; });
            var child = Child(parent, c => { var e = c.emission; e.enabled = false; });
            Time.s_DeltaTime = Time.s_UnscaledDeltaTime = 0.02f;
            if (!simulate) parent.Play();
            for (int i = 0; i < 32; i++)
            {
                if (simulate) parent.Simulate(0.02f, true, i == 0, false);
                else Frame();
            }
            // 0.64 s of a parent particle at 10/s.
            Assert.Equal(6, child.particleCount);
        }

        [Theory]
        [InlineData(false, 1f, 9)]
        [InlineData(true, 1f, 9)]
        [InlineData(true, 0.5f, 4)]
        public void ANonLoopingChildEmitsNothingAtItsDuration(bool simulate, float childDuration, int expected)
        {
            // A 1.5 s parent particle under a child at 10/s: births at 0.1 s steps up to, not at, the duration.
            var parent = Parent(p => { var m = p.main; m.prewarm = false; m.loop = false; m.startLifetime = 1.5f; });
            var child = Child(parent, c => { var m = c.main; m.duration = childDuration; });
            Time.s_DeltaTime = Time.s_UnscaledDeltaTime = 0.02f;
            if (!simulate) parent.Play();
            for (int i = 0; i < 70; i++)
            {
                if (simulate) parent.Simulate(0.02f, true, i == 0, false);
                else Frame();
            }
            Assert.Equal(expected, child.particleCount);
        }

        [Theory]
        [InlineData(ParticleSystemSubEmitterType.Birth)]
        [InlineData(ParticleSystemSubEmitterType.Death)]
        public void AChildSamplesItsStartCurvesAtTheParentParticlesAge(ParticleSystemSubEmitterType type)
        {
            var parent = Parent(p => { var m = p.main; m.prewarm = false; m.loop = false; m.startLifetime = 0.6f; });
            var child = Child(parent, c =>
            {
                var m = c.main; m.duration = 5f;
                m.startSize = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.2f, 1f, 1f));
                if (type == ParticleSystemSubEmitterType.Death)
                {
                    var e = c.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                }
            }, type);
            Time.s_DeltaTime = Time.s_UnscaledDeltaTime = 0.02f;
            parent.Play();
            for (int i = 0; i < 40; i++) Frame();
            var children = Particles(child);
            Assert.NotEmpty(children);
            foreach (var p in children)
            {
                // Birth: the parent's normalized age when the child came (its z position over 0.6 s),
                // within a step; Death: 1. The child's own clock (0.02 to 0.16 of its 5 s) would give 0.2 to 0.33.
                float expected = type == ParticleSystemSubEmitterType.Death ? 1f : 0.2f + 0.8f * (p.position.z - kFar.z) / 0.6f;
                Assert.InRange(p.startSize, expected - 0.03f, expected + 0.03f);
            }
        }

        [Fact]
        public void DeathChildrenWithNoInheritedLifetimeStayUntilTheChildsNextUpdate()
        {
            var parent = Parent(p => { var m = p.main; m.prewarm = false; m.loop = false; m.startLifetime = 0.5f; });
            var child = Child(parent, c =>
            {
                var e = c.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            }, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritLifetime);
            parent.Play();
            int frames = 0;
            while (child.particleCount == 0 && frames++ < 20) Frame();
            Assert.Equal(2, child.particleCount);
            Assert.All(Particles(child), p => Assert.Equal(0f, p.remainingLifetime));
            Frame();
            Assert.Equal(0, child.particleCount);
        }

        [Fact]
        public void ATrailRestartsAtItsHeadOnceItsNewestPointExpired()
        {
            // 0.2 units/s with a 0.05 minimum vertex distance and a 0.2 s trail: the particle never
            // covers the distance before its newest point expires.
            var ps = Make("trail", p =>
            {
                var m = p.main; m.startLifetime = 2f; m.startSpeed = 0.2f;
                var e = p.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var t = p.trails; t.enabled = true; t.lifetime = 0.1f; t.minVertexDistance = 0.05f; t.dieWithParticles = true;
            });
            float lastPointTime = -1f;
            int restarts = 0;
            for (int i = 0; i < 40; i++)
            {
                ps.Simulate(0.02f, true, i == 0, false);
                var p = Particles(ps).Single();
                var trail = ps.m_TrailList[p.m_TrailId - 1];
                // One point, never older than the trail lifetime: no expired anchor, no cut tail.
                Assert.Single(trail.Points);
                Assert.InRange(ps.m_TrailTime - trail.Times[0], 0f, 0.2f + 1e-4f);
                if (trail.Times[0] != lastPointTime) { restarts++; lastPointTime = trail.Times[0]; }
            }
            Assert.InRange(restarts, 3, 5);
        }

        [Fact]
        public void RepeatedRestartingSimulateCallsOfAPrewarmedParentAgree()
        {
            var parent = Parent();
            var child = Child(parent);
            float[] first = null;
            float firstTime = 0f;
            for (int call = 0; call < 3; call++)
            {
                parent.Simulate(0.05f, true, true, false);
                var ages = Ages(child);
                if (first == null) { first = ages; firstTime = child.time; continue; }
                Assert.Equal(first, ages);
                Assert.Equal(firstTime, child.time);
            }
            Assert.Equal(18, first.Length);
        }

        [Fact]
        public void APrewarmGivesTheChildTheSameTimeOnEveryPlay()
        {
            var parent = Parent();
            var child = Child(parent, c => { var m = c.main; m.loop = true; m.duration = 2.5f; });
            var times = new List<float>();
            for (int round = 0; round < 3; round++)
            {
                parent.Play();
                times.Add(child.time);
                for (int i = 0; i < 7; i++) Frame();
                parent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            // A playback of the 3.7 s span: 1.2 s into the child's 2.5 s loop, every time.
            Assert.All(times, t => Assert.Equal(1.2f, t, 3));
        }

        [Fact]
        public void ChildrenAfterARestartingSimulateDrawFreshSeeds()
        {
            var parent = Parent();
            var child = Child(parent);
            parent.Simulate(0.05f, true, true, false);
            for (int i = 0; i < 6; i++) parent.Simulate(0.05f, true, false, false);
            var seeds = Particles(child).Select(p => p.randomSeed).ToArray();
            Assert.True(seeds.Length > 18);
            Assert.Equal(seeds.Length, seeds.Distinct().Count());
        }
    }
}
