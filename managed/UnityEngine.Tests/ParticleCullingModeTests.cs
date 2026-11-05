using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The frame's particle update (ParticleSystemManager.UpdateAll) steps every
    /// registered system, and these tests set the global clock and renderer visibility, so they run
    /// alone.</summary>
    [CollectionDefinition("Particle culling frames", DisableParallelization = true)]
    public sealed class ParticleCullingFramesCollection
    {
    }

    /// <summary>
    /// MainModule.cullingMode in the frame's particle update. Documented (6000.0 Scripting API
    /// ParticleSystemCullingMode, Manual "Main module" Culling Mode): Pause "stops simulating while
    /// offscreen"; PauseAndCatchup also stops, then "when re-entering the view, the simulation
    /// performs a large step to reach the point where it would have been had it not paused";
    /// AlwaysSimulate simulates "regardless of whether it is on screen or not"; Automatic: "Looping
    /// systems use Pause, and all other system use Always Simulate". Assertions on the play state
    /// while culled, on the looping Automatic case (kept simulating, reported) and on the
    /// visibility read from the renderer are checks of Unbound's choices, which are unmeasured, not
    /// Unity guarantees.
    /// <para>Visibility is the system's ParticleSystemRenderer.isVisible: set directly here, or
    /// through the engine's camera culling (FrameBuilder.CullCamera) in the bounds test. The systems
    /// sit far from every other test's camera, on their own layer.</para>
    /// </summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleCullingModeTests : IDisposable
    {
        const float Dt = 0.02f;
        const int kLayer = 30;
        static readonly Vector3 kFar = new Vector3(-2000f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly float m_MaximumParticleDeltaTime = Time.s_MaximumParticleDeltaTime;
        readonly int m_FrameCount = Time.s_FrameCount;
        readonly List<Object> m_Objects = new List<Object>();

        public ParticleCullingModeTests()
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
            Time.s_FrameCount = m_FrameCount;
        }

        /// <summary>A playing system 10 m in front of <see cref="kFar"/> emitting 10 particles a
        /// second that live 5 s and stay where they are born, in a 10 s loop (or one shot).</summary>
        ParticleSystem Make(ParticleSystemCullingMode mode, bool loop = true)
        {
            var go = new GameObject("culled");
            m_Objects.Add(go);
            go.SetActive(false);
            go.layer = kLayer;
            go.transform.position = kFar + new Vector3(0f, 0f, 10f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.cullingMode = mode;
            main.loop = loop;
            main.duration = 10f;
            main.startLifetime = 5f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.gravityModifier = 0f;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.enabled = false;
            go.SetActive(true);
            Assert.True(ps.isPlaying);
            return ps;
        }

        static ParticleSystemRenderer RendererOf(ParticleSystem ps) => ps.GetComponent<ParticleSystemRenderer>();

        static void SetVisible(ParticleSystem ps, bool visible) => RendererOf(ps).SetVisible(visible);

        static void Frames(int count)
        {
            for (int i = 0; i < count; i++) ParticleSystemManager.UpdateAll();
        }

        static ParticleSystem.Particle[] ParticlesOf(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles;
        }

        static void Near(float expected, float actual, float tolerance = 1e-4f) =>
            Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected:R}, got {actual:R}");

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        [Fact]
        public void PauseHoldsAnOffscreenLoopingSystemAndResumesFromTheSameParticles()
        {
            var ps = Make(ParticleSystemCullingMode.Pause);
            SetVisible(ps, true);
            Frames(15);
            float time = ps.time;
            var before = ParticlesOf(ps);
            Assert.NotEmpty(before);

            SetVisible(ps, false);
            Frames(25);
            Assert.Equal(time, ps.time);
            var held = ParticlesOf(ps);
            Assert.Equal(before.Length, held.Length);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[i].remainingLifetime, held[i].remainingLifetime);
                Assert.Equal(before[i].position, held[i].position);
            }
            // Culling is not a script Pause (Unbound's choice, unmeasured).
            Assert.True(ps.isPlaying);
            Assert.False(ps.isPaused);

            // Visible again: the next frame continues from the held state by its own time only.
            SetVisible(ps, true);
            Frames(1);
            Near(time + Dt, ps.time);
            var resumed = ParticlesOf(ps);
            Assert.True(resumed.Length >= before.Length);
            for (int i = 0; i < before.Length; i++)
                Near(before[i].remainingLifetime - Dt, resumed[i].remainingLifetime);
        }

        [Fact]
        public void PauseAndCatchupSimulatesTheMissedTimeWhenVisibleAgain()
        {
            var ps = Make(ParticleSystemCullingMode.PauseAndCatchup);
            var always = Make(ParticleSystemCullingMode.AlwaysSimulate);
            SetVisible(ps, true);
            Frames(15);
            float time = ps.time;
            var before = ParticlesOf(ps);
            Assert.NotEmpty(before);

            SetVisible(ps, false);
            Frames(25);
            Assert.Equal(time, ps.time);
            Assert.Equal(before.Length, ps.particleCount);

            // The first visible frame reaches where it would have been: the 25 missed frames and
            // its own, as an always simulated twin is.
            SetVisible(ps, true);
            Frames(1);
            Near(time + 26 * Dt, ps.time);
            Near(always.time, ps.time);
            var caughtUp = ParticlesOf(ps);
            for (int i = 0; i < before.Length; i++)
                Near(before[i].remainingLifetime - 26 * Dt, caughtUp[i].remainingLifetime);
            Assert.True(caughtUp.Length > before.Length);
            Assert.InRange(caughtUp.Length, always.particleCount - 1, always.particleCount + 1);
        }

        [Fact]
        public void AlwaysSimulateAdvancesOffscreen()
        {
            var ps = Make(ParticleSystemCullingMode.AlwaysSimulate);
            SetVisible(ps, false);
            Frames(10);
            Near(10 * Dt, ps.time);
            Assert.True(ps.particleCount > 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AutomaticKeepsSimulatingOffscreen(bool loop)
        {
            const string automatic = "ParticleSystem.main.cullingMode Automatic";
            var ps = Make(ParticleSystemCullingMode.Automatic, loop);
            SetVisible(ps, false);
            int reported = Hits(automatic);
            Frames(10);
            // One-shot: documented as Always Simulate. Looping: documented as Pause, but also
            // documented to keep updating when not procedural, which Unbound does not decide, so it
            // keeps simulating and is reported (Unbound's choice, unmeasured).
            Near(10 * Dt, ps.time);
            Assert.True(ps.particleCount > 0);
            // (The count is process-wide, so the one-shot case asserts no absence of reports.)
            if (loop) Assert.True(Hits(automatic) > reported);
        }

        [Fact]
        public void VisibleSystemSimulatesAsAlwaysSimulate()
        {
            var ps = Make(ParticleSystemCullingMode.Pause);
            var always = Make(ParticleSystemCullingMode.AlwaysSimulate);
            SetVisible(ps, true);
            Frames(20);
            Assert.Equal(always.time, ps.time);
            var particles = ParticlesOf(ps);
            var reference = ParticlesOf(always);
            Assert.Equal(reference.Length, particles.Length);
            for (int i = 0; i < particles.Length; i++)
                Assert.Equal(reference[i].remainingLifetime, particles[i].remainingLifetime);
        }

        [Fact]
        public void PausedSystemBecomesVisibleAgainByItsHeldBounds()
        {
            var camera = new GameObject("CullingCamera").AddComponent<Camera>();
            m_Objects.Add(camera.gameObject);
            camera.transform.position = kFar;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100;
            camera.aspect = 16f / 9f;
            camera.cullingMask = 1 << kLayer;
            var ps = Make(ParticleSystemCullingMode.Pause);
            var renderer = RendererOf(ps);

            FrameBuilder.CullCamera(camera);
            Assert.True(renderer.isVisible);
            Frames(10);
            Assert.True(ps.particleCount > 0);

            // The camera turns away: no culling takes the renderer, which the next frame's
            // EarlyUpdate makes invisible, and the system holds.
            camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            FrameBuilder.CullCamera(camera);
            Time.s_FrameCount += 2;
            Renderer.NotifyInvisible();
            Assert.False(renderer.isVisible);
            float time = ps.time;
            int count = ps.particleCount;
            Frames(10);
            Assert.Equal(time, ps.time);
            Assert.Equal(count, ps.particleCount);

            // Turned back, the camera sees the held particles' bounds, and the system resumes.
            camera.transform.rotation = Quaternion.identity;
            FrameBuilder.CullCamera(camera);
            Assert.True(renderer.isVisible);
            Frames(1);
            Near(time + Dt, ps.time);
        }

        /// <summary>A camera at <see cref="kFar"/> looking along +z at the test layer only.</summary>
        Camera MakeCamera(bool orthographic)
        {
            var camera = new GameObject("CullingCamera").AddComponent<Camera>();
            m_Objects.Add(camera.gameObject);
            camera.transform.position = kFar;
            camera.orthographic = orthographic;
            camera.orthographicSize = 5f;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100;
            camera.aspect = 16f / 9f;
            camera.cullingMask = 1 << kLayer;
            return camera;
        }

        /// <summary>Whether a vertex of the geometry the renderer draws for the camera (baked in
        /// world space) is inside the camera's view frustum.</summary>
        bool DrawsInView(ParticleSystemRenderer renderer, Camera camera)
        {
            var mesh = new Mesh();
            m_Objects.Add(mesh);
            renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.BakeRotationAndScale | ParticleSystemBakeMeshOptions.BakePosition);
            var viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            foreach (var v in mesh.vertices)
            {
                var c = viewProjection * new Vector4(v.x, v.y, v.z, 1f);
                if (c.w > 0f && Math.Abs(c.x) <= c.w && Math.Abs(c.y) <= c.w && Math.Abs(c.z) <= c.w) return true;
            }
            return false;
        }

        /// <summary>The camera culls the renderer by its bounds (the live particles' centres ±
        /// size/2), which miss the view, and the next frame's EarlyUpdate makes it invisible.</summary>
        static void CullOut(Camera camera, ParticleSystemRenderer renderer)
        {
            FrameBuilder.CullCamera(camera);
            Time.s_FrameCount += 2;
            Renderer.NotifyInvisible();
            Assert.False(renderer.isVisible);
        }

        [Fact]
        public void PauseKeepsSimulatingStretchedQuadsInViewWithTheirCentresOutside()
        {
            const string notCovered = "Particle culling without covering bounds";
            var camera = MakeCamera(orthographic: false);
            var ps = Make(ParticleSystemCullingMode.Pause);
            // Above the view (its top is at y 5.77 at 10 m), each quad stretched 10 m down into it.
            ps.transform.position = kFar + new Vector3(0f, 8f, 10f);
            var renderer = RendererOf(ps);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 10f;
            SetVisible(ps, true);
            Frames(10);
            Assert.True(ps.particleCount > 0);
            Assert.True(DrawsInView(renderer, camera));

            CullOut(camera, renderer);
            int reported = Hits(notCovered);
            float time = ps.time;
            Frames(10);
            // Drawn on screen, so not frozen there: the bounds Unbound culls by do not cover a
            // stretched quad, and the system keeps simulating, reported (Unbound's choice,
            // unmeasured).
            Near(time + 10 * Dt, ps.time);
            Assert.True(Hits(notCovered) > reported);
        }

        [Fact]
        public void PauseKeepsSimulatingARotatedBillboardCornerInView()
        {
            var camera = MakeCamera(orthographic: true);
            var ps = Make(ParticleSystemCullingMode.Pause);
            var main = ps.main;
            main.startRotation = 45f * Mathf.Deg2Rad;
            // Centres 0.6 m above the view's top (y 5): centre ± 0.5 misses it, but the turned
            // 1 m quad's lower corner reaches 0.707 m down, into the view.
            ps.transform.position = kFar + new Vector3(0f, 5.6f, 10f);
            var renderer = RendererOf(ps);
            SetVisible(ps, true);
            Frames(10);
            Assert.True(ps.particleCount > 0);
            Assert.True(DrawsInView(renderer, camera));

            CullOut(camera, renderer);
            float time = ps.time;
            Frames(10);
            // The update's own test with centres ± size·√3/2 sees it (Unbound's choice, unmeasured).
            Near(time + 10 * Dt, ps.time);
        }

        // Implementation invariant (an unmeasured inference, not a Native observation): a new
        // playback drops the time missed by an earlier one, even when both were culled from total
        // time 0 and the restart (Stop(StopEmittingAndClear) + Play in one frame, fixed seed)
        // leaves the same time and random state the culled frames saw.
        [Fact]
        public void PauseAndCatchupDropsTheMissedTimeOfAnEarlierPlayback()
        {
            var ps = Make(ParticleSystemCullingMode.PauseAndCatchup, loop: false);
            // Duration and seed can only change on a stopped, cleared system.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 12345u;
            ps.Play();
            // Never seen: culled from total time 0.
            Frames(40);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
            Frames(25);
            SetVisible(ps, true);
            Frames(1);
            // Only the new playback's 25 missed frames and the visible one (Unbound's choice,
            // unmeasured: a new playback drops the time missed before it).
            Near(26 * Dt, ps.time);
        }

        // Implementation invariant (an unmeasured inference, not a Native observation):
        // Stop(StopEmittingAndClear) drops the time a culled playback missed, so particles a
        // script emits right after it (same frame, fixed seed) are not stepped by time that passed
        // before they existed. Plain Clear() followed by Emit is not covered (open, unmeasured).
        [Fact]
        public void PauseAndCatchupStopAndClearDropsTheMissedTimeBeforeAnEmit()
        {
            var ps = Make(ParticleSystemCullingMode.PauseAndCatchup, loop: false);
            // The seed can only change on a stopped, cleared system.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 12345u;
            ps.Play();
            // Never seen: culled from total time 0, with 40 frames of missed time pending.
            Frames(40);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Emit(3);
            SetVisible(ps, true);
            Frames(1);
            // Only the visible frame's own time ages the emitted particles (5 s lifetime).
            Assert.Equal(3, ps.particleCount);
            foreach (var p in ParticlesOf(ps))
                Near(5f - Dt, p.remainingLifetime);
            Near(Dt, ps.time);
        }

        // Implementation invariant (an unmeasured inference, not a Native observation): the
        // restart reset on its own. A plain Stop() (StopEmitting) with nothing alive stops a culled
        // system without the clearing branch, so only the new playback (Play -> Restart, fixed
        // seed) can drop the earlier playback's missed time. Expected 26 * Dt = 0.52: the new
        // playback's 25 missed frames and the visible one.
        [Fact]
        public void PauseAndCatchupRestartAfterAPlainStopDropsTheMissedTime()
        {
            var ps = Make(ParticleSystemCullingMode.PauseAndCatchup, loop: false);
            // Nothing is alive yet: a plain Stop() stops it, and the duration and seed can change.
            Assert.Equal(0, ps.particleCount);
            ps.Stop();
            Assert.True(ps.isStopped);
            var main = ps.main;
            main.duration = 1f;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 12345u;
            ps.Play();
            // Never seen: culled from total time 0, nothing emitted, 40 frames of missed time pending.
            Frames(40);
            Assert.Equal(0, ps.particleCount);
            ps.Stop();
            // Stopped without clearing, so the next Play begins a new playback.
            Assert.True(ps.isStopped);
            ps.Play();
            Assert.True(ps.isPlaying);
            Frames(25);
            SetVisible(ps, true);
            Frames(1);
            Near(26 * Dt, ps.time);
        }
    }
}
