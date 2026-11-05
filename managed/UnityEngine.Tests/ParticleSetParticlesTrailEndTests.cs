using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ParticleSystem.SetParticles removing a particle (Scripting API 6000.0: "Setting the
    /// lifetime of a particle to a negative value will result in that particle being removed")
    /// ends its trail as TrailModule.dieWithParticles describes a death: "Specifies whether trails
    /// disappear immediately when their owning particle dies. When false, each trail persists until
    /// all its points have naturally expired, based on its lifetime." That a SetParticles removal
    /// counts as that death, and that the trail ends at the call, is Unbound's choice (unmeasured).
    /// These tests read only members that exist without the change, so each one that fails without
    /// it fails on its own assertion.</summary>
    public class ParticleSetParticlesTrailEndTests
    {
        static ParticleSystem Make(string name, bool dieWithParticles, float startLifetime = 2.013f,
            ParticleSystemRingBufferMode ring = ParticleSystemRingBufferMode.Disabled)
        {
            var go = new GameObject(name); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
            var m = ps.main; m.playOnAwake = false; m.startSpeed = 3; m.startLifetime = startLifetime; m.startSize = .3f;
            m.ringBufferMode = ring;
            var e = ps.emission; e.rateOverTime = 4;
            var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle;
            s.radius = .1f; s.arcMode = ParticleSystemShapeMultiModeValue.Loop; s.arcSpeed = .7f;
            var v = ps.velocityOverLifetime; v.enabled = true; v.orbitalZ = 1.5f;
            var t = ps.trails; t.enabled = true; t.lifetime = .5f; t.minVertexDistance = .05f; t.widthOverTrail = .5f;
            t.dieWithParticles = dieWithParticles;
            go.SetActive(true);
            Step(ps, 30, true);
            return ps;
        }

        static void Step(ParticleSystem ps, int steps, bool restartFirst = false)
        {
            for (int i = 0; i < steps; i++) ps.Simulate(1f / 30f, true, restartFirst && i == 0, false);
        }

        static ParticleSystem.Particle[] Particles(ParticleSystem ps)
        {
            var buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            int n = ps.GetParticles(buffer);
            return buffer.Take(n).ToArray();
        }

        /// <summary>Each trail slot's points (null for a free slot).</summary>
        static List<Vector3[]> TrailPoints(ParticleSystem ps) => ps.m_TrailList.Select(t => t?.Points.ToArray()).ToList();

        /// <summary>Each trail slot's fading flag (null for a free slot).</summary>
        static List<bool?> TrailOrphans(ParticleSystem ps) => ps.m_TrailList.Select(t => t == null ? (bool?)null : t.Orphan).ToList();

        static int LiveTrails(ParticleSystem ps) => ps.m_TrailList.Count(t => t != null);

        /// <summary>The index of a particle that carries a trail; every slot is live before any death.</summary>
        static int WithTrail(ParticleSystem ps, ParticleSystem.Particle[] particles)
        {
            Assert.All(ps.m_TrailList, t => Assert.NotNull(t));
            int k = System.Array.FindIndex(particles, p => p.m_TrailId != 0 && p.remainingLifetime > 0f);
            Assert.True(k >= 0);
            return k;
        }

        /// <summary>Records the SetTrails errors and forwards every call to the original handler.</summary>
        sealed class ErrorCapture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            internal readonly List<string> Errors = new List<string>();

            internal ErrorCapture(ILogHandler inner) => m_Inner = inner;

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    if (message.Contains("ParticleSystem.SetTrails")) Errors.Add(message);
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>The SetTrails errors logged while <paramref name="action"/> runs. Unbound
        /// diagnostics ("Unbound: ...") never reach Application's log callbacks, so they are read
        /// at the logger's handler, which is restored afterwards.</summary>
        static List<string> SetTrailsErrors(System.Action action)
        {
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            var capture = new ErrorCapture(previousHandler);
            logger.logHandler = capture;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try { action(); }
            finally
            {
                logger.logHandler = previousHandler;
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
            }
            return capture.Errors;
        }

        [Fact]
        public void RemovingAParticleFreesItsTrailWhenTrailsDieWithParticles()
        {
            var ps = Make("setparticles-trail-free", dieWithParticles: true);
            try
            {
                var particles = Particles(ps);
                int k = WithTrail(ps, particles);
                int link = particles[k].m_TrailId;
                int liveBefore = LiveTrails(ps);
                var pointsBefore = TrailPoints(ps);

                particles[k].remainingLifetime = 0f;
                ps.SetParticles(particles);

                Assert.Equal(particles.Length - 1, ps.particleCount);
                Assert.Null(ps.m_TrailList[link - 1]);
                Assert.Equal(liveBefore - 1, LiveTrails(ps));
                // Every other trail is as it was.
                pointsBefore[link - 1] = null;
                Assert.Equal(pointsBefore, TrailPoints(ps));
                // The freed slot is the next one a new trail takes.
                ps.Emit(1);
                Assert.Contains(Particles(ps), p => p.m_TrailId == link);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void RemovingAParticleLeavesItsTrailToFadeWhenTrailsOutliveParticles()
        {
            var ps = Make("setparticles-trail-fade", dieWithParticles: false);
            try
            {
                var particles = Particles(ps);
                int k = WithTrail(ps, particles);
                int link = particles[k].m_TrailId;
                var pointsBefore = TrailPoints(ps);

                particles[k].remainingLifetime = 0f;
                ps.SetParticles(particles);

                Assert.Equal(particles.Length - 1, ps.particleCount);
                var trail = ps.m_TrailList[link - 1];
                Assert.NotNull(trail);
                Assert.True(trail.Orphan);
                Assert.Equal(pointsBefore[link - 1], trail.Points.ToArray());

                // Its points expire within the trail lifetime (.5 x 2.013 s); with no emission,
                // nothing takes the freed slot again.
                var e = ps.emission; e.enabled = false;
                Step(ps, 45);
                Assert.Null(ps.m_TrailList[link - 1]);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void RemovingAParticleWithoutATrailChangesNoTrail()
        {
            var ps = Make("setparticles-no-trail", dieWithParticles: true);
            try
            {
                var particles = Particles(ps);
                var points = TrailPoints(ps);
                var orphans = TrailOrphans(ps);
                // A new particle carries no trail; first in the buffer, its removal swaps the last
                // particle into its place.
                var written = new[] { new ParticleSystem.Particle { startLifetime = 1f, remainingLifetime = 0f, position = Vector3.one } }
                    .Concat(particles).ToArray();
                Assert.Equal(0, written[0].m_TrailId);

                ps.SetParticles(written);

                Assert.Equal(particles.Length, ps.particleCount);
                Assert.Equal(points, TrailPoints(ps));
                Assert.Equal(orphans, TrailOrphans(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void SetTrailsAfterASetParticlesThatDropsAParticleRestoresTheCapture()
        {
            // Particles paused at the end of their life are exported at zero lifetime, so
            // SetParticles drops them, and SetTrails ends their trails (they die with them).
            var ps = Make("setparticles-trail-restore", dieWithParticles: true, startLifetime: .5f,
                ring: ParticleSystemRingBufferMode.PauseUntilReplaced);
            try
            {
                var particles = Particles(ps);
                var pointsThen = TrailPoints(ps);
                var trails = ps.GetTrails();
                var ending = particles.Where(p => p.remainingLifetime <= 0f && p.m_TrailId != 0).Select(p => p.m_TrailId).ToList();
                Assert.NotEmpty(ending);
                int kept = particles.Count(p => p.remainingLifetime > 0f);

                Step(ps, 10);
                Assert.NotEqual(pointsThen, TrailPoints(ps));

                var errors = SetTrailsErrors(() =>
                {
                    ps.SetParticles(particles);
                    ps.SetTrails(trails);
                });

                Assert.Empty(errors);
                Assert.Equal(kept, ps.particleCount);
                var expected = new List<Vector3[]>(pointsThen);
                foreach (int link in ending) expected[link - 1] = null;
                Assert.Equal(expected, TrailPoints(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
