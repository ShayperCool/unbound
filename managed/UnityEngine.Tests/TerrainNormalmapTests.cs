using UnityEngine.Experimental.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Terrain.normalmapTexture as the Unity 6000.0.71f1 Editor makes it
    /// (tests/projects/terrainnormalmap): an instanced terrain's normal map of its heights.</summary>
    public class TerrainNormalmapTests
    {
        static (GameObject, Terrain, TerrainData) Make(float[,] heights)
        {
            var data = new TerrainData { heightmapResolution = 33 };
            data.size = new Vector3(64, 16, 64);
            if (heights != null) data.SetHeights(0, 0, heights);
            var go = new GameObject("terrain normal map");
            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            return (go, terrain, data);
        }

        [Fact]
        public void AnInstancedTerrainHasItFromTheStartAndLosesItWithInstancing()
        {
            var (go, terrain, data) = Make(null);
            try
            {
                Assert.Null(terrain.normalmapTexture);
                terrain.drawInstanced = true;
                var map = terrain.normalmapTexture;
                Assert.NotNull(map);
                Assert.Same(map, terrain.normalmapTexture);
                Assert.Equal("TerrainNormalMap", map.name);
                Assert.Equal((33, 33), (map.width, map.height));
                Assert.Equal(GraphicsFormat.A2B10G10R10_UNormPack32, map.graphicsFormat);
                Assert.Equal(RenderTextureFormat.ARGB2101010, map.format);
                Assert.True(map.useMipMap);
                Assert.False(map.autoGenerateMips);
                Assert.Equal(6, map.mipmapCount);
                Assert.Equal(FilterMode.Bilinear, map.filterMode);
                Assert.Equal(TextureWrapMode.Clamp, map.wrapMode);
                Assert.False(map.sRGB);
                Assert.Equal((HideFlags)125, map.hideFlags);
                Assert.True(map.IsCreated());
                terrain.drawInstanced = false;
                Assert.True(map == null);
                Assert.Null(terrain.normalmapTexture);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(data);
            }
        }

        [Fact]
        public void TheFormatsAreTenBitsWithoutATextureFormat()
        {
            Assert.Equal(GraphicsFormat.A2B10G10R10_UNormPack32, Terrain.normalmapFormat);
            Assert.Equal(RenderTextureFormat.ARGB2101010, Terrain.normalmapRenderTextureFormat);
            Assert.Equal((TextureFormat)0, Terrain.normalmapTextureFormat);
        }

        /// <summary>A sawtooth along x (1/16 of the 16-unit height per 2-unit cell: a slope of
        /// 1/2) with one flat row: at the clamped left edge the slope halves; beside the flat row
        /// the Sobel weights (1, 2, 1 across) give 3/8 along x and a slope across. Central
        /// differences or cells of size / resolution would give other bytes.</summary>
        [Fact]
        public void TexelsAreTheSobelNormalsInEightBits()
        {
            var heights = new float[33, 33];
            for (int y = 0; y < 33; y++)
                for (int x = 0; x < 33; x++)
                    heights[y, x] = y == 20 ? 0 : (x % 16) / 16f;
            var (go, _, data) = Make(heights);
            try
            {
                var texels = Terrain.NormalmapTexels(data, 33);
                // normalize(-0.25, 1, 0).
                Assert.Equal(new Color32(97, 251, 128, 255), texels[16 * 33]);
                // normalize(-0.375, 1, 2): the flat row above makes both slopes.
                Assert.Equal(new Color32(106, 184, 240, 255), texels[19 * 33 + 8]);
                var flat = Terrain.NormalmapTexels(new TerrainData { heightmapResolution = 33 }, 33);
                Assert.All(flat, t => Assert.Equal(new Color32(128, 255, 128, 255), t));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(data);
            }
        }

        /// <summary>Height changes reach the normal map when the terrain is next rendered; making
        /// and refreshing it leave the script's active render texture active.</summary>
        [Fact]
        public void HeightChangesWaitForTheNextRender()
        {
            var (go, terrain, data) = Make(null);
            var target = new RenderTexture(4, 4, 0);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                terrain.drawInstanced = true;
                var map = terrain.normalmapTexture;
                Assert.Same(target, RenderTexture.active);
                var before = (Color32[])terrain.NormalmapTexelsIfMade.Clone();
                int made = data.m_HeightsVersion;
                data.SetHeights(10, 12, new float[,] { { 0.25f } });
                Assert.Same(map, terrain.normalmapTexture);
                Assert.Equal(made, terrain.m_NormalmapVersion);
                terrain.UpdateNormalmap();
                Assert.Same(map, terrain.normalmapTexture);
                Assert.Equal(data.m_HeightsVersion, terrain.m_NormalmapVersion);
                Assert.Same(target, RenderTexture.active);
                // Beside the raised sample (its own Sobel gradient is zero).
                Assert.NotEqual(before[12 * 33 + 11], terrain.NormalmapTexelsIfMade[12 * 33 + 11]);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(data);
            }
        }

        /// <summary>Unbound makes the map when a script first asks for it: an instanced terrain
        /// nobody asks about has none to keep up to date.</summary>
        [Fact]
        public void TheMapIsMadeWhenAScriptAsksForIt()
        {
            var (go, terrain, data) = Make(null);
            try
            {
                terrain.drawInstanced = true;
                terrain.UpdateNormalmap();
                Assert.Null(terrain.NormalmapIfMade);
                Assert.NotNull(terrain.normalmapTexture);
                Assert.NotNull(terrain.NormalmapIfMade);
                Object.DestroyImmediate(go);
                Assert.Null(terrain.normalmapTexture);
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(data);
            }
        }
    }
}
