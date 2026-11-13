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
        [Fact]
        public void TiltedBodyFrameRetainsRadianRotationAndLocalCenter()
        {
            var parent = new GameObject("parent");
            var go = new GameObject("circle");
            try
            {
                parent.transform.position = new Vector3(.7f, -.4f, 1.2f);
                parent.transform.localRotation = Quaternion.Euler(17f, -29f, 13f);
                parent.transform.localScale = new Vector3(-.9f, 1.7f, 1.1f);
                var body = parent.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
                go.transform.SetParent(parent.transform, false);
                go.transform.localPosition = new Vector3(.23f, -.17f, 2f);
                go.transform.localScale = new Vector3(1f, .8f, 1f);
                var circle = go.AddComponent<CircleCollider2D>(); circle.radius = .75f;
                circle.offset = new Vector2(.17f, -.23f);
                Physics2D.SyncTransforms();
                var group = new PhysicsShapeGroup2D(); circle.GetShapes(group);
                // The Linux x86-64 and macOS arm64 Editors (6000.0.71f1) differ here by an ulp;
                // the platform's sine and cosine give each one's values.
                bool mac = System.OperatingSystem.IsMacOS();
                Assert.Equal(mac ? .954100132f : .9541002f, body.localToWorldMatrix.m00);
                Assert.Equal(mac ? .29948771f : .29948768f, group.localToWorldMatrix.m10);
                var points = new System.Collections.Generic.List<Vector2>(); group.GetShapeVertices(0, points);
                Assert.Equal(-1.44641972f, points[0].x);
                Assert.Equal(mac ? -.889037609f : -.8890377f, points[0].y);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
