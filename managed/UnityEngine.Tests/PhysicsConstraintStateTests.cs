using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class PhysicsConstraintStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        readonly SimulationMode simulationMode = Physics.simulationMode;
        readonly List<GameObject> objects = new List<GameObject>();
        public PhysicsConstraintStateTests()
        {
            PhysicsWorld.Reset();
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Physics.simulationMode = SimulationMode.Script;
        }
        public void Dispose()
        {
            foreach (var go in objects) Object.DestroyImmediate(go);
            PhysicsWorld.Reset();
            ProjectInfo.UnityVersion = release;
            Physics.simulationMode = simulationMode;
        }
        [Theory]
        [InlineData(RigidbodyConstraints.None, 1, 2, 3)]
        [InlineData(RigidbodyConstraints.FreezePositionX, 0, 2, 3)]
        [InlineData(RigidbodyConstraints.FreezePositionY, 1, 0, 3)]
        [InlineData(RigidbodyConstraints.FreezePositionZ, 1, 2, 0)]
        [InlineData(RigidbodyConstraints.FreezeAll, 0, 0, 0)]
        public void VelocityAssignmentsRespectFrozenPositionAxes(RigidbodyConstraints constraint, float x, float y, float z)
        {
            var go=new GameObject("velocity-axes");objects.Add(go);
            var body=go.AddComponent<Rigidbody>();body.constraints=constraint;
            body.linearVelocity=new Vector3(1,2,3);
            Assert.Equal(new Vector3(x,y,z),body.linearVelocity);
            body.constraints=RigidbodyConstraints.None;
            Assert.Equal(new Vector3(x,y,z),body.linearVelocity);
        }
        [Fact]
        public void AngularAssignmentUsesTheAssignedInertiaAxes()
        {
            var go=new GameObject("angular-axes");objects.Add(go);
            var body=go.AddComponent<Rigidbody>();body.rotation=Quaternion.Euler(20,30,40);
            body.inertiaTensorRotation=Quaternion.Euler(15,25,35);
            body.constraints=RigidbodyConstraints.FreezeRotationX;
            body.angularVelocity=new Vector3(4,5,6);
            // physicsccdhinge constraints/velocity/16/True, Unity 6000.0.71f1.
            Assert.InRange((body.angularVelocity-new Vector3(.1990323f,2.38449025f,6.99914646f)).magnitude,0,1e-4f);
        }
        [Fact]
        public void RepeatedAngularAssignmentsReuseImplicitInertiaAxes()
        {
            var go=new GameObject("implicit-axes");objects.Add(go);
            go.AddComponent<BoxCollider>();
            var body=go.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeRotationX;
            var velocity=new Vector3(4,5,6);
            body.angularVelocity=velocity;
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++)body.angularVelocity=velocity;
            Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        }
        [Fact]
        public void TransferredColliderUpdatesBothBodiesMassAndKeepsItsGeometry()
        {
            var first = new GameObject("first"); objects.Add(first);
            first.AddComponent<BoxCollider>();
            var firstBody = first.AddComponent<Rigidbody>();
            var second = new GameObject("second"); objects.Add(second);
            second.transform.position = Vector3.right * 10;
            second.AddComponent<BoxCollider>();
            var secondBody = second.AddComponent<Rigidbody>();
            var child = new GameObject("child"); objects.Add(child);
            child.transform.SetParent(first.transform, false);
            child.transform.localPosition = Vector3.right * 2;
            var collider = child.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Assert.Equal(Vector3.right, firstBody.centerOfMass);
            var shape = collider.m_Shape;

            child.transform.SetParent(second.transform, false);
            Physics.SyncTransforms();

            Assert.Equal(shape, collider.m_Shape);
            Assert.Equal(Vector3.zero, firstBody.centerOfMass);
            Assert.Equal(Vector3.right, secondBody.centerOfMass);
            Assert.Equal(Vector3.right * 12, collider.bounds.center);
        }
        [Theory]
        [InlineData(RigidbodyConstraints.FreezeRotation, 0, 0, 0)]
        [InlineData(RigidbodyConstraints.FreezeRotationX, 0, 2, 3)]
        [InlineData(RigidbodyConstraints.FreezeRotationY, 1, 0, 3)]
        [InlineData(RigidbodyConstraints.FreezeRotationZ, 1, 2, 0)]
        public void NewAngularConstraintsProjectVelocityBeforeTheFirstStep(RigidbodyConstraints constraint, float x, float y, float z)
        {
            var go = new GameObject("angular-transition"); objects.Add(go);
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.angularDamping = 0;
            body.angularVelocity = new Vector3(1, 2, 3);
            body.constraints = constraint;
            var expected = new Vector3(x, y, z);
            Assert.Equal(expected, body.angularVelocity);

            Physics.Simulate(.02f);

            Assert.InRange((body.angularVelocity - expected).magnitude, 0, 1e-5f);
            var q = body.rotation;
            if (constraint != RigidbodyConstraints.FreezeRotation) Assert.True(q.w < .99999f);
            if ((constraint & RigidbodyConstraints.FreezeRotationX) != 0) Assert.InRange(Mathf.Abs(q.x), 0, 1e-6f);
            if ((constraint & RigidbodyConstraints.FreezeRotationY) != 0) Assert.InRange(Mathf.Abs(q.y), 0, 1e-6f);
            if ((constraint & RigidbodyConstraints.FreezeRotationZ) != 0) Assert.InRange(Mathf.Abs(q.z), 0, 1e-6f);
        }
        [Fact]
        public void ConstraintTransitionsUseTheCurrentWorldMassFrame()
        {
            var go = new GameObject("rotated-transition"); objects.Add(go);
            var body = go.AddComponent<Rigidbody>();
            body.rotation = Quaternion.Euler(20, 30, 40);
            body.inertiaTensor = new Vector3(1, 2, 3);
            body.inertiaTensorRotation = Quaternion.Euler(15, 25, 35);
            body.angularVelocity = new Vector3(4, 5, 6);
            body.constraints = RigidbodyConstraints.FreezeRotationX;
            // physicsccdhinge constraints/transition/16/True, Unity 6000.0.71f1.
            Assert.InRange((body.angularVelocity - new Vector3(2.334454f, -.5496373f, 6.13653469f)).magnitude, 0, 1e-4f);
        }
        [Theory]
        [InlineData(RigidbodyConstraints.FreezePositionX, 0, 2, 3)]
        [InlineData(RigidbodyConstraints.FreezePosition, 0, 0, 0)]
        [InlineData(RigidbodyConstraints.FreezeAll, 0, 0, 0)]
        public void NewPositionConstraintsProjectExistingVelocity(RigidbodyConstraints constraint, float x, float y, float z)
        {
            var go = new GameObject("linear-transition"); objects.Add(go);
            var body = go.AddComponent<Rigidbody>();
            body.linearVelocity = new Vector3(1, 2, 3);
            body.constraints = constraint;
            Assert.Equal(new Vector3(x, y, z), body.linearVelocity);
        }
        [Fact]
        public void RepeatedConstraintAssignmentsReuseMassProperties()
        {
            var go = new GameObject("transition-allocations"); objects.Add(go);
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotationX;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) body.constraints = RigidbodyConstraints.FreezeRotationX;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
