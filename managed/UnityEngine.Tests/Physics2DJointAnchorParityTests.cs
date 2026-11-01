using Xunit;
using J2 = Unbound.Box2D.Dynamics.Joints;

namespace UnityEngine.Tests
{
    public class Physics2DJointAnchorParityTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RotatedJointDefinitionsRetainScaledLocalAnchors(bool fixedJoint)
        {
            var root = new GameObject("root");
            var go = new GameObject("body"); var other = new GameObject("other");
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                go.transform.SetParent(root.transform, false); go.transform.localPosition = new Vector3(1, -3, 0);
                go.transform.localRotation = Quaternion.Euler(0, 0, 20);
                var a = go.AddComponent<Rigidbody2D>(); a.mass = 2; a.linearDamping = 0; a.angularDamping = 0;
                go.AddComponent<BoxCollider2D>().size = new Vector2(1, .5f); a.angularVelocity = 30;
                other.transform.SetParent(root.transform, false); other.transform.position = new Vector3(-1, 1, 0);
                other.transform.rotation = Quaternion.Euler(0, 0, -15);
                var b = other.AddComponent<Rigidbody2D>(); b.mass = 3; b.linearDamping = 0; b.angularDamping = 0;
                other.AddComponent<BoxCollider2D>().size = new Vector2(1, .5f);
                var joint = (AnchoredJoint2D)go.AddComponent(fixedJoint ? typeof(FixedJoint2D) : typeof(HingeJoint2D));
                joint.connectedBody = b; joint.autoConfigureConnectedAnchor = true;
                joint.anchor = new Vector2(.25f, .1f); joint.connectedAnchor = Vector2.zero; joint.enableCollision = true;
                if (joint is FixedJoint2D weld) { weld.frequency = 2; weld.dampingRatio = .7f; }
                Physics2D.SyncTransforms(); Physics2D.Simulate(.05f);
                var localA = fixedJoint ? ((J2.WeldJoint)joint.m_Joint).GetLocalAnchorA() : ((J2.RevoluteJoint)joint.m_Joint).GetLocalAnchorA();
                var localB = fixedJoint ? ((J2.WeldJoint)joint.m_Joint).GetLocalAnchorB() : ((J2.RevoluteJoint)joint.m_Joint).GetLocalAnchorB();
                Assert.Equal(.25f, localA.X); Assert.Equal(.1f, localA.Y);
                Assert.Equal(3.11455822f, localB.X); Assert.Equal(-3.12075567f, localB.Y);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(other); Object.DestroyImmediate(root); }
        }
    }
}
