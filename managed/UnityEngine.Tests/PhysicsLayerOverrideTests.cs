using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Records the collision and trigger Enter messages its GameObject gets, with the
    /// collisions' contacts.</summary>
    public class LayerOverrideRecorder : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        public readonly List<Collision> collisions = new List<Collision>();

        void OnCollisionEnter(Collision c) { log.Add("CollisionEnter:" + c.collider.name); collisions.Add(c); }
        void OnTriggerEnter(Collider other) { log.Add("TriggerEnter:" + other.name); }
    }

    /// <summary>
    /// Collider/Rigidbody includeLayers, excludeLayers and Collider.layerOverridePriority deciding
    /// whether two colliders make contact. The rules are the Unity 6000.0 Scripting API's
    /// (Collider.layerOverridePriority): the same decision on both sides stands; if only one side
    /// overrides, its decision is used; if both do, the higher priority's; equal priorities make no
    /// contact. Collider.excludeLayers / Rigidbody.excludeLayers: the matrix first includes, then
    /// excludes; a layer both included and excluded is excluded.
    ///
    /// "Overrides" is read per pair: a collider overrides for a pair when its own or its rigidbody's
    /// include/exclude layers name the other collider's layer. The docs do not define it per bit; the
    /// tests that depend on that reading say so. No Unity capture measures any of these cases yet.
    /// Only contact membership is asserted (a ball resting on or falling through the ground, Enter
    /// messages, a compound body's touching collider), never Stay cadence, Exit timing after a
    /// filter change or message order.
    /// </summary>
    public class PhysicsLayerOverrideTests : IDisposable
    {
        const float Dt = 0.02f;
        const int GroundLayer = 8;
        const int BallLayer = 9;
        const int UnrelatedLayer = 20;
        readonly List<GameObject> m_Made = new List<GameObject>();

        public PhysicsLayerOverrideTests()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Physics.queriesHitTriggers = true;
            Physics.autoSyncTransforms = true;
            Physics.invokeCollisionCallbacks = true;
            AllLayersCollide();
            Physics.simulationMode = SimulationMode.Script;
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            Physics.Simulate(Dt);
            AllLayersCollide();
        }

        static void AllLayersCollide()
        {
            for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Physics.IgnoreLayerCollision(i, j, false);
        }

        GameObject Make(string name, Vector3 position, int layer)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.position = position;
            m_Made.Add(go);
            return go;
        }

        static void Run(int steps)
        {
            for (int i = 0; i < steps; i++) Physics.Simulate(Dt);
        }

        Collider Ground()
        {
            var box = Make("ground", Vector3.zero, GroundLayer).AddComponent<BoxCollider>();
            box.size = new Vector3(20f, 1f, 20f);
            return box;
        }

        Collider Ball(float y = 2f)
        {
            var go = Make("ball", new Vector3(0f, y, 0f), BallLayer);
            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            go.AddComponent<Rigidbody>();
            go.AddComponent<LayerOverrideRecorder>();
            return sphere;
        }

        static LayerOverrideRecorder Log(Collider c) => c.GetComponent<LayerOverrideRecorder>();

        /// <summary>The ball is held by the ground: it rests a radius above its top (0.5) and got
        /// OnCollisionEnter from it.</summary>
        static void AssertContact(Collider ball)
        {
            float y = ball.transform.position.y;
            Assert.True(y > 0.9f && y < 1.1f, $"the ball should rest on the ground, at y {y}");
            Assert.Contains("CollisionEnter:ground", Log(ball).log);
        }

        /// <summary>The ball fell through the ground without touching it.</summary>
        static void AssertNoContact(Collider ball)
        {
            float y = ball.transform.position.y;
            Assert.True(y < -1f, $"the ball should fall through the ground, at y {y}");
            Assert.DoesNotContain("CollisionEnter:ground", Log(ball).log);
        }

        // ------------------------------------------------------------------ the matrix alone

        [Fact]
        public void WithoutOverridesTheMatrixDecides()
        {
            Ground();
            var ball = Ball();
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void WithoutOverridesADisabledMatrixPairPassesThrough()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            Ground();
            var ball = Ball();
            Run(80);
            AssertNoContact(ball);
        }

        // ------------------------------------------------------------------ one side overrides

        [Fact]
        public void OneSidedIncludeOverridesADisabledMatrix()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            Ground();
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void OneSidedIncludeOnTheStaticSideOverridesADisabledMatrix()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var ground = Ground();
            ground.includeLayers = 1 << BallLayer;
            var ball = Ball();
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void OneSidedExcludeOverridesAnEnabledMatrix()
        {
            Ground();
            var ball = Ball();
            ball.excludeLayers = 1 << GroundLayer;
            Run(80);
            AssertNoContact(ball);
        }

        // ------------------------------------------------------------------ both sides override

        [Fact]
        public void AgreeingOverridesStandWhateverTheirPriorities()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var ground = Ground();
            ground.includeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 7;
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = 1;
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void ConflictingOverridesTakeTheHigherPrioritysInclude()
        {
            var ground = Ground();
            ground.excludeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 1;
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = 2;
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void ConflictingOverridesTakeTheHigherPrioritysExclude()
        {
            var ground = Ground();
            ground.excludeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 2;
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = 1;
            Run(80);
            AssertNoContact(ball);
        }

        [Fact]
        public void TheStaticSidesHigherPriorityIncludeWins()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var ground = Ground();
            ground.includeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 5;
            var ball = Ball();
            ball.excludeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = -5;
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void ConflictingOverridesOfEqualPriorityMakeNoContact()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var ground = Ground();
            ground.excludeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 3;
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = 3;
            Run(80);
            AssertNoContact(ball);
        }

        [Theory]
        [InlineData(65536, 1)] // differs from 1 in no low 16 bits
        [InlineData(-1, int.MinValue)] // signed: -1 is the higher
        [InlineData(int.MaxValue, int.MinValue)]
        public void PrioritiesCompareAsFullSignedIntegers(int includePriority, int excludePriority)
        {
            var ground = Ground();
            ground.excludeLayers = 1 << BallLayer;
            ground.layerOverridePriority = excludePriority;
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = includePriority;
            Run(80);
            AssertContact(ball);
        }

        // ------------------------------------------------------------------ one collider's masks

        [Fact]
        public void ALayerBothIncludedAndExcludedIsExcluded()
        {
            Ground();
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.excludeLayers = 1 << GroundLayer;
            Run(80);
            AssertNoContact(ball);
        }

        [Fact]
        public void IncludeBitsForOtherLayersChangeNothing()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            Ground();
            var ball = Ball();
            ball.includeLayers = 1 << UnrelatedLayer;
            Run(80);
            AssertNoContact(ball);
        }

        /// <summary>Depends on the per-pair reading of "using an override" (unresolved by the docs):
        /// the ground's exclude of an unrelated layer does not make it override for this pair, so
        /// the ball's include alone decides. Under an "any non-zero mask" reading both would
        /// override at equal priority and make no contact.</summary>
        [Fact]
        public void AnOverrideForAnUnrelatedLayerDoesNotEnterThePair()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var ground = Ground();
            ground.excludeLayers = 1 << UnrelatedLayer;
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            Run(80);
            AssertContact(ball);
        }

        // ------------------------------------------------------------------ rigidbodies

        [Fact]
        public void ARigidbodysIncludeLayersApplyToItsColliders()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            Ground();
            var ball = Ball();
            ball.GetComponent<Rigidbody>().includeLayers = 1 << GroundLayer;
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void ARigidbodysExcludeOutweighsItsCollidersInclude()
        {
            Ground();
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            ball.GetComponent<Rigidbody>().excludeLayers = 1 << GroundLayer;
            Run(80);
            AssertNoContact(ball);
        }

        [Fact]
        public void ABodyThatDetectsNoCollisionsIgnoresTheOtherSidesInclude()
        {
            var ground = Ground();
            ground.includeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 100;
            var ball = Ball();
            ball.GetComponent<Rigidbody>().detectCollisions = false;
            Run(80);
            AssertNoContact(ball);
        }

        [Fact]
        public void EachColliderOfACompoundBodyDecidesForItself()
        {
            const int LeftLayer = 10, RightLayer = 11;
            Physics.IgnoreLayerCollision(GroundLayer, LeftLayer);
            Physics.IgnoreLayerCollision(GroundLayer, RightLayer);
            Ground();
            var body = Make("body", new Vector3(0f, 2f, 0f), BallLayer);
            var left = Make("left", new Vector3(-1f, 2f, 0f), LeftLayer);
            var right = Make("right", new Vector3(1f, 2f, 0f), RightLayer);
            left.transform.SetParent(body.transform, true);
            right.transform.SetParent(body.transform, true);
            var leftSphere = left.AddComponent<SphereCollider>();
            leftSphere.radius = 0.5f;
            leftSphere.includeLayers = 1 << GroundLayer;
            var rightSphere = right.AddComponent<SphereCollider>();
            rightSphere.radius = 0.5f;
            var rb = body.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            var log = body.AddComponent<LayerOverrideRecorder>();
            Run(80);
            float y = body.transform.position.y;
            Assert.True(y > 0.9f && y < 1.1f, $"the body should rest on its left sphere, at y {y}");
            Assert.Contains("CollisionEnter:ground", log.log);
            foreach (var collision in log.collisions)
                for (int i = 0; i < collision.contactCount; i++)
                    Assert.Same(leftSphere, collision.GetContact(i).thisCollider);
        }

        // ------------------------------------------------------------------ changes

        [Fact]
        public void AnIncludeSetWhileFallingTakesEffect()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            Ground();
            var ball = Ball(3f);
            Run(5);
            Assert.True(ball.transform.position.y > 2f);
            ball.includeLayers = 1 << GroundLayer;
            Run(80);
            AssertContact(ball);
        }

        [Fact]
        public void PriorityChangesRefilterThePair()
        {
            var ground = Ground();
            ground.excludeLayers = 1 << BallLayer;
            ground.layerOverridePriority = 1;
            var ball = Ball(3f);
            ball.includeLayers = 1 << GroundLayer;
            ball.layerOverridePriority = 0;
            Run(5);
            Assert.True(ball.transform.position.y > 2f);
            // The ball's include now outranks the ground's exclude.
            ball.layerOverridePriority = 2;
            Run(80);
            AssertContact(ball);
            // And back: the ground's exclude outranks it, so the resting ball drops through. Woken,
            // as whether it would sleep on is not what this asserts.
            ground.layerOverridePriority = 3;
            ball.GetComponent<Rigidbody>().WakeUp();
            Run(80);
            Assert.True(ball.transform.position.y < -1f, $"the ball should fall through the ground, at y {ball.transform.position.y}");
        }

        // ------------------------------------------------------------------ the other filters

        [Fact]
        public void IgnoreCollisionOutweighsAnInclude()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var ground = Ground();
            var ball = Ball();
            ball.includeLayers = 1 << GroundLayer;
            Physics.IgnoreCollision(ball, ground);
            Run(80);
            AssertNoContact(ball);
        }

        [Fact]
        public void TriggersFollowTheSameDecision()
        {
            Physics.IgnoreLayerCollision(GroundLayer, BallLayer);
            var zone = Make("zone", Vector3.zero, GroundLayer).AddComponent<BoxCollider>();
            zone.size = new Vector3(2f, 2f, 2f);
            zone.isTrigger = true;
            zone.includeLayers = 1 << BallLayer;
            var ball = Ball(3f);
            Run(60);
            Assert.True(ball.transform.position.y < -1f);
            Assert.Contains("TriggerEnter:zone", Log(ball).log);
        }

        [Fact]
        public void ATriggerPairOfConflictingEqualPrioritiesMakesNoContact()
        {
            var zone = Make("zone", Vector3.zero, GroundLayer).AddComponent<BoxCollider>();
            zone.size = new Vector3(2f, 2f, 2f);
            zone.isTrigger = true;
            zone.excludeLayers = 1 << BallLayer;
            var ball = Ball(3f);
            ball.includeLayers = 1 << GroundLayer;
            Run(60);
            Assert.True(ball.transform.position.y < -1f);
            Assert.DoesNotContain("TriggerEnter:zone", Log(ball).log);
        }

        [Fact]
        public void QueriesUseTheirOwnLayerMask()
        {
            var ground = Ground();
            ground.excludeLayers = ~0;
            ground.layerOverridePriority = 50;
            Run(1);
            Assert.True(Physics.Raycast(new Vector3(0f, 5f, 0f), Vector3.down, out var hit, 10f, 1 << GroundLayer));
            Assert.Same(ground, hit.collider);
            Assert.False(Physics.Raycast(new Vector3(0f, 5f, 0f), Vector3.down, out _, 10f, ~(1 << GroundLayer)));
        }
    }
}
