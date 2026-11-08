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
            // Inactive while it is set up, so Play On Awake (on by default) does not play it.
            var go = new GameObject("pending");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 5f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            configure?.Invoke(main);
            go.SetActive(true);
            return ps;
        }

        static bool Pending(ParticleSystem ps) => ps.GetComponent<ParticleSystemRenderer>().NothingToDraw;

        [Fact]
        public void ASystemWithoutBoundsHasNothingToDrawAfterAZeroTimeUpdate()
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
                // Once it has bounds, zero-time updates keep them, also after an Emit or a Play.
                ps.Emit(1);
                ps.Play();
                Frame(0f);
                Assert.False(Pending(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ARenderBeforeAnyUpdateOrPlayOnAwakeGivesBounds()
        {
            var rendered = Make();
            var awake = new GameObject("awake");
            awake.SetActive(false);
            var onAwake = awake.AddComponent<ParticleSystem>();
            var main = onAwake.main; main.playOnAwake = true; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            try
            {
                rendered.Play();
                rendered.Emit(1);
                rendered.GetComponent<ParticleSystemRenderer>().PrepareCullingBounds();
                awake.SetActive(true);
                Frame(0f);
                Assert.False(Pending(rendered));
                Assert.False(Pending(onAwake));
            }
            finally
            {
                Object.DestroyImmediate(rendered.gameObject);
                Object.DestroyImmediate(awake);
            }
        }

        /// <summary>Bounds are the particles', the emitter only without one; zero-time updates keep
        /// the last ones (particlerender firstvis/paused/stale and shown_emit).</summary>
        [Fact]
        public void ZeroTimeFramesKeepTheLastBoundsOfTheParticles()
        {
            var ps = Make();
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                ps.Play();
                ps.Emit(new ParticleSystem.EmitParams { position = new Vector3(-60f, 0f, 0f), startSize = 0.3f, applyShapeToPosition = true }, 1);
                Frame(0.02f);
                renderer.PrepareCullingBounds();
                var far = renderer.CullingBounds;
                Assert.True(far.center.x < -50f);
                ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, startSize = 0.3f, applyShapeToPosition = true }, 1);
                Frame(0f);
                renderer.PrepareCullingBounds();
                Assert.Equal(far.center, renderer.CullingBounds.center);
                Frame(0.02f);
                renderer.PrepareCullingBounds();
                Assert.True(renderer.CullingBounds.max.x > -1f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        /// <summary>A Simulate during frames that keep bounds, and an emitter moved meanwhile: the
        /// kept bounds follow (Simulate gives new ones; a local-space system's move carries them).</summary>
        [Fact]
        public void KeptBoundsFollowASimulateAndTheEmitter()
        {
            var ps = Make();
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                ps.Play();
                ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, startSize = 0.3f, applyShapeToPosition = true }, 1);
                Frame(0.02f);
                renderer.PrepareCullingBounds();
                var before = renderer.CullingBounds.center;
                Frame(0f);
                ps.transform.position = new Vector3(10f, 0f, 0f);
                renderer.PrepareCullingBounds();
                Assert.Equal(before.x + 10f, renderer.CullingBounds.center.x, 3);
                Assert.Equal(before.y, renderer.CullingBounds.center.y, 3);
                // Simulate in a frame that keeps bounds: the next culling takes the new particles.
                ps.Emit(new ParticleSystem.EmitParams { position = new Vector3(0f, 20f, 0f), startSize = 0.3f, applyShapeToPosition = true }, 1);
                ps.Simulate(0f, true, false);
                Frame(0f);
                renderer.PrepareCullingBounds();
                Assert.True(renderer.CullingBounds.max.y > 19f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        /// <summary>Play on a playing system whose emission module is off starts it again from
        /// time 0 and keeps its particles; an emitting one plays on (particlerender
        /// firstvis/*/shown_play, cull/catchup_replay).</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PlayRestartsAPlayingSystemThatIsNotEmitting(bool emitting)
        {
            var ps = Make();
            try
            {
                var emission = ps.emission; emission.enabled = emitting; emission.rateOverTime = 0f;
                ps.Play();
                ps.Emit(1);
                Frame(0.02f);
                Frame(0.02f);
                ps.Play();
                if (emitting) Assert.True(ps.time > 0.03f);
                else Assert.Equal(0f, ps.time);
                Assert.Equal(1, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void SimulateGivesEverySimulatedSystemItsBoundsBack()
        {
            var parent = Make();
            var child = Make();
            child.transform.SetParent(parent.transform, false);
            try
            {
                parent.Play();
                child.Emit(1);
                Frame(0f);
                Assert.True(Pending(parent));
                Assert.True(Pending(child));
                parent.Simulate(0f, true, false);
                Assert.False(Pending(parent));
                Assert.False(Pending(child));
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void EmittingNothingKeepsTheRendererDrawable()
        {
            var ps = Make();
            try
            {
                ps.Play();
                Frame(0.02f);
                ps.Emit(0);
                Frame(0f);
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
