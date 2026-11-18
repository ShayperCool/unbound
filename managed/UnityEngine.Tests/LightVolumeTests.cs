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
