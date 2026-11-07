using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class Physics2DMeasuredSimulationTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        readonly float fixedStep = Time.fixedDeltaTime;
        readonly List<GameObject> objects = new List<GameObject>();

        public Physics2DMeasuredSimulationTests()
        {
            Physics2DWorld.Reset();
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = Vector2.zero;
            Physics2D.autoSyncTransforms = false;
            Physics2D.useSubStepping = false;
            Physics2D.useSubStepContacts = false;
            Physics2D.simulationLayers = -1;
            Physics2D.defaultContactOffset = .01f;
        }

        public void Dispose()
        {
            foreach (var go in objects) Object.DestroyImmediate(go);
            Physics2DWorld.Reset();
            ProjectInfo.UnityVersion = release;
            Time.fixedDeltaTime = fixedStep;
            Physics2D.useSubStepping = false;
            Physics2D.useSubStepContacts = false;
            Physics2D.minSubStepFPS = 30;
            Physics2D.maxSubStepCount = 4;
            Physics2D.gravity = new Vector2(0, -9.81f);
        }

        GameObject Make(Vector2 position)
        {
            var go = new GameObject("measured-control"); go.transform.position = position; objects.Add(go); return go;
        }

        Rigidbody2D Body(Vector2 position, bool circle = false)
        {
            var go = Make(position); var rb = go.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.freezeRotation = true;
            if (circle) go.AddComponent<CircleCollider2D>().radius = .25f;
            else go.AddComponent<BoxCollider2D>().size = new Vector2(.5f, .5f);
            return rb;
        }

        [Theory]
        [InlineData(0f, false)]
        [InlineData(.1f, false)]
        [InlineData(.1f, true)]
        public void RoundedEdgesRetainTheirRadiusInSimulation(float radius, bool circle)
        {
            var edge = Make(Vector2.zero).AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(-4, 0), Vector2.zero, new Vector2(4, 0) }; edge.edgeRadius = radius;
            var rb = Body(new Vector2(-1, .27f + radius), circle); rb.gravityScale = 1; Physics2D.gravity = new Vector2(0, -10);
            for (int i = 0; i < 32; i++) Physics2D.Simulate(.02f);
            Assert.InRange(rb.position.y, .25f + radius, .28f + radius);
            Assert.InRange(rb.linearVelocity.y, -.001f, .001f);
        }

        [Theory]
        [InlineData(SimulationMode2D.FixedUpdate)]
        [InlineData(SimulationMode2D.Update)]
        public void ExplicitSimulationRejectsAutomaticModes(SimulationMode2D mode)
        {
            var rb = Body(Vector2.zero); rb.linearVelocity = Vector2.right;
            Physics2D.simulationMode = mode;
            Assert.False(Physics2D.Simulate(.1f));
            Assert.False(Physics2D.defaultPhysicsScene.Simulate(.1f, 1));
            Assert.Equal(Vector2.zero, rb.position);
            Assert.True(Physics2DWorld.Simulate(.1f, -1, fromLoop: true));
            Assert.InRange(rb.position.x, .099f, .101f);
        }

        public sealed class Receiver : MonoBehaviour
        {
            public int entered, exited;
            void OnTriggerEnter2D(Collider2D other) { entered++; }
            void OnTriggerExit2D(Collider2D other) { exited++; }
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 1)]
        public void SubStepContactsControlRefreshWithoutChangingIntegration(bool refresh, int exits)
        {
            var sensor = Make(new Vector2(.5f, 0)).AddComponent<BoxCollider2D>(); sensor.size = new Vector2(.1f, 2); sensor.isTrigger = true;
            var rb = Body(Vector2.zero); rb.linearVelocity = new Vector2(10, 0); var receiver = rb.gameObject.AddComponent<Receiver>();
            Physics2D.useSubStepping = true; Physics2D.useSubStepContacts = refresh;
            Physics2D.minSubStepFPS = 30; Physics2D.maxSubStepCount = 4;
            Assert.True(Physics2D.Simulate(.1f));
            Assert.Equal(3, Physics2D.defaultPhysicsScene.subStepCount);
            Assert.Equal(0f, Physics2D.defaultPhysicsScene.subStepLostTime);
            Assert.Equal(1, receiver.entered); Assert.Equal(exits, receiver.exited);
            Assert.InRange(rb.position.x, .999f, 1.001f);
            Physics2D.Simulate(.25f);
            Assert.Equal(4, Physics2D.defaultPhysicsScene.subStepCount);
            Assert.InRange(Physics2D.defaultPhysicsScene.subStepLostTime, .11666f, .11667f);
        }

        [Fact]
        public void DeferredMovesRestoreOnlyTheCommandedVelocityComponent()
        {
            var rb = Body(Vector2.zero); rb.freezeRotation = false;
            rb.gravityScale = 1; Physics2D.gravity = new Vector2(0, -10);
            rb.linearDamping = rb.angularDamping = 2; rb.linearVelocity = new Vector2(1, 2); rb.angularVelocity = 3;
            rb.MovePosition(new Vector2(3, 1));
            Physics2D.Simulate(.02f);
            Assert.InRange(rb.position.x, 2.9999f, 3.0001f); Assert.InRange(rb.position.y, .9959f, .9961f);
            Assert.Equal(new Vector2(1, 2), rb.linearVelocity);
            Assert.InRange(rb.angularVelocity, 2.8845f, 2.8848f);
            rb.MoveRotation(90); Physics2D.Simulate(.02f);
            Assert.InRange(rb.rotation, 89.999f, 90.001f);
            Assert.InRange(rb.linearVelocity.x, .9615f, .9616f);
            Assert.InRange(rb.angularVelocity, 2.8845f, 2.8848f);
            Assert.Equal(2f, rb.linearDamping); Assert.Equal(2f, rb.angularDamping);
        }

        [Fact]
        public void SlideCountsFreeIterationsAndPreservesUnusedDistance()
        {
            var wall = Make(new Vector2(2, 2)).AddComponent<BoxCollider2D>(); wall.size = new Vector2(1, 20);
            var rb = Body(new Vector2(0, 1)); rb.bodyType = RigidbodyType2D.Kinematic;
            var movement = new Rigidbody2D.SlideMovement { gravity = Vector2.zero, surfaceAnchor = Vector2.zero, maxIterations = 1, useNoMove = true };
            var result = rb.Slide(new Vector2(4, -1), .5f, movement);
            Assert.Equal(1, result.iterationsUsed);
            Assert.InRange(result.position.x, 1.2352f, 1.2354f);
            Assert.InRange(result.remainingVelocity.y, -.7884f, -.7881f);
            movement.maxIterations = 2; result = rb.Slide(new Vector2(4, -1), .5f, movement);
            Assert.Equal(2, result.iterationsUsed); Assert.Equal(Vector2.zero, result.remainingVelocity);
            Assert.InRange(result.position.y, .4999f, .5001f);
            movement.maxIterations = 0;
            Assert.Throws<ArgumentException>(() => rb.Slide(Vector2.right, .1f, movement));
        }

        [Fact]
        public void RepeatedCompoundSlidesReuseTheirQueryStorage()
        {
            var floor = Make(new Vector2(0, -1)).AddComponent<BoxCollider2D>(); floor.size = new Vector2(20, 1);
            var rb = Body(Vector2.zero, circle: true); rb.bodyType = RigidbodyType2D.Kinematic;
            var child = Make(new Vector2(.5f, 0)); child.transform.SetParent(rb.transform, true); child.AddComponent<CircleCollider2D>();
            var movement = new Rigidbody2D.SlideMovement { useNoMove = true, gravity = Vector2.zero, surfaceAnchor = Vector2.zero };
            Physics2D.SyncTransforms();
            for (int i = 0; i < 32; i++) rb.Slide(new Vector2(2, -1), .5f, movement);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++) rb.Slide(new Vector2(2, -1), .5f, movement);
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }

        [Fact]
        public void SlideStructuresUseTheCapturedSerializedNames()
        {
            string movement = JsonUtility.ToJson(new Rigidbody2D.SlideMovement());
            Assert.Contains("<maxIterations>k__BackingField", movement);
            var copy = JsonUtility.FromJson<Rigidbody2D.SlideMovement>(movement);
            Assert.Equal(3, copy.maxIterations); Assert.Equal(Vector2.down, copy.surfaceAnchor);
            string results = JsonUtility.ToJson(default(Rigidbody2D.SlideResults));
            Assert.Contains("<iterationsUsed>k__BackingField", results);
            Assert.DoesNotContain("slideHit", results); Assert.DoesNotContain("surfaceHit", results);
        }

        [Theory]
        [InlineData(1, 2f, 0f)]
        [InlineData(256, 2.02f, 0f)]
        [InlineData(257, 2.01f, .01f)]
        public void AJointRunsOnlyWhenBothBodyLayersParticipate(int layers, float ownerX, float otherX)
        {
            var other = Body(Vector2.zero);
            var owner = Body(new Vector2(2, 0)); owner.gameObject.layer = 8; owner.linearVelocity = Vector2.right;
            var joint = owner.gameObject.AddComponent<DistanceJoint2D>(); joint.connectedBody = other;
            joint.autoConfigureConnectedAnchor = false; joint.anchor = joint.connectedAnchor = Vector2.zero;
            joint.autoConfigureDistance = false; joint.distance = 2;
            Physics2D.Simulate(.02f, layers);
            Assert.InRange(owner.position.x, ownerX - .0001f, ownerX + .0001f);
            Assert.InRange(other.position.x, otherX - .0001f, otherX + .0001f);
        }

        [Fact]
        public void AChildOnASimulatedLayerWaitsForItsBodyLayer()
        {
            var sensor = Make(Vector2.zero).AddComponent<BoxCollider2D>(); sensor.size = new Vector2(4, 4); sensor.isTrigger = true;
            var rb = Body(Vector2.zero); rb.gameObject.layer = 8; rb.GetComponent<Collider2D>().enabled = false;
            var child = Make(Vector2.zero); child.transform.SetParent(rb.transform, true); child.AddComponent<BoxCollider2D>();
            var receiver = rb.gameObject.AddComponent<Receiver>();
            Physics2D.Simulate(.02f, 1); Assert.Equal(0, receiver.entered);
            Physics2D.Simulate(.02f, 257); Assert.Equal(1, receiver.entered);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.MaxValue)]
        public void ExceptionalFixedIntervalsCannotBecomeNegativeClockSteps(float value)
        {
            Time.fixedDeltaTime = value;
            if (float.IsNaN(value)) Assert.True(double.IsNaN(Time.FixedClockDuration));
            else Assert.True(Time.FixedClockDuration > 0);
        }
    }
}
