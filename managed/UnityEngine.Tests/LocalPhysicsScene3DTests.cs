using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Local 3D physics scenes (6000.0 Scripting API: LocalPhysicsMode, CreateSceneParameters,
    /// PhysicsScene, PhysicsScene.Simulate, PhysicsSceneExtensions.GetPhysicsScene,
    /// Physics.defaultPhysicsScene). A scene created with LocalPhysicsMode.Physics3D owns its own
    /// physics scene: its bodies are simulated only by its PhysicsScene.Simulate, and its colliders
    /// are found only by its queries. These isolation guarantees are documented.
    /// Keeping the default physics scene's behavior unchanged is inferred from that isolation.
    /// That MoveGameObjectToScene takes a body into the new scene's physics scene is also inferred
    /// from it (SceneManager.MoveGameObjectToScene documents only the move), not measured.
    /// Local identities, unload timing and invalid-scene queries are not asserted.
    /// </summary>
    public class LocalPhysicsScene3DTests : IDisposable
    {
        const float Dt = 0.02f;
        static int s_Created;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<Physics.ContactEventDelegate> m_Handlers = new List<Physics.ContactEventDelegate>();
        readonly Scene m_Active;

        public LocalPhysicsScene3DTests()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Physics.queriesHitTriggers = true;
            Physics.autoSyncTransforms = true;
            Physics.invokeCollisionCallbacks = true;
            for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Physics.IgnoreLayerCollision(i, j, false);
            Physics.simulationMode = SimulationMode.Script;
            m_Active = SceneManager.GetActiveScene();
        }

        public void Dispose()
        {
            foreach (var handler in m_Handlers) Physics.ContactEvent -= handler;
            if (m_Active.isLoaded && SceneManager.GetActiveScene() != m_Active) SceneManager.SetActiveScene(m_Active);
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var scene in m_Scenes)
                if (scene.isLoaded) SceneOperations.Unload(scene);
        }

        Scene LocalScene()
        {
            var scene = SceneManager.CreateScene("LocalPhysics3D" + s_Created++, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            m_Scenes.Add(scene);
            return scene;
        }

        /// <summary>Makes objects in a scene as the Scripting API example does: with it active.</summary>
        T In<T>(Scene scene, Func<T> make)
        {
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try { return make(); }
            finally { SceneManager.SetActiveScene(previous); }
        }

        GameObject Ball(string name, Vector3 position)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = position;
            go.AddComponent<SphereCollider>().radius = 0.5f;
            go.AddComponent<Rigidbody>();
            return go;
        }

        GameObject Floor(string name, Vector3 position)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);
            return go;
        }

        static void Steps(int count) { for (int i = 0; i < count; i++) Physics.Simulate(Dt); }

        static void Steps(PhysicsScene scene, int count) { for (int i = 0; i < count; i++) scene.Simulate(Dt); }

        [Fact]
        public void SceneOwnsItsPhysicsScene()
        {
            var local = LocalScene();
            var plain = SceneManager.CreateScene("PlainPhysics3D" + s_Created++);
            m_Scenes.Add(plain);

            var physics = local.GetPhysicsScene();
            Assert.True(physics.IsValid());
            Assert.NotEqual(Physics.defaultPhysicsScene, physics);
            Assert.Equal(physics, local.GetPhysicsScene());
            // A scene without local physics uses the default physics scene.
            Assert.Equal(Physics.defaultPhysicsScene, plain.GetPhysicsScene());
            Assert.Equal(Physics.defaultPhysicsScene, m_Active.GetPhysicsScene());
            Assert.True(Physics.defaultPhysicsScene.IsValid());

            Assert.True(physics.IsEmpty());
            var ball = In(local, () => Ball("LocalBall", Vector3.zero));
            Assert.Equal(local, ball.scene);
            Assert.False(physics.IsEmpty());
        }

        [Fact]
        public void UnloadedSceneInvalidatesItsPhysicsScene()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene();
            In(local, () => Ball("Unloaded", Vector3.zero));
            // The unload is queued to the scripting point that completes it (Compat.DestroyPhasesMeasured).
            SceneOperations.Unload(local);

            // Documented: the physics scene of a destroyed scene is no longer valid; the default
            // one always is. What its other methods do then is not documented and not asserted.
            Assert.False(physics.IsValid());
            Assert.True(Physics.defaultPhysicsScene.IsValid());
        }

        /// <summary>A scene loaded from a scene asset, as a load leaves it (its path; nothing read).</summary>
        Scene LoadedScene(LocalPhysicsMode mode)
        {
            var data = Unbound.Runtime.SceneRegistry.Create("LoadedPhysics" + s_Created++, "Assets/LoadedPhysics.unity", -1);
            SceneManagerInternal.CreateLocalPhysics(data, mode);
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loaded;
            Unbound.Runtime.SceneRegistry.Loaded.Add(data);
            var scene = new Scene(data.Handle);
            m_Scenes.Add(scene);
            return scene;
        }

        /// <summary>The measured release keeps a loaded scene's physics scenes valid and empty
        /// after its unload: stepping them does nothing (Unbound threw), queries find nothing
        /// (tests/projects/localphysics stale.*; 2D answers its Simulate with true). Other
        /// releases follow the documented lifetime.</summary>
        [Fact]
        public void ALoadedScenesPhysicsScenesStayValidAndInertAfterItsUnload()
        {
            // Measured on 6000.0 (other families keep their earlier, unmeasured behaviour).
            using var release = TestRelease.Pin(TestRelease.Measured);
            var loaded = LoadedScene(LocalPhysicsMode.Physics3D | LocalPhysicsMode.Physics2D);
            var physics = loaded.GetPhysicsScene();
            var physics2D = loaded.GetPhysicsScene2D();
            In(loaded, () => Floor("LoadedFloor", Vector3.zero));
            In(loaded, () => Ball("LoadedBall", new Vector3(0f, 3f, 0f)));
            Assert.False(physics.IsEmpty());
            SceneOperations.Unload(loaded);

            Assert.True(physics.IsValid());
            Assert.True(physics2D.IsValid());
            Assert.True(physics.IsEmpty());
            Assert.True(physics2D.IsEmpty());
            physics.Simulate(Dt);
            Assert.True(physics2D.Simulate(Dt));
            Assert.False(physics.Raycast(new Vector3(0f, 10f, 0f), Vector3.down, 100f));
            Assert.Null(physics2D.Raycast(new Vector2(0f, 10f), Vector2.down, 100f).collider);
            // A created scene's do not stay (the control).
            var created = LocalScene();
            var createdPhysics = created.GetPhysicsScene();
            SceneOperations.Unload(created);
            Assert.False(createdPhysics.IsValid());
            Assert.Throws<InvalidOperationException>(() => createdPhysics.Simulate(Dt));
        }

        [Fact]
        public void OtherReleasesEndALoadedScenesPhysicsSceneWithIt()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var loaded = LoadedScene(LocalPhysicsMode.Physics3D);
            var physics = loaded.GetPhysicsScene();
            SceneOperations.Unload(loaded);
            Assert.False(physics.IsValid());
        }

        [Fact]
        public void LocalBodiesIgnoreTheDefaultSimulation()
        {
            var local = LocalScene();
            var localBall = In(local, () => Ball("LocalBall", new Vector3(0f, 5f, 0f)));
            var defaultBall = Ball("DefaultBall", new Vector3(20f, 5f, 0f));

            Steps(10);

            Assert.Equal(5f, localBall.transform.position.y);
            Assert.True(defaultBall.transform.position.y < 5f);
        }

        [Fact]
        public void PhysicsSceneSimulateMovesOnlyItsOwnBodies()
        {
            var local = LocalScene();
            var localBall = In(local, () => Ball("LocalBall", new Vector3(0f, 5f, 0f)));
            var defaultBall = Ball("DefaultBall", new Vector3(20f, 5f, 0f));

            Steps(local.GetPhysicsScene(), 10);

            Assert.True(localBall.transform.position.y < 5f);
            Assert.True(localBall.GetComponent<Rigidbody>().linearVelocity.y < 0f);
            Assert.Equal(5f, defaultBall.transform.position.y);
        }

        [Fact]
        public void DefaultPhysicsSceneSimulateMovesOnlyDefaultBodies()
        {
            var local = LocalScene();
            var localBall = In(local, () => Ball("LocalBall", new Vector3(0f, 5f, 0f)));
            var defaultBall = Ball("DefaultBall", new Vector3(20f, 5f, 0f));

            Steps(Physics.defaultPhysicsScene, 10);

            Assert.True(defaultBall.transform.position.y < 5f);
            Assert.Equal(5f, localBall.transform.position.y);
        }

        [Fact]
        public void QueriesSearchOnlyTheirPhysicsScene()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene();
            var defaultFloor = Floor("DefaultFloor", new Vector3(0f, 0f, 0f));
            var localFloor = In(local, () => Floor("LocalFloor", new Vector3(30f, 0f, 0f)));

            Assert.True(Physics.Raycast(new Vector3(0f, 10f, 0f), Vector3.down, out var hit, 100f));
            Assert.Same(defaultFloor.GetComponent<Collider>(), hit.collider);
            Assert.False(Physics.Raycast(new Vector3(30f, 10f, 0f), Vector3.down, 100f));
            Assert.True(Physics.defaultPhysicsScene.Raycast(new Vector3(0f, 10f, 0f), Vector3.down, out hit, 100f));
            Assert.Same(defaultFloor.GetComponent<Collider>(), hit.collider);

            Assert.True(physics.Raycast(new Vector3(30f, 10f, 0f), Vector3.down, out hit, 100f));
            Assert.Same(localFloor.GetComponent<Collider>(), hit.collider);
            Assert.Equal(9.5f, hit.distance, 3);
            Assert.False(physics.Raycast(new Vector3(0f, 10f, 0f), Vector3.down, 100f));

            var buffer = new Collider[4];
            Assert.Equal(1, physics.OverlapSphere(new Vector3(30f, 0f, 0f), 1f, buffer, Physics.AllLayers, QueryTriggerInteraction.UseGlobal));
            Assert.Same(localFloor.GetComponent<Collider>(), buffer[0]);
            Assert.Equal(0, physics.OverlapBox(new Vector3(0f, 0f, 0f), Vector3.one, buffer));
            Assert.Empty(Physics.OverlapSphere(new Vector3(30f, 0f, 0f), 1f));
            Assert.True(physics.SphereCast(new Vector3(30f, 10f, 0f), 0.25f, Vector3.down, out hit, 100f));
            Assert.Same(localFloor.GetComponent<Collider>(), hit.collider);
            Assert.False(Physics.SphereCast(new Vector3(30f, 10f, 0f), 0.25f, Vector3.down, out hit, 100f));
        }

        [Fact]
        public void MoveGameObjectToSceneMovesItsBody()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene();
            var ball = Ball("Moved", new Vector3(40f, 5f, 0f));
            Steps(5);
            float fallen = ball.transform.position.y;
            Assert.True(fallen < 5f);

            SceneManager.MoveGameObjectToScene(ball, local);
            Assert.False(physics.IsEmpty());
            Steps(5);
            Assert.Equal(fallen, ball.transform.position.y);
            Assert.False(Physics.Raycast(new Vector3(40f, 10f, 0f), Vector3.down, 100f));
            Assert.True(physics.Raycast(new Vector3(40f, 10f, 0f), Vector3.down, 100f));

            // It moves there, from where it was (whether Unity keeps its velocity is not asserted).
            physics.Simulate(Dt);
            Assert.True(ball.transform.position.y < fallen);

            SceneManager.MoveGameObjectToScene(ball, m_Active);
            Assert.True(physics.IsEmpty());
            float back = ball.transform.position.y;
            Steps(physics, 5);
            Assert.Equal(back, ball.transform.position.y);
            Steps(1);
            Assert.True(ball.transform.position.y < back);
        }

        [Fact]
        public void ContactsOfALocalSceneComeFromItsStep()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene();
            var scenes = new List<PhysicsScene>();
            Physics.ContactEventDelegate handler = (scene, headers) => scenes.Add(scene);
            m_Handlers.Add(handler);
            Physics.ContactEvent += handler;
            var ball = In(local, () =>
            {
                Floor("LocalFloor", Vector3.zero);
                var b = Ball("LocalBall", new Vector3(0f, 1.2f, 0f));
                b.GetComponent<Collider>().providesContacts = true;
                return b;
            });
            var recorder = ball.AddComponent<ContactRecorder3D>();

            Steps(30);
            Assert.Empty(recorder.log);
            Assert.Empty(scenes);

            Steps(physics, 30);
            Assert.Contains("CollisionEnter:LocalFloor", recorder.log);
            Assert.NotEmpty(scenes);
            Assert.All(scenes, scene => Assert.Equal(physics, scene));
        }

        /// <summary>A local scene with overlapping objects at the same place, stepped alongside, is
        /// expected to leave the default scene's motion as it is alone (an implementation check of
        /// the separate worlds; Unity's own determinism here is not measured).</summary>
        [Fact]
        public void DefaultSceneIsUnchangedByALocalScene()
        {
            var alone = Run(withLocal: false);
            var beside = Run(withLocal: true);
            Assert.Equal(alone, beside);

            List<Vector3> Run(bool withLocal)
            {
                var made = m_Made.Count;
                var floor = Floor("Floor", new Vector3(50f, 0f, 0f));
                var ball = Ball("Ball", new Vector3(50f, 2f, 0.1f));
                PhysicsScene physics = default;
                if (withLocal)
                {
                    var local = LocalScene();
                    physics = local.GetPhysicsScene();
                    In(local, () =>
                    {
                        Floor("LocalFloor", new Vector3(50f, 0f, 0f));
                        return Ball("LocalBall", new Vector3(50f, 2.3f, 0.1f));
                    });
                }
                var trace = new List<Vector3>();
                for (int i = 0; i < 60; i++)
                {
                    Physics.Simulate(Dt);
                    if (withLocal) physics.Simulate(Dt);
                    trace.Add(ball.transform.position);
                    trace.Add(ball.GetComponent<Rigidbody>().linearVelocity);
                }
                for (int i = m_Made.Count - 1; i >= made; i--)
                {
                    Object.DestroyImmediate(m_Made[i]);
                    m_Made.RemoveAt(i);
                }
                return trace;
            }
        }

        sealed class WarningCapture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Inner = Debug.unityLogger.logHandler;
            internal readonly List<string> Warnings = new List<string>();
            internal WarningCapture() { Debug.unityLogger.logHandler = this; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
            public void Dispose() => Debug.unityLogger.logHandler = m_Inner;
        }

        [Fact]
        public void AJointRefusesABodyOfAnotherPhysicsScene()
        {
            // Measured: tests/projects/localphysics p3.joint.* (Unity 6000.0.71f1).
            var local = LocalScene();
            var held = In(local, () => Ball("Held", new Vector3(80f, 5f, 0f)));
            var other = Ball("Other", new Vector3(82f, 5f, 0f)).GetComponent<Rigidbody>();
            var sameScene = In(local, () => Ball("SameScene", new Vector3(84f, 5f, 0f))).GetComponent<Rigidbody>();
            var joint = held.AddComponent<FixedJoint>();
            using (var logs = new WarningCapture())
            {
                joint.connectedBody = other;
                Assert.Contains("Rigidbody cannot be connected to FixedJoint as they belong to a different physics scenes. Joint's GameObject name: 'Held', Rigidbody component's GameObject name: 'Other'.", logs.Warnings);
            }
            Assert.Null(joint.connectedBody);
            // Control: a body of the same physics scene connects.
            joint.connectedBody = sameScene;
            Assert.Same(sameScene, joint.connectedBody);
        }

        [Fact]
        public void TheDefaultSceneStepOutsideScriptModeWarnsUnderItsCallersName()
        {
            // Measured: tests/projects/localphysics p3.defaultSim.log0 (PhysicsScene) and
            // Physics.Simulate's own warning.
            Physics.simulationMode = SimulationMode.FixedUpdate;
            try
            {
                using var logs = new WarningCapture();
                Physics.defaultPhysicsScene.Simulate(Dt);
                Physics.Simulate(Dt);
                Assert.Equal(new[]
                {
                    "PhysicsScene.Simulate(...) was called but simulation mode is not set to Script. You should set simulation mode to Script first before calling this function therefore the simulation was not run.",
                    "Physics.Simulate(...) was called but simulation mode is not set to Script. You should set simulation mode to Script first before calling this function therefore the simulation was not run.",
                }, logs.Warnings);
            }
            finally { Physics.simulationMode = SimulationMode.Script; }
        }
    }
}
