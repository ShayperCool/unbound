using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Records the 2D contact messages a local physics scene's step sends, and what the
    /// static and per-scene queries see from inside the callback.</summary>
    public class LocalSceneContactProbe : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        public PhysicsScene2D scene;

        void OnCollisionEnter2D(Collision2D c)
        {
            log.Add("Enter:" + c.collider.name);
            var fromStatic = Physics2D.Raycast(new Vector2(-20f, 10f), Vector2.down, 100f);
            var fromScene = scene.Raycast(new Vector2(21.5f, 10f), Vector2.down, 100f);
            log.Add("static:" + (fromStatic.collider != null ? fromStatic.collider.name : "none"));
            log.Add("scene:" + (fromScene.collider != null ? fromScene.collider.name : "none"));
        }
    }

    /// <summary>
    /// Local 2D physics scenes (6000.0 Scripting API: LocalPhysicsMode, CreateSceneParameters,
    /// PhysicsScene2D, PhysicsScene2D.Simulate, PhysicsSceneExtensions2D.GetPhysicsScene2D), as the
    /// Unity 6000.0.71f1 Editor answers them in tests/projects/localphysics: a scene created with
    /// LocalPhysicsMode.Physics2D owns its own physics scene, simulated only by its Simulate (in any
    /// simulation mode) and searched only by its queries; Physics2D's static queries and steps are the
    /// default scene's; a body that changes physics scene starts at rest; a joint refuses a body of
    /// another physics scene; a created scene's physics scene is invalid after its unload. Each test
    /// has a control where one shared world would give another answer.
    /// </summary>
    public class LocalPhysicsScene2DTests : IDisposable
    {
        const float Dt = 0.02f;
        static int s_Created;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly Scene m_Active;

        public LocalPhysicsScene2DTests()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Physics2D.queriesHitTriggers = true;
            Physics2D.queriesStartInColliders = true;
            Physics2D.autoSyncTransforms = true;
            for (int i = 0; i < 32; i++) Physics2D.SetLayerCollisionMask(i, -1);
            Physics2D.simulationMode = SimulationMode2D.Script;
            m_Active = SceneManager.GetActiveScene();
        }

        public void Dispose()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            if (m_Active.isLoaded && SceneManager.GetActiveScene() != m_Active) SceneManager.SetActiveScene(m_Active);
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var scene in m_Scenes)
                if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            SceneManagerInternal.ProcessPending();
            Physics2D.Simulate(Dt);
        }

        Scene LocalScene()
        {
            var scene = SceneManager.CreateScene("LocalPhysics2D" + s_Created++, new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            m_Scenes.Add(scene);
            return scene;
        }

        GameObject Make(string name, Vector2 position, Scene? scene = null)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            if (scene.HasValue) SceneManager.MoveGameObjectToScene(go, scene.Value);
            return go;
        }

        Rigidbody2D Box(string name, Vector2 position, Scene? scene = null)
        {
            var go = Make(name, position, scene);
            go.AddComponent<BoxCollider2D>();
            return go.AddComponent<Rigidbody2D>();
        }

        GameObject Floor(string name, Vector2 position, Scene? scene = null)
        {
            var go = Make(name, position, scene);
            go.AddComponent<BoxCollider2D>().size = new Vector2(4f, 1f);
            return go;
        }

        static void Step(PhysicsScene2D scene, int steps)
        {
            for (int i = 0; i < steps; i++) Assert.True(scene.Simulate(Dt));
        }

        static void Run(int steps)
        {
            for (int i = 0; i < steps; i++) Assert.True(Physics2D.Simulate(Dt));
        }

        [Fact]
        public void ALocalSceneOwnsItsPhysicsScene()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene2D();
            Assert.NotEqual(Physics2D.defaultPhysicsScene, physics);
            Assert.True(physics.IsValid());
            Assert.True(physics.IsEmpty());
            Assert.Equal(physics, local.GetPhysicsScene2D());
            Assert.Equal(Physics.defaultPhysicsScene, local.GetPhysicsScene());
            Assert.Equal(Physics2D.defaultPhysicsScene, SceneManager.GetActiveScene().GetPhysicsScene2D());
            // A static collider alone counts (tests/projects/localphysics p2.lone.empty).
            Floor("Lone", new Vector2(0f, -50f), local);
            Assert.False(physics.IsEmpty());
        }

        [Fact]
        public void OverlappingBodiesOfTwoPhysicsScenesDoNotTouch()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene2D();
            var localBody = Box("LocalBody", new Vector2(0f, 5f), local);
            var defaultBody = Box("DefaultBody", new Vector2(0f, 5f));
            // Control: the same overlap in one physics scene pushes the bodies apart.
            var pushed = Box("Pushed", new Vector2(40f, 5f));
            Box("Pusher", new Vector2(40f, 5f));

            Run(5);
            Assert.Equal(new Vector2(0f, 5f), localBody.position);
            Assert.Equal(0f, defaultBody.position.x);
            Assert.NotEqual(40f, pushed.position.x);
            Assert.False(localBody.GetComponent<Collider2D>().IsTouching(defaultBody.GetComponent<Collider2D>()));

            float defaultY = defaultBody.position.y;
            Step(physics, 5);
            Assert.True(localBody.position.y < 5f);
            Assert.Equal(0f, localBody.position.x);
            Assert.Equal(defaultY, defaultBody.position.y);
        }

        [Fact]
        public void ALocalSceneStepsInAnySimulationModeAndTheDefaultOneOnlyInScript()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene2D();
            var body = Box("Mode", new Vector2(0f, 5f), local);
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            Assert.True(physics.Simulate(Dt));
            Assert.True(body.position.y < 5f);
            Assert.False(Physics2D.Simulate(Dt));
            Assert.False(Physics2D.defaultPhysicsScene.Simulate(Dt));
        }

        [Fact]
        public void QueriesSearchOnlyTheirPhysicsScene()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene2D();
            Floor("LocalFloor", new Vector2(20f, 0f), local);
            Floor("DefaultFloor", new Vector2(-20f, 0f));
            var body = Box("Caster", new Vector2(0f, 5f), local);
            body.gravityScale = 0f;
            var wallLocal = Make("LocalWall", new Vector2(10f, 5f), local);
            wallLocal.AddComponent<BoxCollider2D>().size = new Vector2(1f, 4f);
            var wallDefault = Make("DefaultWall", new Vector2(5f, 5f));
            wallDefault.AddComponent<BoxCollider2D>().size = new Vector2(1f, 4f);

            Assert.Null(Physics2D.Raycast(new Vector2(20f, 10f), Vector2.down, 100f).collider);
            Assert.Equal("DefaultFloor", Physics2D.Raycast(new Vector2(-20f, 10f), Vector2.down, 100f).collider.name);
            Assert.Equal("LocalFloor", physics.Raycast(new Vector2(20f, 10f), Vector2.down, 100f).collider.name);
            Assert.Null(physics.Raycast(new Vector2(-20f, 10f), Vector2.down, 100f).collider);
            Assert.Null(Physics2D.OverlapPoint(new Vector2(20f, 0f)));
            Assert.Equal("LocalFloor", physics.OverlapPoint(new Vector2(20f, 0f)).name);
            // A body's cast searches its own physics scene: the nearer default wall is not hit.
            var hits = new RaycastHit2D[4];
            Assert.True(body.Cast(Vector2.right, hits, 100f) > 0);
            Assert.Equal("LocalWall", hits[0].collider.name);
        }

        [Fact]
        public void ContactsOfALocalStepAreSentAndStaticQueriesInTheirCallbacksSearchTheDefaultScene()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene2D();
            Floor("LocalFloor", new Vector2(20f, 0f), local);
            Floor("DefaultFloor", new Vector2(-20f, 0f));
            var dropper = Box("Dropper", new Vector2(20f, 1.6f), local);
            var probe = dropper.gameObject.AddComponent<LocalSceneContactProbe>();
            probe.scene = physics;

            Run(30);
            Assert.Empty(probe.log);
            Step(physics, 30);
            Assert.Equal(new[] { "Enter:LocalFloor", "static:DefaultFloor", "scene:LocalFloor" }, probe.log);
        }

        [Fact]
        public void ABodyThatChangesPhysicsSceneStartsAtRest()
        {
            var local = LocalScene();
            var body = Box("Mover", new Vector2(0f, 5f));
            body.gravityScale = 0f;
            body.linearVelocity = new Vector2(3f, 0f);
            body.angularVelocity = 90f;
            SceneManager.MoveGameObjectToScene(body.gameObject, local);
            Assert.Equal(Vector2.zero, body.linearVelocity);
            Assert.Equal(0f, body.angularVelocity);
            Step(local.GetPhysicsScene2D(), 1);
            Assert.Equal(new Vector2(0f, 5f), body.position);

            // Control: within its physics scene a body keeps its velocity across a step.
            body.linearVelocity = new Vector2(2f, 0f);
            Step(local.GetPhysicsScene2D(), 1);
            Assert.Equal(new Vector2(2f, 0f), body.linearVelocity);

            SceneManager.MoveGameObjectToScene(body.gameObject, m_Active);
            Assert.Equal(Vector2.zero, body.linearVelocity);
        }

        [Fact]
        public void AJointRefusesABodyOfAnotherPhysicsScene()
        {
            var local = LocalScene();
            var held = Box("Held", new Vector2(80f, 5f), local);
            var other = Box("Other", new Vector2(82f, 5f));
            var sameScene = Box("SameScene", new Vector2(84f, 5f), local);
            var joint = held.gameObject.AddComponent<FixedJoint2D>();
            using (var logs = new LogCapture())
            {
                joint.connectedBody = other;
                Assert.Contains("Cannot set the connected rigidbody for the 'FixedJoint2D' type because it connects to a rigidbody in a different physics scene.", logs.Warnings);
            }
            Assert.Null(joint.connectedBody);
            // Control: a body of the same physics scene connects.
            joint.connectedBody = sameScene;
            Assert.Same(sameScene, joint.connectedBody);
        }

        [Fact]
        public void EffectorsActOnlyInTheStepOfTheirPhysicsScene()
        {
            var local = LocalScene();
            var area = Make("Area", new Vector2(0f, 0f));
            var box = area.AddComponent<BoxCollider2D>();
            box.size = new Vector2(4f, 4f);
            box.isTrigger = true;
            box.usedByEffector = true;
            var effector = area.AddComponent<AreaEffector2D>();
            effector.forceAngle = 0f;
            effector.forceMagnitude = 50f;
            var pushed = Box("InArea", new Vector2(0f, 0f));
            pushed.gravityScale = 0f;
            Box("LocalBody", new Vector2(0f, 50f), local);
            Run(2);
            var velocity = pushed.linearVelocity;
            Assert.True(velocity.x > 0f);
            // Stepping the local scene leaves the default scene's effector and bodies alone.
            Step(local.GetPhysicsScene2D(), 3);
            Run(1);
            var once = pushed.linearVelocity;
            Run(1);
            // Control: each default step adds the same push again.
            Assert.Equal(once.x - velocity.x, pushed.linearVelocity.x - once.x, 4);
        }

        [Fact]
        public void ACreatedScenesPhysicsSceneEndsWithIt()
        {
            var local = LocalScene();
            var physics = local.GetPhysicsScene2D();
            Floor("Gone", new Vector2(20f, 0f), local);
            SceneManager.UnloadSceneAsync(local);
            SceneManagerInternal.ProcessPending();

            Assert.False(physics.IsValid());
            Assert.NotEqual(Physics2D.defaultPhysicsScene, physics);
            Assert.Throws<InvalidOperationException>(() => physics.IsEmpty());
            Assert.Throws<InvalidOperationException>(() => physics.Simulate(Dt));
            Assert.Null(physics.Raycast(new Vector2(20f, 10f), Vector2.down, 100f).collider);
            Assert.True(Physics2D.defaultPhysicsScene.IsValid());
        }

        sealed class LogCapture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Inner = Debug.unityLogger.logHandler;
            internal readonly List<string> Warnings = new List<string>();

            internal LogCapture() { Debug.unityLogger.logHandler = this; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
            public void Dispose() => Debug.unityLogger.logHandler = m_Inner;
        }
    }
}
