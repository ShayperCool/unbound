using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class PhysicsConstraintStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        readonly List<GameObject> objects = new List<GameObject>();
        public PhysicsConstraintStateTests()
        {
            PhysicsWorld.Reset();
            ProjectInfo.UnityVersion = "6000.0.71f1";
        }
        public void Dispose()
        {
            foreach (var go in objects) Object.DestroyImmediate(go);
            PhysicsWorld.Reset();
            ProjectInfo.UnityVersion = release;
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
    }
}
