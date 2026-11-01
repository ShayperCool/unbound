using Xunit;

namespace UnityEngine.Tests
{
    public class Physics2DManifoldParityTests
    {
        [Theory]
        [InlineData(false, 10f, .135541752f, -.07764301f)]
        [InlineData(true, 30f, -.0616025627f, -.0933012962f)]
        public void ParallelSegmentCastsClipToCoreBounds(bool horizontal, float angle, float x, float y)
        {
            var target = new GameObject("target"); target.layer = 8;
            try
            {
                Physics2D.queriesStartInColliders = true;
                target.transform.position = new Vector3(0, 0, 2);
                target.transform.rotation = Quaternion.Euler(0, 0, angle);
                target.transform.localScale = new Vector3(1, .8f, 1);
                var capsule = target.AddComponent<CapsuleCollider2D>();
                capsule.direction = horizontal ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                capsule.size = horizontal ? new Vector2(3, 1) : new Vector2(1, 3);
                Physics2D.SyncTransforms();
                var axis = Quaternion.Euler(0, 0, angle) * (horizontal ? Vector3.right : Vector3.up);
                var origin = -.1f * new Vector2(axis.x, axis.y);
                var hit = horizontal
                    ? Physics2D.CapsuleCast(origin, new Vector2(1.6f, .6f), CapsuleDirection2D.Horizontal, angle, Vector2.right, 1, 1 << 8)
                    : Physics2D.BoxCast(origin, new Vector2(.5f, .8f), angle, Vector2.right, 1, 1 << 8);
                Assert.Same(capsule, hit.collider);
                Assert.Equal(x, hit.point.x, 6); Assert.Equal(y, hit.point.y, 6);
            }
            finally { Object.DestroyImmediate(target); }
        }
    }
}
