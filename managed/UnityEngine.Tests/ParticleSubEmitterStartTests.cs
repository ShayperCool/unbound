using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Sub-emitters in the frame's particle update. The 6000.0 Manual (Sub Emitters module) says a
    /// Birth sub-emitter emits "throughout the parent particle's lifetime" with the child's own
    /// emission settings, and nothing about when the child counts as playing or how old a child is
    /// at the end of the frame. Measured in 6000.0.71f1 (tests/projects/particlerender birthstart/*,
    /// subcull/*; tests/projects/subemitters played/*): every sub-emitter plays, without emitting,
    /// after each update of its playing parent and stops, silently and keeping its time, at the end
    /// of a frame that leaves it empty; a Birth child is as old at the end of the frame as it was
    /// due before the end of its parent's internal step, whatever the systems' update order; a
    /// parent emits no Birth child in the step its life ends.
    /// </summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleSubEmitterStartTests : IDisposable
    {
        const float Dt = 0.02f;
        static readonly Vector3 kFar = new Vector3(3000f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly float m_MaximumParticleDeltaTime = Time.s_MaximumParticleDeltaTime;
        readonly int m_FrameCount = Time.s_FrameCount;
        readonly string m_Version = ProjectInfo.UnityVersion;
        readonly List<Object> m_Objects = new List<Object>();

        public ParticleSubEmitterStartTests()
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
            main.loop = true;
            main.duration = 1f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = ps.shape;
            shape.enabled = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            configure?.Invoke(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A parent with a burst of two at once and a child of <paramref name="type"/>
        /// (7 a second for Birth, one in a burst otherwise).</summary>
        ParticleSystem Pair(ParticleSystemSubEmitterType type, out ParticleSystem child, float parentLifetime = 0.7f, float childRate = 7f,
            bool childFirst = false)
        {
            ParticleSystem MakeChild(Transform under) => Make("child", c =>
            {
                var m = c.main; m.startLifetime = 3f; m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 256;
                var e = c.emission;
                if (type == ParticleSystemSubEmitterType.Birth) e.rateOverTime = childRate;
                else e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            }, under);
            ParticleSystem early = childFirst ? MakeChild(null) : null;
            var parent = Make("parent", p =>
            {
                var m = p.main; m.startLifetime = parentLifetime;
                var e = p.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            });
            child = early ?? MakeChild(parent.transform);
            var subs = parent.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(child, type, ParticleSystemSubEmitterProperties.InheritNothing);
            return parent;
        }

        /// <summary>The frame's particle update, a script reading the systems (LateUpdate), then
        /// the end of the frame's update.</summary>
        static void Frame(Action late = null)
        {
            ParticleSystemManager.UpdateAll();
            ParticleSystemManager.CompleteUpdate();
            late?.Invoke();
            ParticleSystemManager.EndUpdateAll();
            Time.s_FrameCount++;
        }

        static float[] Ages(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            int n = ps.GetParticles(particles);
            return particles.Take(n).Select(p => p.startLifetime - p.remainingLifetime).OrderBy(a => a).ToArray();
        }

        [Theory]
        [InlineData(ParticleSystemSubEmitterType.Birth)]
        [InlineData(ParticleSystemSubEmitterType.Death)]
        [InlineData(ParticleSystemSubEmitterType.Collision)]
        [InlineData(ParticleSystemSubEmitterType.Trigger)]
        [InlineData(ParticleSystemSubEmitterType.Manual)]
        public void AChildPlaysAfterEachParentUpdateAndStopsEmptyAtTheFrameEnd(ParticleSystemSubEmitterType type)
        {
            var parent = Pair(type, out var child);
            parent.Play();
            Assert.False(child.isPlaying);
            for (int frame = 1; frame <= 3; frame++)
            {
                bool playing = false, emitting = true;
                float time = -1f;
                Frame(() => { playing = child.isPlaying; emitting = child.isEmitting; time = child.time; });
                Assert.True(playing);
                Assert.False(emitting);
                Assert.Equal(frame * Dt, time, 4);
                // Not playing before the next update, its time kept.
                Assert.Equal(type == ParticleSystemSubEmitterType.Birth ? child.particleCount > 0 : false, child.isPlaying);
                Assert.Equal(frame * Dt, child.time, 4);
            }
        }

        [Fact]
        public void APausedOrStoppedParentDoesNotStartItsChild()
        {
            var paused = Pair(ParticleSystemSubEmitterType.Death, out var pausedChild);
            var stopped = Pair(ParticleSystemSubEmitterType.Death, out var stoppedChild);
            paused.Play();
            paused.Pause(false);
            bool pausedPlaying = true, stoppedPlaying = true;
            Frame(() => { pausedPlaying = pausedChild.isPlaying; stoppedPlaying = stoppedChild.isPlaying; });
            Assert.False(pausedPlaying);
            Assert.False(stoppedPlaying);
            Assert.Equal(0f, stoppedChild.time);
        }

        [Fact]
        public void AChildAScriptPlayedAfterTheUpdateKeepsPlaying()
        {
            var parent = Pair(ParticleSystemSubEmitterType.Death, out var child);
            parent.Play();
            Frame(() => child.Play());
            Assert.True(child.isPlaying);
            Assert.True(child.isEmitting);
        }

        [Fact]
        public void AChainOfSubEmittersStartsWhateverTheUpdateOrder()
        {
            // C (made first, so updated first) is the sub-emitter of B, itself A's sub-emitter.
            var c = Make("c", s => { var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) }); });
            var b = Make("b", s => { var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) }); });
            var a = Make("a", s => { var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) }); });
            var bs = b.subEmitters; bs.enabled = true; bs.AddSubEmitter(c, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            var aSubs = a.subEmitters; aSubs.enabled = true; aSubs.AddSubEmitter(b, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            a.Play();
            bool bPlaying = false, cPlaying = false;
            float cTime = 0f;
            Frame(() => { bPlaying = b.isPlaying; cPlaying = c.isPlaying; cTime = c.time; });
            Assert.True(bPlaying);
            Assert.True(cPlaying);
            Assert.Equal(Dt, cTime, 4);
        }

        [Fact]
        public void AnUnmeasuredReleaseKeepsTheChildStoppedUntilItHasParticles()
        {
            ProjectInfo.UnityVersion = "2022.3.10f1";
            var parent = Pair(ParticleSystemSubEmitterType.Death, out var child);
            parent.Play();
            bool playing = true;
            Frame(() => playing = child.isPlaying);
            Assert.False(playing);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BirthChildrenAreAsOldAsTheyWereDueBeforeTheStepEnd(bool childFirst)
        {
            // Births at 1/7 s of the parents' ages, at the end of the frame of 0.16 s: 0.0171 s old.
            var parent = Pair(ParticleSystemSubEmitterType.Birth, out var child, childFirst: childFirst);
            parent.Play();
            for (int frame = 0; frame < 7; frame++) Frame();
            Assert.Equal(0, child.particleCount);
            Frame();
            var ages = Ages(child);
            Assert.Equal(2, ages.Length);
            Assert.All(ages, a => Assert.Equal(0.16f - 1f / 7f, a, 4));
        }

        [Fact]
        public void BirthChildrenDueInAnEarlierInternalStepAreNotAgedByTheLaterOne()
        {
            // Frames of 0.05 s in two steps of 0.025 s: the birth due at 4/7 s (0.5714) falls in the
            // first step of the frame ending at 0.6 s and is 0.575 - 0.5714 s old at the frame's end.
            Time.s_DeltaTime = Time.s_UnscaledDeltaTime = 0.05f;
            var parent = Pair(ParticleSystemSubEmitterType.Birth, out var child);
            parent.Play();
            for (int frame = 0; frame < 12; frame++) Frame();
            var ages = Ages(child);
            Assert.Equal(0.575f - 4f / 7f, ages[0], 4);
            Assert.Equal(0.575f - 4f / 7f, ages[1], 4);
        }

        [Fact]
        public void AParentEmitsNoBirthChildInTheStepItsLifeEnds()
        {
            // 0.5 s parents at 10 a second in steps of 0.025 s: births at 0.1 ... 0.4 s; the one due
            // exactly when the parent's life ends is not emitted.
            Time.s_DeltaTime = Time.s_UnscaledDeltaTime = 0.05f;
            var parent = Pair(ParticleSystemSubEmitterType.Birth, out var child, parentLifetime: 0.5f, childRate: 10f);
            var main = parent.main; main.loop = false;
            parent.Play();
            for (int frame = 0; frame < 12; frame++) Frame();
            Assert.Equal(8, child.particleCount);
        }

        [Fact]
        public void HeldBirthEmissionsAllocateNothingOnceWarm()
        {
            var parent = Pair(ParticleSystemSubEmitterType.Birth, out var child, parentLifetime: 5f, childRate: 40f);
            var e = parent.emission; e.rateOverTime = 20f;
            parent.Play();
            // Long enough for the child to reach its capacity (its buffers stop growing).
            for (int frame = 0; frame < 80; frame++) Frame();
            Assert.Equal(256, child.particleCount);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 5; frame++) Frame();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
