using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Records the 3D physics messages its GameObject gets, in order.</summary>
    public class ContactRecorder3D : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        public readonly List<Collision> collisions = new List<Collision>();

        void OnCollisionEnter(Collision c) { log.Add("CollisionEnter:" + c.collider.name); collisions.Add(c); }
        void OnCollisionExit(Collision c) { log.Add("CollisionExit:" + c.collider.name); }
        void OnTriggerEnter(Collider other) { log.Add("TriggerEnter:" + other.name); }
        void OnTriggerExit(Collider other) { log.Add("TriggerExit:" + other.name); }
    }

    public class TriggerStepRecorder3D : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        public bool disableOnEnter;
        void OnTriggerEnter(Collider other)
        {
            log.Add("Enter");
            if (disableOnEnter) GetComponent<Collider>().enabled = false;
        }
        void OnTriggerStay(Collider other) { log.Add("Stay"); }
        void OnTriggerExit(Collider other) { log.Add("Exit"); }
    }

    public class ContactPhaseRecorder3D : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        void OnCollisionEnter(Collision other) { log.Add("CE"); }
        void OnCollisionExit(Collision other) { log.Add("CX"); }
        void OnTriggerEnter(Collider other) { log.Add("TE"); }
        void OnTriggerStay(Collider other) { log.Add("TS"); }
        void OnTriggerExit(Collider other) { log.Add("TX"); }
    }

    /// <summary>
    /// 3D physics: Box3D behind Unity's components. The world is global, so every test of the class
    /// runs alone (xunit runs a class's tests one after another) and destroys what it made.
    /// </summary>
    public class PhysicsTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly List<GameObject> m_Made = new List<GameObject>();

        public PhysicsTests()
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Physics.queriesHitTriggers = true;
            Physics.autoSyncTransforms = true;
            for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Physics.IgnoreLayerCollision(i, j, false);
            Physics.simulationMode = SimulationMode.Script;
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            Physics.Simulate(Dt);
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

        GameObject Ground(string name = "ground", float y = 0f)
        {
            var go = Make(name, new Vector3(0f, y, 0f));
            go.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
            return go;
        }

        GameObject Ball(string name, Vector3 position, float radius = 0.5f)
        {
            var go = Make(name, position);
            go.AddComponent<SphereCollider>().radius = radius;
            go.AddComponent<Rigidbody>();
            return go;
        }

        [Fact]
        public void ABallFallsAndRestsOnTheGround()
        {
            Ground();
            var ball = Ball("ball", new Vector3(0f, 3f, 0f));
            var recorder = ball.AddComponent<ContactRecorder3D>();
            Run(150);
            // The ground's top is at 0.5; the ball's center rests a radius above it.
            Assert.InRange(ball.transform.position.y, 0.95f, 1.05f);
            Assert.True(ball.GetComponent<Rigidbody>().linearVelocity.magnitude < 0.05f);
            Assert.Equal("CollisionEnter:ground", recorder.log[0]);
            var contact = recorder.collisions[0].GetContact(0);
            // Seen from the ball, the normal points from the ground towards it.
            Assert.True(contact.normal.y > 0.9f, $"normal {contact.normal}");
            Assert.True(recorder.collisions[0].relativeVelocity.y > 1f, $"relative velocity {recorder.collisions[0].relativeVelocity}");
            Assert.Same(ball.GetComponent<SphereCollider>(), contact.thisCollider);
        }

        [Fact]
        public void TriggersReportEnterAndExitWithoutStoppingBodies()
        {
            var zone = Make("zone", new Vector3(0f, 2f, 0f));
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var ball = Ball("ball", new Vector3(0f, 5f, 0f), 0.25f);
            var recorder = ball.AddComponent<ContactRecorder3D>();
            Run(80);
            Assert.Contains("TriggerEnter:zone", recorder.log);
            Assert.Contains("TriggerExit:zone", recorder.log);
            Assert.True(ball.transform.position.y < 0f);
        }

        [Fact]
        public void StaticTriggersIgnoreStaticColliders()
        {
            var zone = Make("zone", Vector3.zero);
            zone.AddComponent<BoxCollider>().isTrigger = true;
            var wall = Make("wall", Vector3.zero);
            wall.AddComponent<BoxCollider>();
            var recorder = zone.AddComponent<ContactRecorder3D>();
            Run(3);
            Assert.Empty(recorder.log);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SleepingTriggersStayOnEnterAndEveryOverlappingSimulation(bool otherKinematic)
        {
            var zone = Make("zone", Vector3.zero);
            zone.AddComponent<BoxCollider>().isTrigger = true;
            zone.AddComponent<Rigidbody>().isKinematic = true;
            var other = Make("other", new Vector3(0.8f, 0f, 0f));
            other.AddComponent<BoxCollider>();
            if (otherKinematic) other.AddComponent<Rigidbody>().isKinematic = true;
            var a = zone.AddComponent<TriggerStepRecorder3D>();
            var b = other.AddComponent<TriggerStepRecorder3D>();
            Physics.SyncTransforms();
            Physics.Simulate(Dt);
            Assert.Equal(new[] { "Enter", "Stay" }, a.log);
            Assert.Equal(a.log, b.log);
            a.log.Clear(); b.log.Clear();
            Run(7);
            Assert.Equal(new[] { "Stay", "Stay", "Stay", "Stay", "Stay", "Stay", "Stay" }, a.log);
            Assert.Equal(a.log, b.log);
            a.log.Clear(); b.log.Clear();
            zone.transform.position = new Vector3(-3f, 0f, 0f);
            Physics.SyncTransforms();
            Physics.Simulate(Dt);
            Assert.Equal(new[] { "Exit" }, a.log);
            Assert.Equal(a.log, b.log);
            a.log.Clear(); b.log.Clear();
            zone.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            Physics.Simulate(Dt);
            Assert.Equal(new[] { "Enter", "Stay" }, a.log);
            Assert.Equal(a.log, b.log);
        }

        [Fact]
        public void DisablingATriggerDuringEnterRetainsThatSimulationsStay()
        {
            var zone = Make("zone", Vector3.zero);
            zone.AddComponent<BoxCollider>().isTrigger = true;
            zone.AddComponent<Rigidbody>().isKinematic = true;
            var other = Make("other", new Vector3(0.8f, 0f, 0f));
            other.AddComponent<BoxCollider>();
            var a = zone.AddComponent<TriggerStepRecorder3D>();
            var b = other.AddComponent<TriggerStepRecorder3D>();
            a.disableOnEnter = true;
            Physics.SyncTransforms();
            Physics.Simulate(Dt);
            Assert.Equal(new[] { "Enter", "Stay" }, a.log);
            Assert.Equal(a.log, b.log);
            a.log.Clear(); b.log.Clear();
            Run(3);
            Assert.Empty(a.log);
            Assert.Empty(b.log);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void KinematicSettersApplyEverySleepWakeTransitionImmediately(int lifetime)
        {
            var go = Make("body", Vector3.zero);
            if (lifetime == 1) go.SetActive(false);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            if (lifetime == 1) go.SetActive(true);
            if (lifetime == 2)
            {
                var clone = Object.Instantiate(go); m_Made.Add(clone);
                go.SetActive(false); rb = clone.GetComponent<Rigidbody>();
            }
            Physics.SyncTransforms();
            rb.linearVelocity = new Vector3(1f, 2f, 3f);
            rb.angularVelocity = new Vector3(0.4f, 0.5f, 0.6f);
            rb.isKinematic = true;
            Assert.Equal(Vector3.zero, rb.linearVelocity);
            Assert.Equal(Vector3.zero, rb.angularVelocity);
            Assert.True(rb.IsSleeping());
            rb.isKinematic = false;
            Assert.Equal(Vector3.zero, rb.linearVelocity);
            Assert.Equal(Vector3.zero, rb.angularVelocity);
            Assert.False(rb.IsSleeping());
            rb.linearVelocity = new Vector3(3f, 2f, 1f);
            rb.angularVelocity = new Vector3(0.6f, 0.5f, 0.4f);
            rb.isKinematic = true;
            rb.isKinematic = false;
            Assert.Equal(Vector3.zero, rb.linearVelocity);
            Assert.Equal(Vector3.zero, rb.angularVelocity);
            Assert.False(rb.IsSleeping());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActivatedKinematicBodiesSettleWithoutDisplacement(bool clone)
        {
            var position = new Vector3(3f, 4f, 5f);
            var go = Make("kinematic", position);
            if (!clone) go.SetActive(false);
            go.AddComponent<BoxCollider>().isTrigger = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            if (clone)
            {
                var copy = Object.Instantiate(go); m_Made.Add(copy);
                go.SetActive(false); rb = copy.GetComponent<Rigidbody>();
            }
            else go.SetActive(true);
            Physics.SyncTransforms();
            Assert.False(rb.IsSleeping());
            for (int step = 0; step < 3; step++)
            {
                Physics.Simulate(Dt);
                Assert.Equal(position, rb.position);
                Assert.Equal(Vector3.zero, rb.linearVelocity);
                Assert.Equal(Vector3.zero, rb.angularVelocity);
                Assert.Equal(step == 2, rb.IsSleeping());
            }
        }

        // Native KinematicSleep.cs records both immediate state and twenty settling steps.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void KinematicMoveWakesImmediatelyAndMergesPositionAndRotation(bool rotationFirst)
        {
            var go = Make("kinematic", Vector3.zero);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            Run(20);
            Assert.True(rb.IsSleeping());
            var target = new Vector3(2f, 0f, 0f);
            var turn = new Quaternion(0f, 1f, 0f, 0f);
            if (rotationFirst) { rb.MoveRotation(turn); rb.MovePosition(target); }
            else { rb.MovePosition(target); rb.MoveRotation(turn); }
            Assert.False(rb.IsSleeping());
            Assert.Equal(Vector3.zero, rb.position);
            Assert.Equal(Quaternion.identity, rb.rotation);
            Physics.SyncTransforms();
            Assert.Equal(Vector3.zero, rb.position);
            for (int step = 0; step < 20; step++)
            {
                Physics.Simulate(Dt);
                Assert.Equal(target, rb.position);
                Assert.Equal(turn, rb.rotation);
                Assert.Equal(step >= 2, rb.IsSleeping());
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void KinematicTeleportsWakeAndReplaceOnlyTheirTargetComponents(int setter)
        {
            var go = Make("kinematic", Vector3.zero);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            Run(20);
            var target = new Vector3(2f, 0f, 0f);
            var teleport = new Vector3(4f, 0f, 0f);
            var turn = new Quaternion(0f, 1f, 0f, 0f);
            rb.Move(target, turn);
            if (setter == 0) { go.transform.position = teleport; Physics.SyncTransforms(); }
            else if (setter == 1) rb.position = teleport;
            else rb.rotation = turn;
            Assert.False(rb.IsSleeping());
            for (int step = 0; step < 20; step++)
            {
                Physics.Simulate(Dt);
                Assert.Equal(setter == 2 ? target : teleport, rb.position);
                Assert.Equal(setter == 0 ? Quaternion.identity : turn, rb.rotation);
                Assert.Equal(step >= 2, rb.IsSleeping());
            }
        }

        [Fact]
        public void KinematicSameTransformAndRepeatedSyncDoNotWakeButBodyAssignmentDoes()
        {
            var go = Make("kinematic", Vector3.zero);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true;
            Run(20);
            go.transform.position = go.transform.position;
            Physics.SyncTransforms(); Physics.SyncTransforms();
            Assert.True(rb.IsSleeping());
            rb.position = rb.position;
            Assert.False(rb.IsSleeping());
            for (int step = 0; step < 20; step++)
            {
                Physics.Simulate(Dt);
                Assert.Equal(Vector3.zero, rb.position);
                Assert.Equal(Vector3.zero, rb.linearVelocity);
                Assert.Equal(step >= 2, rb.IsSleeping());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DynamicMoveTeleportsAndWakesBeforeSimulation(bool sleeping)
        {
            var go = Make("dynamic", Vector3.zero);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>(); rb.useGravity = false;
            Run(20);
            if (sleeping) rb.Sleep(); else rb.WakeUp();
            var target = new Vector3(2f, 0f, 0f);
            rb.MovePosition(target);
            Assert.False(rb.IsSleeping());
            Assert.Equal(target, rb.position);
            Physics.Simulate(Dt);
            Assert.Equal(target, rb.position);
        }

        [Fact]
        public void DynamicMoveThenSleepKeepsTheTransformUnpublished()
        {
            var go = Make("dynamic", Vector3.zero);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>(); rb.useGravity = false;
            Run(20);
            var target = new Vector3(2f, 0f, 0f);
            rb.MovePosition(target); rb.Sleep();
            for (int step = 0; step < 20; step++)
            {
                Physics.Simulate(Dt);
                Assert.True(rb.IsSleeping());
                Assert.Equal(target, rb.position);
                Assert.Equal(Vector3.zero, go.transform.position);
            }
        }

        [Fact]
        public void TriggerConversionsKeepBothMessageKindsAndTriggerPairsReenter()
        {
            var a = Make("a", Vector3.zero); var b = Make("b", new Vector3(0.8f, 0f, 0f));
            var ac = a.AddComponent<BoxCollider>(); var bc = b.AddComponent<BoxCollider>();
            foreach (var go in new[] { a, b })
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll; rb.sleepThreshold = 0f;
            }
            var ar = a.AddComponent<ContactPhaseRecorder3D>(); var br = b.AddComponent<ContactPhaseRecorder3D>();
            void Step(params string[] expected)
            {
                ar.log.Clear(); br.log.Clear(); Physics.SyncTransforms(); Physics.Simulate(Dt);
                Assert.Equal(expected, ar.log); Assert.Equal(expected, br.log);
            }
            Step("CE");
            ac.isTrigger = true; Step("TE", "TS", "CX");
            bc.isTrigger = true; Step("TS");
            ac.isTrigger = false; Step("TS");
            ac.isTrigger = true; bc.isTrigger = false; Step("TS");
            bc.isTrigger = true; Step("TS");
            b.transform.position = new Vector3(3f, 0f, 0f); Step("TX");
            b.transform.position = new Vector3(0.8f, 0f, 0f); Step("TE", "TS");
            ac.isTrigger = false; bc.isTrigger = false; Step("TX", "CE");
            Physics.IgnoreCollision(ac, bc, true); Step("CX");
            Physics.IgnoreCollision(ac, bc, false); Step("CE");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IgnoredTriggerPairsSurviveColliderAndGameObjectDeactivation(bool wholeObject)
        {
            var a = Make("a", Vector3.zero); var b = Make("b", new Vector3(0.8f, 0f, 0f));
            var ac = a.AddComponent<BoxCollider>(); var bc = b.AddComponent<BoxCollider>();
            ac.isTrigger = true;
            foreach (var go in new[] { a, b })
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll;
            }
            var ar = a.AddComponent<TriggerStepRecorder3D>(); var br = b.AddComponent<TriggerStepRecorder3D>();
            Physics.SyncTransforms(); Physics.IgnoreCollision(ac, bc, true);
            Physics.Simulate(Dt);
            Assert.Empty(ar.log); Assert.Empty(br.log);
            if (wholeObject) a.SetActive(false); else ac.enabled = false;
            Assert.True(Physics.GetIgnoreCollision(ac, bc));
            if (wholeObject) a.SetActive(true); else ac.enabled = true;
            Assert.True(Physics.GetIgnoreCollision(ac, bc));
            Physics.Simulate(Dt);
            Assert.Empty(ar.log); Assert.Empty(br.log);
            Physics.IgnoreCollision(ac, bc, false); Physics.Simulate(Dt);
            Assert.Equal(new[] { "Enter", "Stay" }, ar.log);
            Assert.Equal(ar.log, br.log);
        }

        [Fact]
        public void RaycastsHitTheNearestColliderOnTheirLayers()
        {
            var near = Make("near", new Vector3(0f, 0f, 5f));
            near.AddComponent<BoxCollider>();
            near.layer = 8;
            var far = Make("far", new Vector3(0f, 0f, 10f));
            far.AddComponent<BoxCollider>();

            Assert.True(Physics.Raycast(Vector3.zero, Vector3.forward, out var hit, 100f));
            Assert.Equal("near", hit.collider.name);
            Assert.Equal(4.5f, hit.distance, 3);
            Assert.Equal(new Vector3(0f, 0f, 4.5f), hit.point);
            Assert.True(hit.normal.z < -0.99f);
            Assert.Same(near.transform, hit.transform);

            Assert.True(Physics.Raycast(Vector3.zero, Vector3.forward, out hit, 100f, ~(1 << 8)));
            Assert.Equal("far", hit.collider.name);
            Assert.False(Physics.Raycast(Vector3.zero, Vector3.forward, 4f));
            Assert.Equal(2, Physics.RaycastAll(Vector3.zero, Vector3.forward, 100f).Length);
            var buffer = new RaycastHit[1];
            Assert.Equal(1, Physics.RaycastNonAlloc(new Ray(Vector3.zero, Vector3.forward), buffer, 100f));
        }

        [Fact]
        public void QueriesHitTriggersAsAsked()
        {
            var zone = Make("zone", new Vector3(0f, 0f, 3f));
            zone.AddComponent<BoxCollider>().isTrigger = true;
            Assert.True(Physics.Raycast(Vector3.zero, Vector3.forward, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide));
            Assert.False(Physics.Raycast(Vector3.zero, Vector3.forward, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
            Physics.queriesHitTriggers = false;
            Assert.False(Physics.Raycast(Vector3.zero, Vector3.forward, 10f));
        }

        [Fact]
        public void ShapeQueriesFindOverlapsAndCasts()
        {
            var box = Make("box", new Vector3(3f, 0f, 0f));
            box.AddComponent<BoxCollider>();
            var sphere = Make("sphere", new Vector3(-3f, 0f, 0f));
            sphere.AddComponent<SphereCollider>();

            var found = Physics.OverlapSphere(new Vector3(2.2f, 0f, 0f), 0.5f);
            Assert.Single(found);
            Assert.Equal("box", found[0].name);
            Assert.Empty(Physics.OverlapSphere(Vector3.zero, 1f));
            Assert.True(Physics.CheckBox(new Vector3(-3f, 0f, 0f), Vector3.one * 0.1f));

            Assert.True(Physics.SphereCast(Vector3.zero, 0.5f, Vector3.right, out var hit, 10f));
            Assert.Equal("box", hit.collider.name);
            // Box3D's casts stop within its linear slop (5 mm) of the surface.
            Assert.InRange(hit.distance, 1.99f, 2.01f);
            Assert.True(Physics.BoxCast(Vector3.zero, Vector3.one * 0.25f, Vector3.left, out hit, Quaternion.identity, 10f));
            Assert.Equal("sphere", hit.collider.name);
            Assert.InRange(hit.distance, 2.24f, 2.26f);
            Assert.True(Physics.CapsuleCast(new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), 0.25f, Vector3.right, 10f));
        }

        [Fact]
        public void IgnoredPairsPassThroughEachOther()
        {
            var ground = Ground();
            var ball = Ball("ball", new Vector3(0f, 2f, 0f));
            Physics.IgnoreCollision(ball.GetComponent<Collider>(), ground.GetComponent<Collider>());
            Assert.True(Physics.GetIgnoreCollision(ground.GetComponent<Collider>(), ball.GetComponent<Collider>()));
            Run(60);
            Assert.True(ball.transform.position.y < -1f);
        }

        [Fact]
        public void LayersThatIgnoreEachOtherDoNotCollide()
        {
            var ground = Ground();
            ground.layer = 9;
            var ball = Ball("ball", new Vector3(0f, 2f, 0f));
            ball.layer = 10;
            Physics.IgnoreLayerCollision(9, 10);
            Run(60);
            Assert.True(ball.transform.position.y < -1f);
        }

        [Fact]
        public void ImpulsesChangeVelocityByTheirMass()
        {
            var ball = Ball("ball", Vector3.zero);
            var rb = ball.GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 2f;
            rb.AddForce(new Vector3(4f, 0f, 0f), ForceMode.Impulse);
            rb.AddForce(new Vector3(1f, 0f, 0f), ForceMode.VelocityChange);
            // As in Unity (PhysX), forces act at the next step: the velocity has not changed yet.
            Assert.Equal(0f, rb.linearVelocity.x, 3);
            Run(1);
            Assert.Equal(3f, rb.linearVelocity.x, 3);
            Run(49);
            Assert.InRange(ball.transform.position.x, 2.9f, 3.1f);
        }

        [Fact]
        public void KinematicBodiesFollowMovePosition()
        {
            var go = Make("platform", Vector3.zero);
            go.AddComponent<BoxCollider>();
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.MovePosition(new Vector3(1f, 0f, 0f));
            Run(1);
            Assert.Equal(1f, go.transform.position.x, 3);
            // Gravity does not move kinematic bodies.
            Run(10);
            Assert.Equal(0f, go.transform.position.y, 3);
        }

        [Fact]
        public void ChildCollidersAttachToTheParentRigidbody()
        {
            var parent = Make("parent", new Vector3(0f, 5f, 0f));
            var rb = parent.AddComponent<Rigidbody>();
            var child = Make("child", Vector3.zero);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = new Vector3(1f, 0f, 0f);
            var box = child.AddComponent<BoxCollider>();
            Run(1);
            Assert.Same(rb, box.attachedRigidbody);
            // The child's box moves the center of mass off the parent's origin.
            Assert.Equal(1f, rb.centerOfMass.x, 3);
            Assert.True(parent.transform.position.y < 5f);
        }

        [Fact]
        public void FrozenAxesDoNotMove()
        {
            var ball = Ball("ball", new Vector3(0f, 5f, 0f));
            var rb = ball.GetComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezePositionY;
            rb.linearVelocity = new Vector3(1f, 0f, 0f);
            Run(10);
            Assert.Equal(5f, ball.transform.position.y, 3);
            Assert.True(ball.transform.position.x > 0.1f);
        }

        [Fact]
        public void BouncyMaterialsBounce()
        {
            var ground = Ground();
            var ball = Ball("ball", new Vector3(0f, 3f, 0f));
            var bouncy = new PhysicsMaterial("bouncy") { bounciness = 1f, bounceCombine = PhysicsMaterialCombine.Maximum };
            ball.GetComponent<Collider>().sharedMaterial = bouncy;
            float highest = 0f;
            bool falling = true;
            for (int i = 0; i < 150; i++)
            {
                Run(1);
                float y = ball.transform.position.y;
                float vy = ball.GetComponent<Rigidbody>().linearVelocity.y;
                if (falling && vy > 0f) falling = false;
                if (!falling) highest = Mathf.Max(highest, y);
            }
            Assert.True(highest > 2f, $"bounced to {highest}");
        }

        [Fact]
        public void BoundsAndClosestPointFollowTheShape()
        {
            var go = Make("box", new Vector3(1f, 2f, 3f));
            go.transform.localScale = new Vector3(2f, 1f, 1f);
            var box = go.AddComponent<BoxCollider>();
            Assert.Equal(new Vector3(1f, 2f, 3f), box.bounds.center);
            Assert.Equal(2f, box.bounds.size.x, 3);
            var p = box.ClosestPoint(new Vector3(10f, 2f, 3f));
            Assert.Equal(2f, p.x, 3);
            Assert.True(box.Raycast(new Ray(new Vector3(10f, 2f, 3f), Vector3.left), out var hit, 100f));
            Assert.Equal(8f, hit.distance, 3);
        }
    }
}
