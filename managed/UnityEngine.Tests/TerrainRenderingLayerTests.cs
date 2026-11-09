using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Terrain.renderingLayerMask is its renderer's: renderer lists filter the terrain and
    /// light layers light it by that mask (tests/projects/urp terrain_light_layers).</summary>
    public class TerrainRenderingLayerTests
    {
        [Fact]
        public void TheTerrainsRendererTakesItsRenderingLayerMask()
        {
            var data = new TerrainData { heightmapResolution = 33 };
            var go = new GameObject("terrain rendering layers");
            go.SetActive(false);
            try
            {
                var terrain = go.AddComponent<Terrain>();
                terrain.terrainData = data;
                // Set before the terrain is enabled, then while it is.
                terrain.renderingLayerMask = 4u;
                go.SetActive(true);
                var renderer = terrain.Renderer;
                Assert.NotNull(renderer);
                Assert.Equal(4u, renderer.renderingLayerMask);
                terrain.renderingLayerMask = 6u;
                Assert.Equal(6u, renderer.renderingLayerMask);
                Assert.Equal(6u, terrain.renderingLayerMask);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(data);
            }
        }
    }
}
