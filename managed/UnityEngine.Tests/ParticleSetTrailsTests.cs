using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ParticleSystem.GetTrails/SetTrails (Scripting API 6000.0): SetTrails restores the
    /// state stored by an earlier GetTrails, together with SetParticles and SetPlaybackState.
    /// Measured in 6000.0.71f1 (tests/projects/particletrails api/*): trails are restored by
    /// particle index, also into another system; GetTrails returns the trail storage size
    /// (particle slots, at least 32, times points per trail, at least 4, both powers of two,
    /// never shrinking), which also becomes the Trails' capacity when that is smaller. Refusing a
    /// Trails that GetTrails never filled is Unbound's choice (unmeasured).</summary>
    public class ParticleSetTrailsTests
    {
        static ParticleSystem Make(string name, bool dieWithParticles)
        {
            var go = new GameObject(name); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
            var m = ps.main; m.playOnAwake = false; m.startSpeed = 3; m.startLifetime = 2.013f; m.startSize = .3f;
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

        sealed class Scene : System.IDisposable
        {
            readonly List<Object> m_Objects = new List<Object>();
            internal readonly Camera Camera;

            internal Scene()
            {
                var cameraObject = Track(new GameObject("set-trails-camera"));
                Camera = cameraObject.AddComponent<Camera>();
                Camera.transform.position = new Vector3(0, 0, -6); Camera.fieldOfView = 40; Camera.aspect = 1;
            }

            internal ParticleSystem NewSystem(string name, bool dieWithParticles = true) =>
                Track(Make(name, dieWithParticles).gameObject).GetComponent<ParticleSystem>();

            T Track<T>(T o) where T : Object { m_Objects.Add(o); return o; }

            public void Dispose() { foreach (var o in m_Objects) Object.DestroyImmediate(o); }
        }

        /// <summary>The baked trail mesh: every vertex, uv, color and index.</summary>
        static (Vector3[] Vertices, Vector2[] Uv, Color32[] Colors, int[] Indices) Bake(ParticleSystem ps, Camera camera)
        {
            var mesh = new Mesh();
            try
            {
                ps.GetComponent<ParticleSystemRenderer>().BakeTrailsMesh(mesh, camera,
                    ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                return (mesh.vertices, mesh.uv, mesh.colors32, mesh.triangles);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        static void SameBake((Vector3[] Vertices, Vector2[] Uv, Color32[] Colors, int[] Indices) expected,
            (Vector3[] Vertices, Vector2[] Uv, Color32[] Colors, int[] Indices) actual)
        {
            Assert.Equal(expected.Vertices, actual.Vertices);
            Assert.Equal(expected.Uv, actual.Uv);
            Assert.Equal(expected.Colors, actual.Colors);
            Assert.Equal(expected.Indices, actual.Indices);
        }

        /// <summary>Each particle's trail points, in particle order (null for a particle without one).</summary>
        static List<Vector3[]> TrailPoints(ParticleSystem ps) => Enumerable.Range(0, ps.m_Count)
            .Select(i => ps.m_Particles[i].m_TrailId > 0 ? ps.m_TrailList[ps.m_Particles[i].m_TrailId - 1]?.Points.ToArray() : null).ToList();

        static ParticleSystem.Particle[] Particles(ParticleSystem ps)
        {
            var buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            int n = ps.GetParticles(buffer);
            return buffer.Take(n).ToArray();
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

        /// <summary>Steps until a particle has died and left its trail fading.</summary>
        static void StepUntilATrailFades(ParticleSystem ps)
        {
            for (int i = 0; i < 90 && !ps.m_TrailList.Any(t => t != null && t.Orphan); i++) Step(ps, 1);
            Assert.Contains(ps.m_TrailList, t => t != null && t.Orphan);
        }

        [Fact]
        public void RoundTripLeavesTheBakedTrailsAndLaterSimulationUnchanged()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-round-trip");
            var twin = scene.NewSystem("set-trails-round-trip-twin");
            var before = Bake(ps, scene.Camera);
            SameBake(before, Bake(twin, scene.Camera));

            var errors = SetTrailsErrors(() =>
            {
                ps.SetTrails(ps.GetTrails());
                var trails = new ParticleSystem.Trails();
                ps.GetTrails(ref trails);
                ps.SetTrails(trails);
            });

            Assert.Empty(errors);
            SameBake(before, Bake(ps, scene.Camera));
            Step(ps, 20); Step(twin, 20);
            SameBake(Bake(twin, scene.Camera), Bake(ps, scene.Camera));
            Assert.Equal(TrailPoints(twin), TrailPoints(ps));
        }

        [Fact]
        public void SetTrailsOfAnEarlierCaptureRestoresThoseTrails()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-restore");
            var bakedThen = Bake(ps, scene.Camera);
            var pointsThen = TrailPoints(ps);
            var particles = Particles(ps);
            var trails = ps.GetTrails();

            Step(ps, 15);
            Assert.NotEqual(pointsThen, TrailPoints(ps));
            Assert.NotEqual(bakedThen.Vertices, Bake(ps, scene.Camera).Vertices);

            ps.SetParticles(particles);
            ps.SetTrails(trails);
            Assert.Equal(pointsThen, TrailPoints(ps));
            SameBake(bakedThen, Bake(ps, scene.Camera));

            // The capture is applied as a copy: it restores the same trails again later.
            Step(ps, 15);
            ps.SetParticles(particles);
            ps.SetTrails(trails);
            Assert.Equal(pointsThen, TrailPoints(ps));
            SameBake(bakedThen, Bake(ps, scene.Camera));
        }

        [Fact]
        public void ARestoreAfterSetParticlesDroppedCarriersGivesEachIndexTheTrailCapturedThere()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-fading", dieWithParticles: false);
            StepUntilATrailFades(ps);
            var pointsThen = TrailPoints(ps);
            var particles = Particles(ps);
            // The dead particle that carries the fading trail is exported at zero lifetime, and
            // SetParticles drops it (measured): the particles after it move down an index.
            Assert.Contains(particles, p => p.IsTrailCarrier);
            var trails = ps.GetTrails();

            Step(ps, 10);
            var errors = SetTrailsErrors(() =>
            {
                ps.SetParticles(particles);
                ps.SetTrails(trails);
            });

            Assert.Empty(errors);
            Assert.True(ps.particleCount < particles.Length);
            Assert.Equal(pointsThen.Take(ps.particleCount), TrailPoints(ps));
        }

        [Fact]
        public void ARoundTripWhileATrailFadesIsAcceptedAndChangesNothing()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-fading-round-trip", dieWithParticles: false);
            StepUntilATrailFades(ps);
            var baked = Bake(ps, scene.Camera);
            var points = TrailPoints(ps);

            // The carrier of the fading trail is still there: GetTrails then SetTrails, without
            // SetParticles, is accepted.
            var errors = SetTrailsErrors(() => ps.SetTrails(ps.GetTrails()));

            Assert.Empty(errors);
            Assert.Equal(points, TrailPoints(ps));
            SameBake(baked, Bake(ps, scene.Camera));
        }

        [Fact]
        public void TrailsOfAnotherSystemAreRestoredByParticleIndex()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-target");
            var other = scene.NewSystem("set-trails-other");
            Step(other, 10);
            var foreign = other.GetTrails();
            var foreignPoints = TrailPoints(other);
            Step(ps, 4);
            Assert.NotEqual(foreignPoints.Take(ps.particleCount), TrailPoints(ps));

            var errors = SetTrailsErrors(() => ps.SetTrails(foreign));

            Assert.Empty(errors);
            int n = System.Math.Min(ps.particleCount, foreignPoints.Count);
            Assert.True(n > 0);
            Assert.Equal(foreignPoints.Take(n), TrailPoints(ps).Take(n));
        }

        [Fact]
        public void StaleTrailsGoToTheCurrentParticlesByIndex()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-stale");
            var pointsThen = TrailPoints(ps);
            var stale = ps.GetTrails();
            Step(ps, 15);
            var after = TrailPoints(ps);

            var errors = SetTrailsErrors(() => ps.SetTrails(stale));

            Assert.Empty(errors);
            var restored = TrailPoints(ps);
            for (int i = 0; i < restored.Count; i++)
                Assert.Equal(i < pointsThen.Count ? pointsThen[i] : after[i], restored[i]);
        }

        [Fact]
        public void SetParticlesLeavesEachTrailWithItsIndex()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-particles-trail-slots");
            var points = TrailPoints(ps);
            var particles = Particles(ps);
            Assert.True(particles.Length >= 3);
            // The same particles written in reverse: the trails stay where they were.
            System.Array.Reverse(particles);
            ps.SetParticles(particles);
            Assert.Equal(points, TrailPoints(ps));
            // A particle written past the live ones is new and starts an empty trail.
            var more = Particles(ps).Concat(new[] { particles[0] }).ToArray();
            ps.SetParticles(more);
            var now = TrailPoints(ps);
            Assert.Equal(points, now.Take(points.Count));
            Assert.Empty(now[points.Count]);
        }

        [Fact]
        public void GetTrailsReturnsTheStorageSizeThatOnlyGrows()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("get-trails-storage");
            var trails = new ParticleSystem.Trails();
            int size = ps.GetTrails(ref trails);
            // The longest trail so far, not only now, sizes the storage.
            int longest = ps.m_TrailList.Where(t => t != null).Max(t => t.Points.Count);
            Assert.True(ps.m_TrailStoragePoints >= longest);
            int slots = System.Math.Max(32, NextPowerOfTwo(ps.particleCount));
            Assert.Equal(slots * System.Math.Max(4, NextPowerOfTwo(ps.m_TrailStoragePoints)), size);
            Assert.Equal(size, trails.capacity);
            // A larger presized capacity stays.
            var presized = new ParticleSystem.Trails { capacity = size * 2 + 1 };
            Assert.Equal(size, ps.GetTrails(ref presized));
            Assert.Equal(size * 2 + 1, presized.capacity);
            // Clearing the particles keeps the storage.
            ps.Clear();
            Assert.Equal(size, ps.GetTrails(ref trails));
            // Ribbon trails keep none.
            var t = ps.trails; t.mode = ParticleSystemTrailMode.Ribbon;
            Assert.Equal(0, ps.GetTrails(ref trails));
        }

        static int NextPowerOfTwo(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        [Fact]
        public void ATrailsThatGetTrailsNeverFilledIsRefusedWithAnErrorAndChangesNothing()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-default");
            var points = TrailPoints(ps);

            var errors = SetTrailsErrors(() => ps.SetTrails(new ParticleSystem.Trails()));

            Assert.Single(errors);
            Assert.Equal(points, TrailPoints(ps));
        }

        [Fact]
        public void APresizedTrailsThatGetTrailsNeverFilledIsRefusedWithAnErrorAndChangesNothing()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-presized");
            var points = TrailPoints(ps);
            var reserved = new ParticleSystem.Trails { capacity = 64 };

            var errors = SetTrailsErrors(() => ps.SetTrails(reserved));

            Assert.Single(errors);
            Assert.Equal(points, TrailPoints(ps));
        }

        [Fact]
        public void CapacityOnlyReservesAndAPresizedTrailsIsRefilledInPlace()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-capacity");
            var trails = new ParticleSystem.Trails { capacity = 1 };
            ps.GetTrails(ref trails);
            Assert.True(trails.capacity >= 1);

            // A presized Trails passed again holds the newer state, whatever its capacity.
            Step(ps, 10);
            var particles = Particles(ps);
            var pointsThen = TrailPoints(ps);
            var bakedThen = Bake(ps, scene.Camera);
            ps.GetTrails(ref trails);
            Assert.True(trails.capacity >= 1);

            Step(ps, 10);
            Assert.NotEqual(pointsThen, TrailPoints(ps));
            ps.SetParticles(particles);
            ps.SetTrails(trails);
            Assert.Equal(pointsThen, TrailPoints(ps));
            SameBake(bakedThen, Bake(ps, scene.Camera));
        }
    }
}
