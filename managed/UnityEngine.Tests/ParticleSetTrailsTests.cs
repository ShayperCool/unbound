using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ParticleSystem.GetTrails/SetTrails (Scripting API 6000.0): SetTrails restores the
    /// state stored by an earlier GetTrails, together with SetParticles and SetPlaybackState;
    /// Trails.capacity only reserves memory. Refusing a Trails that does not belong to the
    /// system's current particles is Unbound's choice (unmeasured). GetTrails' return value is
    /// not asserted: documented as the number of trails, a 6000.0.71f1 capture returned the
    /// storage size. These tests read only members that exist without the write-back, so each
    /// one that fails without it fails on its own assertion.</summary>
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

        /// <summary>Each trail slot's points (null for a free slot).</summary>
        static List<Vector3[]> TrailPoints(ParticleSystem ps) => ps.m_TrailList.Select(t => t?.Points.ToArray()).ToList();

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
        public void ARestoreWhileATrailFadesPutsTheFadingTrailBack()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-fading", dieWithParticles: false);
            StepUntilATrailFades(ps);
            var bakedThen = Bake(ps, scene.Camera);
            var pointsThen = TrailPoints(ps);
            var particles = Particles(ps);
            // The dead particle that carries the fading trail is exported at zero lifetime, and
            // SetParticles drops it and reorders the others.
            Assert.Contains(particles, p => p.remainingLifetime <= 0f);
            var trails = ps.GetTrails();

            Step(ps, 10);
            Assert.NotEqual(pointsThen, TrailPoints(ps));

            var errors = SetTrailsErrors(() =>
            {
                ps.SetParticles(particles);
                ps.SetTrails(trails);
            });

            Assert.Empty(errors);
            Assert.Equal(pointsThen, TrailPoints(ps));
            SameBake(bakedThen, Bake(ps, scene.Camera));
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
        public void TrailsOfAnotherSystemAreRefusedWithAnErrorAndChangeNothing()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-target");
            var other = scene.NewSystem("set-trails-other");
            Step(other, 10);
            var foreign = other.GetTrails();
            // Same particle count and trail links, so only the system they came from differs.
            Step(ps, 10);
            var points = TrailPoints(ps);

            var errors = SetTrailsErrors(() => ps.SetTrails(foreign));

            Assert.Single(errors);
            Assert.Contains("another particle system", errors[0]);
            Assert.Equal(points, TrailPoints(ps));
        }

        [Fact]
        public void TrailsCapturedWithOtherParticlesAreRefusedWithAnErrorAndChangeNothing()
        {
            using var scene = new Scene();
            var ps = scene.NewSystem("set-trails-stale");
            int countThen = ps.particleCount;
            var stale = ps.GetTrails();
            Step(ps, 15);
            Assert.NotEqual(countThen, ps.particleCount);
            var points = TrailPoints(ps);

            var errors = SetTrailsErrors(() => ps.SetTrails(stale));

            Assert.Single(errors);
            Assert.Contains("SetParticles", errors[0]);
            Assert.Equal(points, TrailPoints(ps));
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
