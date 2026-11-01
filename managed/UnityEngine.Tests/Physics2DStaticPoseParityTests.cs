using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    public class Physics2DStaticPoseParityTests
    {
        [Theory]
        [InlineData(false, RigidbodyType2D.Dynamic)]
        [InlineData(false, RigidbodyType2D.Kinematic)]
        [InlineData(true, RigidbodyType2D.Dynamic)]
        [InlineData(true, RigidbodyType2D.Kinematic)]
        public void StaticPoseRetainsTouchingUntilTheStepAndClearsSolidViews(bool sensor, RigidbodyType2D kind)
        {
            var objects = new List<GameObject>();
            GameObject At(float x)
            {
                var go = new GameObject("contact"); go.transform.position = new Vector3(x, 0, 0);
                objects.Add(go); return go;
            }
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
                var moved = At(1000.5f); var target = moved.AddComponent<BoxCollider2D>();
                target.size = new Vector2(2, 2); target.isTrigger = sensor;
                var go = At(1000f); var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = kind; body.useFullKinematicContacts = true; body.constraints = RigidbodyConstraints2D.FreezeAll;
                var collider = go.AddComponent<CircleCollider2D>(); collider.radius = .25f;
                var peerGo = At(1020f); var peer = peerGo.AddComponent<Rigidbody2D>(); peer.constraints = RigidbodyConstraints2D.FreezeAll;
                peerGo.AddComponent<CircleCollider2D>().radius = .25f;
                At(1020.5f).AddComponent<BoxCollider2D>().size = new Vector2(2, 2);
                Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); Physics2D.Simulate(.02f);
                body.Sleep(); peer.Sleep(); Assert.True(collider.IsTouching(target));
                moved.transform.position = new Vector3(1005.5f, 0, 0); Physics2D.SyncTransforms();
                Assert.False(body.IsSleeping()); Assert.False(peer.IsSleeping());
                Assert.True(collider.IsTouching(target));
                Assert.Equal(0, body.GetContacts(new ContactPoint2D[8]));
                Assert.Equal(sensor ? 1 : 0, body.GetContacts(new Collider2D[8]));
                Assert.Equal(sensor ? 1 : 0, target.GetContacts(new Collider2D[8]));
                Physics2D.Simulate(.02f); Assert.False(collider.IsTouching(target));
            }
            finally { foreach (var go in objects) Object.DestroyImmediate(go); }
        }
    }
}
