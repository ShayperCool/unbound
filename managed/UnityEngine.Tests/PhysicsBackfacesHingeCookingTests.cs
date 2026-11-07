using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unity.Collections;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Backface queries, hinge motors and limits, and mesh cooking options.
    /// Hinge and query behavior is measured by physicsccdhinge and physicsbatch in
    /// Unity 6000.0.71f1. Cooking options retain their documented, unmeasured controls.</summary>
    public class PhysicsBackfacesHingeCookingTests : IDisposable
    {
        const float Dt = 0.02f;
        // Far from the origin, away from anything other tests leave in the default physics scene.
        static readonly Vector3 Origin = new Vector3(0f, 0f, -1300f);
        const string WeldOff = "MeshColliderCookingOptions.WeldColocatedVertices (off)";
        const string WeldWithoutCleaning = "MeshColliderCookingOptions.WeldColocatedVertices (without EnableMeshCleaning)";
        const MeshColliderCookingOptions Default = (MeshColliderCookingOptions)30;

        readonly List<Object> m_Made = new List<Object>();
        readonly bool m_Backfaces, m_HitTriggers, m_AutoSync;
        readonly SimulationMode m_Mode;
        readonly Vector3 m_Gravity;

        public PhysicsBackfacesHingeCookingTests()
        {
            m_Backfaces = Physics.queriesHitBackfaces;
            m_HitTriggers = Physics.queriesHitTriggers;
            m_AutoSync = Physics.autoSyncTransforms;
            m_Mode = Physics.simulationMode;
            m_Gravity = Physics.gravity;
            Physics.queriesHitBackfaces = false;
            Physics.queriesHitTriggers = true;
            Physics.autoSyncTransforms = true;
            Physics.simulationMode = SimulationMode.Script;
        }

        public void Dispose()
        {
            foreach (var o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            Physics.Simulate(Dt);
            Physics.queriesHitBackfaces = m_Backfaces;
            Physics.queriesHitTriggers = m_HitTriggers;
            Physics.autoSyncTransforms = m_AutoSync;
            Physics.simulationMode = m_Mode;
            Physics.gravity = m_Gravity;
        }

        GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            m_Made.Add(go);
            return go;
        }

        static void Run(int steps)
        {
            for (int i = 0; i < steps; i++) Physics.Simulate(Dt);
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        // ------------------------------------------------------------------ meshes

        /// <summary>A closed cube of half size h around the origin, each triangle's edge cross
        /// product pointing out (a front face seen from outside). Triangles 10 and 11 are the +z
        /// face; 11 is the one with the (+x, -y) corner.</summary>
        static Mesh Cube(float h)
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
                v[i] = new Vector3((i & 1) != 0 ? h : -h, (i & 2) != 0 ? h : -h, (i & 4) != 0 ? h : -h);
            int[][] quads =
            {
                new[] { 0, 2, 6, 4 }, // -x
                new[] { 1, 5, 7, 3 }, // +x
                new[] { 0, 4, 5, 1 }, // -y
                new[] { 2, 3, 7, 6 }, // +y
                new[] { 0, 1, 3, 2 }, // -z
                new[] { 4, 6, 7, 5 }, // +z
            };
            var triangles = new List<int>();
            foreach (var q in quads)
            {
                Outward(v, triangles, q[0], q[1], q[2]);
                Outward(v, triangles, q[0], q[2], q[3]);
            }
            return new Mesh { vertices = v, triangles = triangles.ToArray() };
        }

        static void Outward(Vector3[] v, List<int> triangles, int a, int b, int c)
        {
            var centroid = (v[a] + v[b] + v[c]) / 3f;
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), centroid) < 0f) (b, c) = (c, b);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        MeshCollider MeshAt(string name, Vector3 position, Mesh mesh, MeshColliderCookingOptions options)
        {
            m_Made.Add(mesh);
            var go = Make(name, position);
            var collider = go.AddComponent<MeshCollider>();
            collider.cookingOptions = options;
            collider.sharedMesh = mesh;
            return collider;
        }

        // ------------------------------------------------------------------ log capture

        /// <summary>Forwards everything to the original handler, counting the warnings that name a
        /// member.</summary>
        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly string[] m_Members;
            internal readonly Dictionary<string, int> Warnings = new Dictionary<string, int>();

            internal Capture(ILogHandler inner, string[] members)
            {
                m_Inner = inner;
                m_Members = members;
                foreach (var m in members) Warnings[m] = 0;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = args == null || args.Length == 0
                    ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Warning && message != null)
                    foreach (var m in m_Members)
                        if (message.Contains(m)) Warnings[m]++;
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        static Capture Captured(Action body, params string[] members)
        {
            var logger = Debug.unityLogger;
            var handler = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var capture = new Capture(handler, members);
            logger.logHandler = capture;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                body();
            }
            finally
            {
                logger.logHandler = handler;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
            }
            return capture;
        }

        /// <summary>Stubs semantics: every use is counted; only the process's first use of a member
        /// logs, once.</summary>
        static void ReportedOnce(Capture capture, string member, int before, int uses)
        {
            Assert.True(Hits(member) >= before + uses, $"{member}: {Hits(member)} uses counted, {before} before, {uses} made");
            Assert.Equal(before == 0 ? 1 : 0, capture.Warnings[member]);
        }

        // ------------------------------------------------------------------ queriesHitBackfaces

        // Documented (6000.0 Physics.queriesHitBackfaces Remarks): "By default, all physics queries
        // such as a raycast or a shape sweep (e.g. SphereCastAll) won't detect hits with back-face
        // triangles." From inside a closed cube every face the ray and the sphere reach is a back
        // face: off, nothing is hit; on, the +z face is hit where they leave the cube. The hit
        // normal's direction is not asserted (unmeasured).
        // Red at 4d0029d5: the setting is not read, so the first Assert.True (Raycast with it on)
        // fails. Red reverting only native/unbound_physx.cpp: the native casts ignore the back-face
        // bit, same failure. Red turning only PhysicsQueries' two CastFlags(triggers) calls back into
        // HitTriggers(triggers): the bit is never sent, same failure (reverting the whole file does
        // not compile, since BatchQueryCommands uses PhysicsQueries.Backfaces).
        [Fact]
        public void CastsFromInsideAClosedMeshHitBackFacesOnlyWhenQueriesHitBackfaces()
        {
            var center = Origin;
            var cube = MeshAt("BackfaceCube", center, Cube(1f), Default);
            // Off the +z face's diagonal, so the ray crosses one triangle's interior (11).
            var from = center + new Vector3(0.3f, -0.2f, 0f);

            Physics.queriesHitBackfaces = false;
            Assert.False(Physics.Raycast(from, Vector3.forward, out _, 5f));
            Assert.Empty(Physics.RaycastAll(from, Vector3.forward, 5f));
            Assert.False(Physics.SphereCast(from, 0.1f, Vector3.forward, out _, 5f));

            Physics.queriesHitBackfaces = true;
            Assert.True(Physics.Raycast(from, Vector3.forward, out var hit, 5f));
            Assert.Same(cube, hit.collider);
            Assert.Equal(1f, hit.distance, 3);
            Assert.Equal(center.z + 1f, hit.point.z, 3);
            Assert.Equal(11, hit.triangleIndex);
            var all = Physics.RaycastAll(from, Vector3.forward, 5f);
            Assert.Single(all);
            Assert.Same(cube, all[0].collider);
            Assert.True(Physics.SphereCast(from, 0.1f, Vector3.forward, out var swept, 5f));
            Assert.Same(cube, swept.collider);
            Assert.Equal(0.9f, swept.distance, 3);

            // Back to off: the setting is read per query.
            Physics.queriesHitBackfaces = false;
            Assert.False(Physics.Raycast(from, Vector3.forward, out _, 5f));
        }

        // The back-face bit rides with the trigger bit and does not change it: a trigger between the
        // ray's start and the back face is skipped with QueryTriggerInteraction.Ignore and hit
        // first with Collide, back faces on (documented trigger interaction).
        // Red reverting only native/unbound_physx.cpp: the old native reads any non-zero flag as
        // "hit triggers", so the Ignore query reports the trigger. Red at 4d0029d5: no back face is
        // hit, so the Ignore query's Assert.True fails.
        [Fact]
        public void BackfaceBitKeepsTriggerInteraction()
        {
            var center = Origin + new Vector3(0f, 0f, 20f);
            var cube = MeshAt("TriggerCube", center, Cube(1f), Default);
            var trigger = Make("InsideTrigger", center + new Vector3(0.3f, -0.2f, 0.5f)).AddComponent<BoxCollider>();
            trigger.size = new Vector3(0.2f, 0.2f, 0.2f);
            trigger.isTrigger = true;
            var from = center + new Vector3(0.3f, -0.2f, 0f);
            Physics.queriesHitBackfaces = true;

            Assert.True(Physics.Raycast(from, Vector3.forward, out var ignored, 5f, Physics.AllLayers, QueryTriggerInteraction.Ignore));
            Assert.Same(cube, ignored.collider);
            Assert.True(Physics.Raycast(from, Vector3.forward, out var collided, 5f, Physics.AllLayers, QueryTriggerInteraction.Collide));
            Assert.Same(trigger, collided.collider);
        }

        [Fact]
        public void ColliderRaycastUsesTheGlobalBackfaceSetting()
        {
            var center = Origin + new Vector3(0f, 0f, 30f);
            var mesh = MeshAt("ColliderRaycastCube", center, Cube(1f), Default);
            var convex = MeshAt("ColliderRaycastHull", center + new Vector3(0f, 0f, 4f), Cube(1f), Default);
            convex.convex = true;
            var outside = new Ray(center + new Vector3(0.3f, -0.2f, -3f), Vector3.forward);
            var inside = new Ray(center + new Vector3(0.3f, -0.2f, 0f), Vector3.forward);
            foreach (bool backfaces in new[] { false, true })
            {
                Physics.queriesHitBackfaces = backfaces;
                Assert.True(mesh.Raycast(outside, out var front, 5f));
                Assert.Same(mesh, front.collider);
                Assert.Equal(2f, front.distance, 3);
                Assert.Equal(backfaces, mesh.Raycast(inside, out var back, 5f));
                if (backfaces)
                {
                    Assert.Same(mesh, back.collider);
                    Assert.Equal(1f, back.distance, 3);
                }
                Assert.True(convex.Raycast(new Ray(outside.origin + Vector3.forward * 4, Vector3.forward), out _, 5f));
            }
        }

        // QueryParameters.hitBackfaces (6000.0: "Whether physics queries should hit back-face
        // triangles"; QueryParameters.Default: no back faces). Unbound's reading (unmeasured): a
        // command hits back faces as its own field says, not as Physics.queriesHitBackfaces does;
        // a command asking for back faces is covered by PhysicsCastBatchQueryTests.
        // So a RaycastCommand from inside the cube with hitBackfaces false misses while the global
        // setting is on and Physics.Raycast hits.
        // Red at 4d0029d5: only through its control (the Physics.Raycast hit). Red reverting only the
        // scope line in managed/UnityEngine/Physics/BatchQueryCommands.cs (the
        // PhysicsQueries.Backfaces using): the command reads the global setting and hits the cube.
        [Fact]
        public void RaycastCommandHitsBackFacesAsItsQueryParametersSay()
        {
            var center = Origin + new Vector3(0f, 0f, 40f);
            var cube = MeshAt("CommandCube", center, Cube(1f), Default);
            var from = center + new Vector3(0.3f, -0.2f, 0f);
            Physics.queriesHitBackfaces = true;
            Assert.True(Physics.Raycast(from, Vector3.forward, out var control, 5f));
            Assert.Same(cube, control.collider);

            var parameters = new QueryParameters(Physics.AllLayers, false, QueryTriggerInteraction.UseGlobal, false);
            var commands = new NativeArray<RaycastCommand>(1, Allocator.TempJob);
            var results = new NativeArray<RaycastHit>(2, Allocator.TempJob);
            try
            {
                commands[0] = new RaycastCommand(from, Vector3.forward, parameters, 5f);
                RaycastCommand.ScheduleBatch(commands, results, 1, 1).Complete();
                Assert.Null(results[0].collider);
                RaycastCommand.ScheduleBatch(commands, results, 1, 2).Complete();
                Assert.Null(results[0].collider);
            }
            finally
            {
                commands.Dispose();
                results.Dispose();
            }
        }

        // ------------------------------------------------------------------ hinge

        (Rigidbody Body, HingeJoint Hinge) Plate(string name, Vector3 position)
        {
            var go = Make(name, position);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 0.2f, 1f);
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            var hinge = go.AddComponent<HingeJoint>();
            hinge.axis = Vector3.up;
            hinge.anchor = Vector3.zero;
            return (body, hinge);
        }

        // Documented (6000.0 HingeJoint.motor): the motor applies a force up to its maximum to reach
        // the target velocity; without free spin it therefore brakes a hinge turning faster than
        // the target. Spun at about 344 deg/s with a 90 deg/s target, the hinge settles at 90.
        // Not a red at 4d0029d5: a regression control for the motor path (green there).
        [Fact]
        public void MotorWithoutFreeSpinBrakesTowardItsTarget()
        {
            var (body, hinge) = Plate("BrakedPlate", Origin + new Vector3(0f, 0f, 60f));
            hinge.useMotor = true;
            hinge.motor = new JointMotor { targetVelocity = 90f, force = 50f, freeSpin = false };
            Run(1);
            body.angularVelocity = new Vector3(0f, 6f, 0f);
            Assert.True(hinge.velocity > 300f, $"spun to {hinge.velocity} deg/s");
            Run(10);
            Assert.InRange(hinge.velocity, 85f, 95f);
        }

        // physicsccdhinge measures both signs, a zero target and connected-body changes.
        [Theory]
        [InlineData(90f, 6f)]
        [InlineData(-90f, -6f)]
        public void FreeSpinCoastsAboveItsTargetAndBrakesAtZero(float target, float speed)
        {
            var (body, hinge) = Plate("FreeSpinMotor", Origin + new Vector3(0f, 0f, 84f));
            hinge.motor = new JointMotor { targetVelocity = target, force = 50f, freeSpin = true };
            hinge.useMotor = true;
            Run(1);
            body.angularDamping = 0;
            body.angularVelocity = Vector3.up * speed;
            Run(10);
            Assert.InRange(hinge.velocity / (speed * Mathf.Rad2Deg), .999f, 1.001f);
            hinge.motor = new JointMotor { targetVelocity = 0, force = 50f, freeSpin = true };
            Run(10);
            Assert.InRange(Mathf.Abs(hinge.velocity), 0, .01f);
        }

        // The extended solver range does not extend the public angle's wrapped range.
        [Fact]
        public void ExtendedLimitsKeepThePublicAngleWrapped()
        {
            var (_, extended) = Plate("ExtendedPlate", Origin + new Vector3(0f, 0f, 100f));
            var (_, plain) = Plate("PlainPlate", Origin + new Vector3(0f, 0f, 104f));
            foreach (var h in new[] { extended, plain })
            {
                h.useMotor = true;
                h.motor = new JointMotor { targetVelocity = 360f, force = 1000f };
            }
            extended.extendedLimits = true;
            Run(40);
            Assert.InRange(extended.angle, -180f, 0f);
            Assert.InRange(Mathf.Abs(extended.angle - plain.angle), 0f, .001f);
            Assert.InRange(plain.angle, -180f, 0f);
            Assert.InRange(extended.velocity, 350f, 370f);
        }

        // The body reaches 270 degrees and the public angle reports its equivalent -90.
        [Fact]
        public void ExtendedLimitsHoldALimitBeyond180()
        {
            var (_, hinge) = Plate("LimitedPlate", Origin + new Vector3(0f, 0f, 120f));
            hinge.useMotor = true;
            hinge.motor = new JointMotor { targetVelocity = 360f, force = 1000f };
            hinge.useLimits = true;
            hinge.limits = new JointLimits { min = -10f, max = 270f, bounceMinVelocity = 0.2f };
            hinge.extendedLimits = true;
            Run(80);
            Assert.InRange(hinge.angle, -98f, -82f);
            Assert.InRange(hinge.velocity, -10f, 10f);
        }

        // ------------------------------------------------------------------ cooking options

        /// <summary>One triangle in [0, 1] and an unused vertex at x = 10.</summary>
        static Mesh TriangleWithUnusedVertex() => new Mesh
        {
            vertices = new[] { Vector3.zero, new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(10f, 0f, 0f) },
            triangles = new[] { 0, 2, 1 },
        };

        // Documented (6000.0 Manual, Mesh Collider: Enable Mesh Cleaning "attempts to clear the mesh
        // of degenerate triangles ... and other geometrical artifacts"). Unbound's mapping
        // (unmeasured): EnableMeshCleaning off is PhysX's eDISABLE_CLEAN_MESH, which keeps the unused
        // vertex in the cooked mesh, so the collider's bounds (the cooked shape's) reach x = 10;
        // cleaned, they end at the triangle. A cookingOptions change cooks the mesh again (Unbound's
        // choice). Unity's own bounds for such a mesh are not measured.
        // Red at 4d0029d5: the options are ignored, so the uncleaned bounds still end near 1. Red
        // reverting only managed/UnityEngine/Physics/Collider.cs (native kept): same. A native-only
        // revert is not a valid configuration.
        [Fact]
        public void MeshCleaningOffKeepsWhatCleaningRemoves()
        {
            var at = Origin + new Vector3(0f, 0f, 140f);
            var collider = MeshAt("CleaningMesh", at, TriangleWithUnusedVertex(), Default);
            Run(1);
            Assert.InRange(collider.bounds.max.x - at.x, 0.9f, 2f);

            var noCleaning = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase;
            collider.cookingOptions = noCleaning;
            Run(1);
            Assert.InRange(collider.bounds.max.x - at.x, 9.9f, 10.5f);

            collider.cookingOptions = Default;
            Run(1);
            Assert.InRange(collider.bounds.max.x - at.x, 0.9f, 2f);

            var fresh = MeshAt("UncleanedMesh", at + new Vector3(0f, 0f, 4f), TriangleWithUnusedVertex(), MeshColliderCookingOptions.None);
            Run(1);
            Assert.InRange(fresh.bounds.max.x - at.x, 9.9f, 10.5f);
        }

        // Use Fast Midphase off (PhysX's BVH33, R-trees) and Cook for Faster Simulation off (the
        // midphase's fast-cooking setting) change cooking speed only (6000.0 Manual, Mesh
        // Collider): a ray hits the same triangle at the same point as with the default options,
        // with no report. Not a red at 4d0029d5: an equivalence control for the cooking path.
        [Fact]
        public void MidphaseAndCookingSpeedOptionsAnswerAsTheDefault()
        {
            var options = new[]
            {
                Default,
                MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices,
                MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.CookForFasterSimulation,
                MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase,
            };
            int weldOff = Hits(WeldOff), weldWithout = Hits(WeldWithoutCleaning);
            for (int i = 0; i < options.Length; i++)
            {
                var center = Origin + new Vector3(0f, 0f, 160f + 4f * i);
                var cube = MeshAt("OptionCube" + i, center, Cube(1f), options[i]);
                var from = center + new Vector3(0.3f, -0.2f, -3f);
                Assert.True(Physics.Raycast(from, Vector3.forward, out var hit, 5f), $"options {options[i]}");
                Assert.Same(cube, hit.collider);
                Assert.Equal(2f, hit.distance, 3);
                // The -z face's triangle with the (+x, -y) corner, as in Cube's order.
                Assert.Equal(8, hit.triangleIndex);
            }
            Assert.Equal(weldOff, Hits(WeldOff));
            Assert.Equal(weldWithout, Hits(WeldWithoutCleaning));
        }

        // The two weld/cleaning combinations PhysX's cooking cannot make are reported through
        // Stubs.Ignored, every cooking counted, one warning each for the process; the default and
        // None (no cleaning, no welding) are not reported.
        // Red at 4d0029d5: nothing is reported, so the count assertions fail.
        [Fact]
        public void UnsupportedWeldCombinationsAreReported()
        {
            int weldOff = Hits(WeldOff), weldWithout = Hits(WeldWithoutCleaning);
            var capture = Captured(() =>
            {
                MeshAt("DefaultCooking", Origin + new Vector3(0f, 0f, 200f), Cube(1f), Default);
                MeshAt("NoCooking", Origin + new Vector3(0f, 0f, 204f), Cube(1f), MeshColliderCookingOptions.None);
                Run(1);
                Assert.Equal(weldOff, Hits(WeldOff));
                Assert.Equal(weldWithout, Hits(WeldWithoutCleaning));

                MeshAt("WeldOff1", Origin + new Vector3(0f, 0f, 208f), Cube(1f), MeshColliderCookingOptions.EnableMeshCleaning);
                MeshAt("WeldOff2", Origin + new Vector3(0f, 0f, 212f), Cube(1f), MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.UseFastMidphase);
                MeshAt("WeldNoClean1", Origin + new Vector3(0f, 0f, 216f), Cube(1f), MeshColliderCookingOptions.WeldColocatedVertices);
                MeshAt("WeldNoClean2", Origin + new Vector3(0f, 0f, 220f), Cube(1f), MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase);
                Run(1);
            }, WeldOff, WeldWithoutCleaning);
            ReportedOnce(capture, WeldOff, weldOff, 2);
            ReportedOnce(capture, WeldWithoutCleaning, weldWithout, 2);
        }
    }
}
