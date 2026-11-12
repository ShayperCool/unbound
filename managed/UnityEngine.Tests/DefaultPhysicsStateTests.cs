using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultPhysicsStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultPhysicsStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Fact]
        public void MaterialCopiesBelongToIndividualCollidersAndPreservePhysicalProperties()
        {
            var firstOwner = new GameObject("first"); var secondOwner = new GameObject("second");
            var source = new PhysicsMaterial("shared") { dynamicFriction = .2f, staticFriction = .3f,
                bounciness = .4f, frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum };
            PhysicsMaterial own = null, transferred = null;
            try
            {
                firstOwner.SetActive(false); secondOwner.SetActive(false);
                var first = firstOwner.AddComponent<BoxCollider>(); var second = secondOwner.AddComponent<CapsuleCollider>();
                first.sharedMaterial = source; second.sharedMaterial = source;
                own = first.material;
                Assert.NotSame(source, own); Assert.Same(own, first.material); Assert.Same(own, first.sharedMaterial);
                Assert.Equal("shared (Instance)", own.name); Assert.Equal(.3f, own.staticFriction);
                Assert.Equal(.4f, own.bounciness); Assert.Equal(PhysicsMaterialCombine.Maximum, own.frictionCombine);
                Assert.Equal(PhysicsMaterialCombine.Minimum, own.bounceCombine);
                own.dynamicFriction = .8f;
                Assert.Equal(.2f, source.dynamicFriction); Assert.Equal(.2f, second.sharedMaterial.dynamicFriction);
                second.sharedMaterial = own; transferred = second.material;
                Assert.NotSame(own, transferred); Assert.Same(transferred, second.material);
                Assert.Equal("shared (Instance) (Instance)", transferred.name);
                transferred.dynamicFriction = .9f;
                Assert.Equal(.8f, first.material.dynamicFriction);
                first.sharedMaterial = own; Assert.Same(own, first.material);
            }
            finally
            { Object.DestroyImmediate(firstOwner); Object.DestroyImmediate(secondOwner); Object.DestroyImmediate(source);
                Object.DestroyImmediate(own); Object.DestroyImmediate(transferred); }
        }

        [Fact]
        public void MissingMaterialGetsAnUnnamedReusableCopyAndNamedConstructorsKeepTheirInput()
        {
            var owner = new GameObject("material defaults"); var material = new PhysicsMaterial();
            var empty = new PhysicsMaterial(""); var named = new PhysicsMaterial("named");
            PhysicsMaterial copy = null;
            try
            {
                owner.SetActive(false); var collider = owner.AddComponent<SphereCollider>();
                Assert.Null(collider.sharedMaterial); copy = collider.material;
                Assert.Equal("", copy.name); Assert.Same(copy, collider.material); Assert.Same(copy, collider.sharedMaterial);
                Assert.Equal("DynamicMaterial", material.name); Assert.Equal("", empty.name); Assert.Equal("named", named.name);
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(material); Object.DestroyImmediate(empty);
                Object.DestroyImmediate(named); Object.DestroyImmediate(copy); }
        }

        [Fact]
        public void EmptyCallbackPayloadsThrowButValidStaticColliderPayloadsResolveTheirObjects()
        {
            Assert.Throws<NullReferenceException>(() => new Collision().gameObject);
            Assert.Throws<NullReferenceException>(() => new Collision().transform);
            Assert.Throws<NullReferenceException>(() => new ControllerColliderHit().gameObject);
            Assert.Throws<NullReferenceException>(() => new ControllerColliderHit().transform);
            Assert.Throws<NullReferenceException>(() => new ControllerColliderHit().rigidbody);
            var owner = new GameObject("callback payload");
            try
            {
                owner.SetActive(false); var collider = owner.AddComponent<BoxCollider>();
                var collision = new Collision { m_Collider = collider };
                var hit = new ControllerColliderHit { m_Collider = collider };
                Assert.Same(owner, collision.gameObject); Assert.Same(owner.transform, collision.transform);
                Assert.Same(owner, hit.gameObject); Assert.Same(owner.transform, hit.transform); Assert.Null(hit.rigidbody);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Theory]
        [InlineData("xDrive")]
        [InlineData("yDrive")]
        [InlineData("zDrive")]
        [InlineData("angularXDrive")]
        [InlineData("angularYZDrive")]
        [InlineData("slerpDrive")]
        public void JointDrivesNormalizeInputsWhenAssignedWithoutChangingTheStandaloneValue(string name)
        {
            var owner = new GameObject("drive ranges");
            try
            {
                owner.SetActive(false); var joint = owner.AddComponent<ConfigurableJoint>();
                var property = typeof(ConfigurableJoint).GetProperty(name);
                Assert.Equal(JointDrive.MaximumSetting, ((JointDrive)property.GetValue(joint)).maximumForce);
                foreach (var pair in new[] { (-3f, 0f), (27.5f, 27.5f), (float.MaxValue, JointDrive.MaximumSetting),
                    (float.PositiveInfinity, JointDrive.MaximumSetting), (float.NaN, float.NaN) })
                {
                    var input = pair.Item1; var expected = pair.Item2;
                    var setting = new JointDrive { positionSpring = input, positionDamper = input, maximumForce = input, useAcceleration = true };
                    property.SetValue(joint, setting); var result = (JointDrive)property.GetValue(joint);
                    Assert.Equal(expected, result.positionSpring); Assert.Equal(expected, result.positionDamper);
                    Assert.Equal(expected, result.maximumForce); Assert.True(result.useAcceleration);
                    Assert.Equal(input, setting.maximumForce);
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void LegacySolverPropertiesForwardReadsAndWritesAfterStateDiverges()
        {
            var owner = new GameObject("solver aliases");
            try
            {
                owner.SetActive(false); var body = owner.AddComponent<Rigidbody>();
                var count = typeof(Rigidbody).GetProperty("solverIterationCount");
                var velocity = typeof(Rigidbody).GetProperty("solverVelocityIterationCount");
                body.solverIterations = 13; body.solverVelocityIterations = 7;
                Assert.Equal(13, count.GetValue(body)); Assert.Equal(7, velocity.GetValue(body));
                count.SetValue(body, 9); velocity.SetValue(body, 3);
                Assert.Equal(9, body.solverIterations); Assert.Equal(3, body.solverVelocityIterations);
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
