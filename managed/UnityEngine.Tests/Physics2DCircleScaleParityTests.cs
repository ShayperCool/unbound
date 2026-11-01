using Xunit;

namespace UnityEngine.Tests
{
    public class Physics2DCircleScaleParityTests
    {
        [Theory]
        [InlineData("static", .7500002f)]
        [InlineData("body", .7500002f)]
        [InlineData("child", .7500002f)]
        [InlineData("rotated-child", .74999994f)]
        public void CircleRadiusRetainsWorldScaleAcrossBodyFrames(string frame, float expected)
        {
            var go = new GameObject("circle");
            var parent = new GameObject("parent");
            try
            {
                go.transform.position = new Vector3(.3f, -.2f, 2f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                go.transform.localScale = new Vector3(1f, .8f, 1f);
                if (frame == "body") go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                if (frame == "child" || frame == "rotated-child")
                {
                    parent.transform.position = go.transform.position;
                    parent.transform.rotation = Quaternion.Euler(0f, 0f, frame == "child" ? 0f : 11f);
                    parent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                    go.transform.SetParent(parent.transform, true);
                }
                var circle = go.AddComponent<CircleCollider2D>();
                circle.radius = .75f;
                Physics2D.SyncTransforms();
                var group = new PhysicsShapeGroup2D();
                Assert.Equal(1, circle.GetShapes(group));
                Assert.Equal(expected, group.GetShape(0).radius);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
