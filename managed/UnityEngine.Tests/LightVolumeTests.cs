using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The share of the view's height a deferred point light's box covers, which picks its
    /// volume mesh (tests/projects/builtinfx deferred_volume_point_small and _large): the box's
    /// faces clipped by the near plane, projected and clamped to the view.</summary>
    public class LightVolumeTests
    {
        // A camera at the origin looking down -z (camera space) with a 60 degree view, 1:1.
        static readonly Matrix4x4 s_ViewProjection = Matrix4x4.Perspective(60f, 1f, 0.3f, 100f);

        [Fact]
        public void ABoxAheadCoversItsNearFacesHeight()
        {
            // The nearest face is 9 away; its half height 1 projects to 1 / (9 tan 30).
            float height = FrameBuilder.PointBoxHeight(new Vector3(0f, 0f, -10f), 1f, s_ViewProjection);
            Assert.Equal(1f / (9f * Mathf.Tan(30f * Mathf.Deg2Rad)), height, 4);
        }

        [Fact]
        public void ABoxAroundTheCameraIsClippedToTheWholeView()
        {
            Assert.Equal(1f, FrameBuilder.PointBoxHeight(new Vector3(0f, 0f, -0.5f), 2f, s_ViewProjection), 5);
        }

        [Fact]
        public void ABoxBehindTheCameraOrBesideTheViewCoversNothing()
        {
            Assert.Equal(0f, FrameBuilder.PointBoxHeight(new Vector3(0f, 0f, 10f), 1f, s_ViewProjection));
            Assert.Equal(0f, FrameBuilder.PointBoxHeight(new Vector3(0f, 50f, -10f), 1f, s_ViewProjection));
        }

        [Fact]
        public void ABoxAboveTheViewsMiddleSpansItsLowestAndHighestProjectedCorners()
        {
            // Centered 4 above the axis at depth 10: from the far face's lower edge (y 3 at depth 11)
            // to the near face's upper edge (y 5 at depth 9).
            float t = Mathf.Tan(30f * Mathf.Deg2Rad);
            float height = FrameBuilder.PointBoxHeight(new Vector3(0f, 4f, -10f), 1f, s_ViewProjection);
            Assert.Equal((5f / (9f * t) - 3f / (11f * t)) * 0.5f, height, 4);
        }

        [Fact]
        public void ABoxReachingAboveTheViewIsClampedToItsTop()
        {
            float t = Mathf.Tan(30f * Mathf.Deg2Rad);
            float height = FrameBuilder.PointBoxHeight(new Vector3(0f, 6f, -10f), 1f, s_ViewProjection);
            Assert.Equal((1f - 5f / (11f * t)) * 0.5f, height, 4);
        }
    }
}

namespace UnityEngine.Tests
{
    /// <summary>Which off-screen deferred cameras draw their G-buffer with the target's own depth
    /// and stencil (tests/projects/builtinfx deferredDontClear.*: an LDR camera into a 24-bit
    /// target); the others keep a depth of their own, which has the stencil the light passes test.</summary>
    public class DeferredTargetDepthTests
    {
        static RenderTexture Target(int depth, int samples = 1)
        {
            var rt = new RenderTexture(64, 32, depth) { antiAliasing = samples };
            rt.Create();
            return rt;
        }

        [Fact]
        public void AnLdrCameraDrawsWithASingleSampleStencilTargetDepthOfItsSize()
        {
            var rt = Target(24);
            try
            {
                Assert.True(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(false, rt, 64, 32));
                // Controls: an HDR camera, another size (a scaled intermediate).
                Assert.False(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(true, rt, 64, 32));
                Assert.False(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(false, rt, 32, 16));
            }
            finally { Object.DestroyImmediate(rt); }
        }

        [Fact]
        public void ATargetDepthWithoutStencilWithSamplesOrNoneKeepsTheCamerasOwn()
        {
            var shallow = Target(16);
            var multisampled = Target(24, 4);
            var none = Target(0);
            try
            {
                Assert.False(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(false, shallow, 64, 32));
                Assert.False(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(false, multisampled, 64, 32));
                Assert.False(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(false, none, 64, 32));
                Assert.False(Unbound.Runtime.FrameBuilder.DrawsWithTargetDepth(false, null, 64, 32));
            }
            finally
            {
                Object.DestroyImmediate(shallow);
                Object.DestroyImmediate(multisampled);
                Object.DestroyImmediate(none);
            }
        }
    }
}
