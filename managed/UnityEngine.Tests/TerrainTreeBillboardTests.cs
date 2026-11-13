using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The numbers legacy trees and their billboards are drawn with
    /// (tests/projects/treebillboards).</summary>
    public class TerrainTreeBillboardTests
    {
        [Fact]
        public void TheAtlasTakesTheNearestPowerOfTwo()
        {
            Assert.Equal(1, TerrainRenderer.NearestPowerOfTwo(0));
            Assert.Equal(128, TerrainRenderer.NearestPowerOfTwo(167));
            // A tie goes up.
            Assert.Equal(256, TerrainRenderer.NearestPowerOfTwo(192));
            Assert.Equal(256, TerrainRenderer.NearestPowerOfTwo(257));
            Assert.Equal(4096, TerrainRenderer.NearestPowerOfTwo(4000));
        }

        [Fact]
        public void BillboardsLeanAndComeForwardWithThePitch()
        {
            TerrainRenderer.BillboardFactors(0f, out float angle, out float offset);
            Assert.Equal(0f, angle);
            Assert.Equal(0.1f, offset);
            TerrainRenderer.BillboardFactors(90f, out angle, out offset);
            Assert.Equal(1f, angle, 5);
            Assert.Equal(1f, offset);
            // Halfway from 40 to 90 degrees the smoothstep is a half.
            TerrainRenderer.BillboardFactors(65f, out _, out offset);
            Assert.Equal(0.5f, offset, 5);
            // A camera looking up (350 degrees is -10) leans them as much as one looking down.
            TerrainRenderer.BillboardFactors(350f, out angle, out offset);
            Assert.Equal(1f - Mathf.Cos(10f * Mathf.Deg2Rad), angle, 5);
            Assert.Equal(0.1f, offset);
        }

        [Fact]
        public void MeshTreesSquashAcrossTheFade()
        {
            Assert.Equal(1f, TerrainRenderer.SquashAt(10f, 25f, 40f));
            Assert.Equal(1f, TerrainRenderer.SquashAt(25f, 25f, 40f));
            Assert.Equal(0.04f, TerrainRenderer.SquashAt(65f, 25f, 40f), 6);
            Assert.Equal(0.04f + 0.96f * 0.5f, TerrainRenderer.SquashAt(45f, 25f, 40f), 5);
            // No fade: no squash.
            Assert.Equal(1f, TerrainRenderer.SquashAt(30f, 25f, 0f));
        }

        [Fact]
        public void TheLightmapColorTintsByEighthBitSteps()
        {
            var white = new Color32(255, 255, 255, 255);
            Assert.Equal(new Color32(200, 100, 50, 255), TerrainRenderer.Tinted(new Color32(200, 100, 50, 255), white));
            Assert.Equal(new Color32(100, 50, 25, 126), TerrainRenderer.Tinted(new Color32(200, 100, 50, 255), new Color32(127, 127, 127, 126)));
        }
    }
}
