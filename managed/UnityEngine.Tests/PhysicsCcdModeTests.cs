using System;
using System.Collections.Generic;
using Xunit;
using UnityEngine.SceneManagement;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Rigidbody.collisionDetectionMode per mode (Unity 6000.0 Scripting API: CollisionDetectionMode,
    /// its four values and Rigidbody.collisionDetectionMode; Manual: Rigidbody component reference,
    /// "Continuous collision detection (CCD)", "Sweep-based CCD", "Speculative CCD").
    /// Documented and asserted:
    /// - Discrete: CCD is off; collisions are only checked at the fixed time step.
    /// - Continuous: sweep CCD against static colliders (colliders without a Rigidbody) only; the
    ///   Manual lists collisions with moving colliders as what it is not for.
    /// - ContinuousDynamic: the 6000.0.71f1 physicsccdhinge capture also measures Discrete
    ///   dynamic targets, using their end-of-step pose. A Continuous body also stops a
    ///   ContinuousDynamic one (Scripting API, CollisionDetectionMode.Continuous).
    /// - ContinuousSpeculative: speculative CCD against static and dynamic geometry, usable on
    ///   kinematic bodies too.
    /// Outcomes are asserted as tunnelled or not, with generous margins, in deterministic
    /// fixed-step scenes whose step moves the fast body far past a thin obstacle: no contact exists
    /// at either end of the step, so only CCD can stop it.
    /// Not asserted: what a kinematic body in Continuous or ContinuousDynamic does itself (not
    /// documented; Unbound gives it no sweep, unmeasured), positions, impulses and event order.
    /// physicsccdhinge measures the static/kinematic target controls and per-step callbacks in
    /// Unity 6000.0.71f1, including dynamic pairs and mesh/compound geometry controls.
    ///
    /// Red base 4d0029d5: the file compiles there (it uses only existing public API). There every
    /// mode other than Discrete raised sweep CCD on the body and every pair with a moving body was
    /// swept, so the "tunnels" rows of the moving-obstacle cases and the kinematic speculative row
    /// fail there; each test names its own red.
    /// </summary>
    public class PhysicsCcdModeTests : IDisposable
    {
        const float Dt = 0.02f;
        // 100 m/s: 2 m per step, far more than the sphere's diameter and the obstacle's thickness.
        const float Speed = 100f;
        // Far from the origin, away from anything other tests leave in the default physics scene.
        static readonly Vector3 Origin = new Vector3(0f, 0f, 800f);
        static int s_Lane;

        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Mesh> m_Meshes = new List<Mesh>();
        readonly Vector3 m_Gravity;
        readonly SimulationMode m_Mode;
        readonly bool m_AutoSync;
        readonly string m_Release;
        readonly Scene m_PreviousScene, m_Scene;
        readonly PhysicsScene m_Space;

        public PhysicsCcdModeTests()
        {
            m_Gravity = Physics.gravity;
            m_Mode = Physics.simulationMode;
            m_AutoSync = Physics.autoSyncTransforms;
            Physics.gravity = Vector3.zero;
            Physics.autoSyncTransforms = true;
            Physics.simulationMode = SimulationMode.Script;
            m_Release = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            m_PreviousScene = SceneManager.GetActiveScene();
            m_Scene = SceneManager.CreateScene("ccd-test-" + s_Lane++, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(m_Scene);
            m_Space = m_Scene.GetPhysicsScene();
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var mesh in m_Meshes) Object.DestroyImmediate(mesh);
            m_Space.Simulate(Dt);
            SceneManager.SetActiveScene(m_PreviousScene);
            SceneManager.UnloadSceneAsync(m_Scene);
            SceneManagerInternal.ProcessPending();
            ProjectInfo.UnityVersion = m_Release;
            Physics.gravity = m_Gravity;
            Physics.autoSyncTransforms = m_AutoSync;
            Physics.simulationMode = m_Mode;
        }

        /// <summary>A fresh lane, 10 m away from every other one.</summary>
        static Vector3 Lane() => Origin + new Vector3(0f, 10f * s_Lane++, 0f);

        GameObject Make(string name, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            m_Made.Add(go);
            return go;
        }

        /// <summary>A sphere of radius 0.1 one metre before the obstacle, flying at it.</summary>
        Rigidbody Bullet(Vector3 lane, CollisionDetectionMode mode)
        {
            var go = Make("bullet", PrimitiveType.Sphere, lane + new Vector3(-1f, 0f, 0f), Vector3.one * 0.2f);
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 1f;
            rb.collisionDetectionMode = mode;
            rb.linearVelocity = new Vector3(Speed, 0f, 0f);
            return rb;
        }

        /// <summary>A 0.1 m thin, 2 m wide plate across the lane at x 0 (a static collider).</summary>
        GameObject Wall(Vector3 lane) => Make("wall", PrimitiveType.Cube, lane, new Vector3(0.1f, 2f, 2f));

        /// <summary>The same plate as a heavy dynamic body, so a hit barely moves it.</summary>
        Rigidbody Plate(Vector3 lane, CollisionDetectionMode mode)
        {
            var rb = Wall(lane).AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 1000f;
            rb.collisionDetectionMode = mode;
            return rb;
        }

        void Run(int steps)
        {
            for (int i = 0; i < steps; i++) m_Space.Simulate(Dt);
        }

        /// <summary>Tunnelled: the sphere's centre ends at least 0.5 m past the obstacle's centre
        /// (untouched it is about 5 m past after three steps; stopped it stays behind, below 0).</summary>
        static bool Tunnelled(Rigidbody bullet, Vector3 obstacle) => bullet.position.x - obstacle.x > 0.5f;

        static bool Stopped(Rigidbody bullet, Vector3 obstacle) => bullet.position.x < obstacle.x;

        // Red: none at 4d0029d5 (it passes there: Discrete had no CCD, the others swept). Guard;
        // head-only reds: in native apply_ccd drop eENABLE_SPECULATIVE_CCD (the Speculative row
        // tunnels), or make sweeps() false against statics (the Continuous rows tunnel).
        [Theory]
        [InlineData(CollisionDetectionMode.Discrete, true)]
        [InlineData(CollisionDetectionMode.Continuous, false)]
        [InlineData(CollisionDetectionMode.ContinuousDynamic, false)]
        [InlineData(CollisionDetectionMode.ContinuousSpeculative, false)]
        public void FastSphereAgainstThinStaticWallTunnelsOnlyWhenDiscrete(CollisionDetectionMode mode, bool tunnels)
        {
            var lane = Lane();
            Wall(lane);
            var bullet = Bullet(lane, mode);
            Run(3);
            Assert.Equal(tunnels, Tunnelled(bullet, lane));
            Assert.Equal(!tunnels, Stopped(bullet, lane));
        }

        [Theory]
        [InlineData(CollisionDetectionMode.Discrete, true)]
        [InlineData(CollisionDetectionMode.Continuous, true)]
        [InlineData(CollisionDetectionMode.ContinuousDynamic, false)]
        [InlineData(CollisionDetectionMode.ContinuousSpeculative, false)]
        public void FastSphereAgainstThinKinematicWall(CollisionDetectionMode mode, bool tunnels)
        {
            var lane = Lane();
            Wall(lane).AddComponent<Rigidbody>().isKinematic = true;
            var bullet = Bullet(lane, mode);
            Run(3);
            Assert.Equal(tunnels, Tunnelled(bullet, lane));
        }

        public class StayRecorder : MonoBehaviour
        {
            internal Vector3 Relative;
            internal int Count;
            void OnCollisionStay(Collision collision) { Relative = collision.relativeVelocity; Count++; }
        }

        [Fact]
        public void SpeculativeStayReportsVelocityEnteringTheStep()
        {
            var lane = Lane();
            Wall(lane);
            var bullet = Bullet(lane, CollisionDetectionMode.ContinuousSpeculative);
            var recorder = bullet.gameObject.AddComponent<StayRecorder>();
            Run(1);
            float entering = bullet.linearVelocity.x;
            Run(1);
            Assert.Equal(1, recorder.Count);
            Assert.Equal(-entering, recorder.Relative.x, 4);
            Assert.True(Mathf.Abs(recorder.Relative.x) > bullet.linearVelocity.x * 2);
        }

        // A Continuous body is swept against static colliders only: a fast Continuous sphere is not
        // swept against a Discrete or Continuous dynamic plate (and so passes it in this scene), while
        // a ContinuousDynamic plate is swept against it ("all other colliders except Discrete").
        // Red at 4d0029d5: the Discrete and Continuous rows (every moving pair was swept there).
        [Theory]
        [InlineData(CollisionDetectionMode.Discrete, true)]
        [InlineData(CollisionDetectionMode.Continuous, true)]
        [InlineData(CollisionDetectionMode.ContinuousDynamic, false)]
        public void FastContinuousSphereAgainstDynamicPlate(CollisionDetectionMode plateMode, bool tunnels)
        {
            var lane = Lane();
            var plate = Plate(lane, plateMode);
            var bullet = Bullet(lane, CollisionDetectionMode.Continuous);
            Run(3);
            Assert.Equal(tunnels, Tunnelled(bullet, plate.position));
            Assert.Equal(!tunnels, Stopped(bullet, plate.position));
        }

        // physicsccdhinge geometry-controls, 6000.0.71f1: a Discrete dynamic target is also
        // eligible for a ContinuousDynamic sweep. Its stationary pose is on the swept path.
        [Theory]
        [InlineData(CollisionDetectionMode.Discrete, false)]
        [InlineData(CollisionDetectionMode.Continuous, false)]
        [InlineData(CollisionDetectionMode.ContinuousDynamic, false)]
        [InlineData(CollisionDetectionMode.ContinuousSpeculative, false)]
        public void FastContinuousDynamicSphereAgainstDynamicPlate(CollisionDetectionMode plateMode, bool tunnels)
        {
            var lane = Lane();
            var plate = Plate(lane, plateMode);
            var bullet = Bullet(lane, CollisionDetectionMode.ContinuousDynamic);
            Run(3);
            Assert.Equal(tunnels, Tunnelled(bullet, plate.position));
            Assert.Equal(!tunnels, Stopped(bullet, plate.position));
        }

        [Theory]
        [InlineData(0f, -0.2f)]
        [InlineData(-1f, -0.20792079f)]
        [InlineData(-100f, 1f)]
        public void DiscreteDynamicTargetNeedsItsDestinationInsideTheSweptBounds(float targetSpeed, float expectedX)
        {
            var lane = Lane();
            var target = Make("target", PrimitiveType.Sphere, lane, Vector3.one * .2f).AddComponent<Rigidbody>();
            target.useGravity = false;
            target.linearDamping = 0;
            var bullet = Bullet(lane, CollisionDetectionMode.ContinuousDynamic);
            bullet.linearDamping = 0;
            bullet.linearVelocity = Vector3.zero;
            m_Space.Simulate(Dt);
            bullet.linearVelocity = Vector3.right * Speed;
            target.linearVelocity = Vector3.right * targetSpeed;
            m_Space.Simulate(Dt);
            Assert.InRange(Mathf.Abs(bullet.position.x - lane.x - expectedX), 0, 1e-4f);
            float speed = targetSpeed == -100 ? Speed : (Speed + targetSpeed) * .5f;
            for (int step = 1; step <= 3; step++)
            {
                m_Space.Simulate(Dt);
                Assert.InRange(Mathf.Abs(bullet.position.x - lane.x - expectedX - speed * Dt * step), 0, 1e-4f);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NonConvexMeshReturnsAfterKinematicTypeChanges(bool child)
        {
            var lane = Lane();
            var root = new GameObject("mesh-body"); root.transform.position = lane; m_Made.Add(root);
            var owner = root;
            if (child)
            {
                owner = new GameObject("mesh-child"); owner.transform.SetParent(root.transform, false);
            }
            var mesh = new Mesh(); m_Meshes.Add(mesh);
            mesh.vertices = new[] { new Vector3(0,-1,-1), new Vector3(0,-1,1), new Vector3(0,1,-1), new Vector3(0,1,1) };
            mesh.triangles = new[] { 0,1,2,2,1,3 };
            owner.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms();
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false;
            foreach (bool kinematic in new[] { true, false, true })
            {
                body.isKinematic = kinematic;
                Physics.SyncTransforms();
                bool hit = m_Space.Raycast(lane + Vector3.left, Vector3.right, out var result, 2);
                Assert.Equal(kinematic, hit);
                if (hit) Assert.Same(owner.GetComponent<MeshCollider>(), result.collider);
            }
            var bullet = Bullet(lane, CollisionDetectionMode.ContinuousDynamic);
            Run(2);
            Assert.True(Stopped(bullet, lane));
        }

        [Fact]
        public void PendingConvexEditAllowsChangingTheMeshBodyToDynamic()
        {
            var lane = Lane();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.position = lane; m_Made.Add(go);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            var body = go.AddComponent<Rigidbody>(); body.useGravity = false; body.linearDamping = 0;
            body.isKinematic = true;
            Physics.SyncTransforms();
            collider.convex = true;
            body.isKinematic = false;
            body.linearVelocity = Vector3.right;
            m_Space.Simulate(Dt);
            Assert.InRange(Mathf.Abs(body.position.x - lane.x - Dt), 0, 1e-5f);
            Assert.InRange(Mathf.Abs(body.linearVelocity.x - 1), 0, 1e-5f);
        }

        // A fast kinematic plate (0.1 m thin, MovePosition 2 m a step) passes a resting sphere of
        // radius 0.25 entirely within one step. Speculative CCD, which the docs allow on kinematic
        // bodies, makes it hit; Discrete does not.
        // Red at 4d0029d5: the ContinuousSpeculative row (sent as sweep CCD, which a kinematic body
        // cannot take, so the plate had no CCD). Also red with only the managed mapping reverted.
        [Theory]
        [InlineData(CollisionDetectionMode.Discrete, false)]
        [InlineData(CollisionDetectionMode.ContinuousSpeculative, true)]
        public void FastKinematicPlateHitsRestingSphereOnlyWhenSpeculative(CollisionDetectionMode mode, bool hits)
        {
            var lane = Lane();
            var sphere = Make("target", PrimitiveType.Sphere, lane, Vector3.one * 0.5f).AddComponent<Rigidbody>();
            sphere.useGravity = false;
            sphere.mass = 1f;
            var start = lane + new Vector3(-1.2f, 0f, 0f);
            var plate = Make("paddle", PrimitiveType.Cube, start, new Vector3(0.1f, 2f, 2f)).AddComponent<Rigidbody>();
            plate.isKinematic = true;
            plate.collisionDetectionMode = mode;
            m_Space.Simulate(Dt);
            Assert.Equal(mode, plate.collisionDetectionMode);
            for (int i = 1; i <= 3; i++)
            {
                plate.MovePosition(start + new Vector3(Speed * Dt * i, 0f, 0f));
                m_Space.Simulate(Dt);
            }
            // Hit: pushed ahead of the plate (about 5 m); missed: still at rest where it was.
            bool pushed = sphere.position.x - lane.x > 0.5f;
            bool untouched = Mathf.Abs(sphere.position.x - lane.x) < 0.01f && sphere.linearVelocity.magnitude < 0.01f;
            Assert.Equal(hits, pushed);
            Assert.Equal(!hits, untouched);
        }

        // A mode set after the body exists reaches it: a sphere built as ContinuousDynamic and then
        // switched to Continuous passes a Continuous plate; one built as Continuous and switched to
        // ContinuousDynamic is stopped by it.
        // Red at 4d0029d5: the first half (every non-Discrete pair was swept there). The second half
        // passes there; its red is head with only the managed mapping reverted (both bodies sent as
        // 1, Continuous) or with the runtime path dropped (ApplyBodySettings not passing the mode).
        [Fact]
        public void ModeChangedAtRuntimeReachesTheBody()
        {
            var laneA = Lane();
            var plateA = Plate(laneA, CollisionDetectionMode.Continuous);
            var bulletA = Bullet(laneA, CollisionDetectionMode.ContinuousDynamic);
            bulletA.linearVelocity = Vector3.zero;
            var laneB = Lane();
            var plateB = Plate(laneB, CollisionDetectionMode.Continuous);
            var bulletB = Bullet(laneB, CollisionDetectionMode.Continuous);
            bulletB.linearVelocity = Vector3.zero;
            m_Space.Simulate(Dt); // both bodies exist, at rest

            bulletA.collisionDetectionMode = CollisionDetectionMode.Continuous;
            bulletB.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            bulletA.linearVelocity = new Vector3(Speed, 0f, 0f);
            bulletB.linearVelocity = new Vector3(Speed, 0f, 0f);
            Run(3);
            Assert.Equal(CollisionDetectionMode.Continuous, bulletA.collisionDetectionMode);
            Assert.Equal(CollisionDetectionMode.ContinuousDynamic, bulletB.collisionDetectionMode);
            Assert.True(Tunnelled(bulletA, plateA.position), $"switched to Continuous: x {bulletA.position.x - laneA.x}");
            Assert.True(Stopped(bulletB, plateB.position), $"switched to ContinuousDynamic: x {bulletB.position.x - laneB.x}");
        }

        // The getter returns the mode set, before and after the body exists, dynamic or kinematic;
        // the default is Discrete.
        // Red: none at 4d0029d5 (it passes there). Guard against a native-only or kinematic rewrite
        // of the stored value.
        [Fact]
        public void ModeGetterRoundTrips()
        {
            var lane = Lane();
            var rb = Make("roundtrip", PrimitiveType.Cube, lane, Vector3.one).AddComponent<Rigidbody>();
            rb.useGravity = false;
            Assert.Equal(CollisionDetectionMode.Discrete, rb.collisionDetectionMode);
            var modes = new[]
            {
                CollisionDetectionMode.Continuous, CollisionDetectionMode.ContinuousDynamic,
                CollisionDetectionMode.ContinuousSpeculative, CollisionDetectionMode.Discrete,
            };
            foreach (bool kinematic in new[] { false, true })
            {
                rb.isKinematic = kinematic;
                foreach (var mode in modes)
                {
                    rb.collisionDetectionMode = mode;
                    Assert.Equal(mode, rb.collisionDetectionMode);
                    m_Space.Simulate(Dt);
                    Assert.Equal(mode, rb.collisionDetectionMode);
                }
            }
        }
    }
}
