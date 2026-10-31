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
    }
}
