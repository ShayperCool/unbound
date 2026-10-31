using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    // Expectations come from the independent Unity 6000.0.71f1 emission recordings.
    public class ParticleEmissionTests
    {
        static ParticleSystem Make(bool enabled, int source = 0, bool loop = false, float delay = 0, bool live = false)
        {
            var go = new GameObject("emission-control"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = loop;
            main.startDelay = delay; main.startLifetime = 2; main.startSpeed = 0;
            var emission = ps.emission; emission.enabled = enabled;
            emission.rateOverTime = source == 1 ? 4 : 0; emission.rateOverDistance = source == 2 ? 4 : 0;
            if (source == 3) emission.SetBursts(new[] { new ParticleSystem.Burst(.25f, (short)2) });
            var shape = ps.shape; shape.enabled = false;
            go.SetActive(true);
            if (live) ps.SetParticles(new[] { new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 2 } });
            return ps;
        }

        static void State(ParticleSystem ps, bool playing, bool emitting, bool paused, bool stopped, bool alive)
        {
            Assert.Equal(playing, ps.isPlaying);
            Assert.Equal(emitting, ps.isEmitting);
            Assert.Equal(paused, ps.isPaused);
            Assert.Equal(stopped, ps.isStopped);
            Assert.Equal(alive, ps.IsAlive(false));
        }

        public static IEnumerable<object[]> Configurations()
        {
            foreach (bool enabled in new[] { false, true })
            for (int source = 0; source < 4; source++)
            foreach (bool loop in new[] { false, true })
            foreach (float delay in new[] { 0f, .25f })
            foreach (bool live in new[] { false, true })
                yield return new object[] { enabled, source, loop, delay, live };
        }

        [Theory]
        [MemberData(nameof(Configurations))]
        public void ModuleTogglesPreservePlaybackRegardlessOfEmissionSource(bool enabled, int source, bool loop, float delay, bool live)
        {
            var ps = Make(enabled, source, loop, delay, live);
            try
            {
                State(ps, false, false, false, true, live);
                var emission = ps.emission;
                ps.Play(false);
                State(ps, true, enabled, false, false, true);
                int count = ps.particleCount;
                foreach (bool value in new[] { false, false, true, true, false, true })
                {
                    emission.enabled = value;
                    State(ps, true, value, false, false, true);
                    Assert.Equal(count, ps.particleCount);
                    Assert.Equal(0f, ps.time);
                }
                ps.Pause(false);
                foreach (bool value in new[] { false, false, true, true })
                {
                    emission.enabled = value;
                    State(ps, false, false, true, false, true);
                }
                ps.Play(false);
                State(ps, true, true, false, false, true);
                ps.Emit(1);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                foreach (bool value in new[] { false, false, true, true })
                {
                    emission.enabled = value;
                    State(ps, true, false, false, false, true);
                }
                ps.Clear(false);
                State(ps, false, false, false, true, false);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZeroEmitDoesNotStartPlaybackAndManualParticlesDoNotStartEmission(bool enabled)
        {
            var ps = Make(enabled);
            try
            {
                ps.Emit(0);
                State(ps, false, false, false, true, false);
                ps.Emit(1);
                State(ps, true, false, false, false, true);
                var emission = ps.emission; emission.enabled = !enabled; emission.enabled = enabled;
                State(ps, true, false, false, false, true);
                ps.Pause(false);
                ps.Emit(new ParticleSystem.EmitParams { startLifetime = 2 }, 1);
                Assert.Equal(2, ps.particleCount);
                State(ps, true, false, false, false, true);
                ps.Clear(false);
                State(ps, false, false, false, true, false);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        public static IEnumerable<object[]> PlaybackStates()
        {
            foreach (bool enabled in new[] { false, true })
            for (int saved = 0; saved < 4; saved++)
            for (int target = 0; target < 4; target++)
                yield return new object[] { enabled, saved, target };
        }

        static void Setup(ParticleSystem ps, int state)
        {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (state > 0) { ps.Play(false); ps.time = .375f; }
            if (state == 2) ps.Pause(false);
            if (state == 3) { ps.Emit(1); ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
        }

        [Theory]
        [MemberData(nameof(PlaybackStates))]
        public void PlaybackSnapshotRestoresTimeWithoutReplacingCurrentState(bool enabled, int saved, int target)
        {
            var ps = Make(enabled);
            try
            {
                Setup(ps, saved);
                var snapshot = ps.GetPlaybackState();
                float time = ps.time;
                Setup(ps, target);
                ps.SetPlaybackState(snapshot);
                State(ps, target == 1 || target == 3, enabled && target == 1, target == 2, target == 0, target != 0);
                Assert.Equal(target == 3 ? 1 : 0, ps.particleCount);
                Assert.Equal(time, ps.time);
                var emission = ps.emission; emission.enabled = !enabled;
                ps.SetPlaybackState(snapshot);
                Assert.Equal(!enabled, ps.emission.enabled);
                Assert.Equal(!enabled && target == 1, ps.isEmitting);
                ps.Play(false);
                Assert.Equal(target == 2 ? time : 0f, ps.time);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ResumeAfterSimulatedDurationKeepsTimeAndResumesEmission(bool enabled, bool fixedStep)
        {
            var ps = Make(enabled);
            try
            {
                ps.Play(false);
                ps.Simulate(2f, false, false, fixedStep);
                State(ps, false, false, true, false, true);
                float time = ps.time;
                Assert.InRange(time, .9998f, 1.0002f);
                ps.Play(false);
                State(ps, true, enabled, false, false, true);
                Assert.Equal(time, ps.time);
                ps.Clear(false);
                State(ps, true, enabled, false, false, true);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ParentModuleSwitchDoesNotPropagateWithPlayback(bool enabled, bool withChildren)
        {
            var parent = Make(enabled);
            var child = Make(!enabled);
            child.transform.SetParent(parent.transform, false);
            try
            {
                parent.Play(withChildren);
                State(parent, true, enabled, false, false, true);
                State(child, withChildren, withChildren && !enabled, false, !withChildren, withChildren);
                var emission = parent.emission; emission.enabled = !enabled;
                Assert.Equal(!enabled, child.emission.enabled);
                Assert.Equal(withChildren && !enabled, child.isEmitting);
                parent.Pause(withChildren);
                Assert.False(parent.isEmitting);
                Assert.False(child.isEmitting);
                parent.Play(withChildren);
                parent.Stop(withChildren, ParticleSystemStopBehavior.StopEmitting);
                State(parent, false, false, false, true, false);
                State(child, false, false, false, true, false);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        public static IEnumerable<object[]> StoppedSimulations()
        {
            foreach (bool enabled in new[] { false, true })
            foreach (bool fixedStep in new[] { false, true })
            foreach (bool loop in new[] { false, true })
            foreach (float delay in new[] { 0f, .25f })
                yield return new object[] { enabled, fixedStep, loop, delay };
        }

        [Theory]
        [MemberData(nameof(StoppedSimulations))]
        public void StoppedSimulationAdvancesOnlyTheExistingClock(bool enabled, bool fixedStep, bool loop, float delay)
        {
            var ps = Make(enabled, 1, loop, delay);
            try
            {
                ps.Simulate(.125f, false, true, fixedStep);
                ps.Play(false);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                State(ps, false, false, false, true, false);
                ps.Simulate(3f, false, false, fixedStep);
                State(ps, false, false, true, false, true);
                Assert.Equal(0, ps.particleCount);
                float expected = delay > 0 ? 0 : loop ? fixedStep ? .12f : .125f : 1;
                Assert.InRange(ps.time, expected - 2e-4f, expected + 2e-4f);
                ps.Play(false);
                State(ps, true, enabled, false, false, true);
                Assert.InRange(ps.time, expected - 2e-4f, expected + 2e-4f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
