using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A frame update with zero frame time after a script Play or Emit leaves the renderer
    /// nothing to draw until time moves again (6000.0.71f1, tests/projects/particlerender firstvis/*,
    /// resources mask_particles). These tests set the global clock, so they run with the other
    /// frame tests.</summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleDrawPendingTests : IDisposable
    {
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_Playing = EditorSession.IsPlaying;

        public ParticleDrawPendingTests() => EditorSession.IsPlaying = true;

        public void Dispose()
        {
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_UnscaledDeltaTime = m_UnscaledDeltaTime;
            EditorSession.IsPlaying = m_Playing;
        }

        static void Frame(float delta)
        {
            Time.s_DeltaTime = delta;
            Time.s_UnscaledDeltaTime = 0.02f;
            ParticleSystemManager.UpdateAll();
            ParticleSystemManager.CompleteUpdate();
        }

        static ParticleSystem Make(Action<ParticleSystem.MainModule> configure = null)
        {
            var ps = new GameObject("pending").AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 5f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            configure?.Invoke(main);
            return ps;
        }

        static bool Pending(ParticleSystem ps) => ps.GetComponent<ParticleSystemRenderer>().NothingToDraw;

        [Fact]
        public void AZeroTimeUpdateAfterAScriptEmitLeavesNothingToDrawUntilTimeMoves()
        {
            var ps = Make();
            try
            {
                ps.Play();
                ps.Emit(1);
                // Drawn before any update.
                Assert.False(Pending(ps));
                Frame(0f);
                Assert.True(Pending(ps));
                Frame(0f);
                Assert.True(Pending(ps));
                Frame(0.02f);
                Assert.False(Pending(ps));
                // A zero-time update without a new Play or Emit keeps it drawable.
                Frame(0f);
                Assert.False(Pending(ps));
                // Simulate gives the bounds back.
                ps.Emit(1);
                Frame(0f);
                Assert.True(Pending(ps));
                ps.Simulate(0f, true, false);
                Assert.False(Pending(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void TimeMovesForAStillSystemAndForUnscaledTime()
        {
            // Simulation speed 0 does not matter: the frame's time moved.
            var still = Make(main => main.simulationSpeed = 0f);
            // Unscaled time moves while the time scale stops the scaled time.
            var unscaled = Make(main => main.useUnscaledTime = true);
            try
            {
                still.Play();
                still.Emit(1);
                unscaled.Play();
                unscaled.Emit(1);
                Frame(0f);
                Assert.True(Pending(still));
                Assert.False(Pending(unscaled));
                Frame(0.02f);
                Assert.False(Pending(still));
            }
            finally
            {
                Object.DestroyImmediate(still.gameObject);
                Object.DestroyImmediate(unscaled.gameObject);
            }
        }
    }
}
