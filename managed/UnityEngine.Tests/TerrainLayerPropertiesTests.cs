using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A terrain layer's metallic and smoothness as its splat material gets them
    /// (tests/projects/defaultreflection, terrainlod layers5*).</summary>
    [Collection("RenderSettings state")]
    public class TerrainLayerPropertiesTests
    {
        [Fact]
        public void TheDiffuseAlphaSetsTheSmoothnessWhenItIsTheSource()
        {
            var rgba = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var rgb = new Texture2D(4, 4, TextureFormat.RGB24, false);
            try
            {
                // A new layer takes its smoothness from the diffuse alpha: the slider no longer counts.
                var layer = new TerrainLayer { diffuseTexture = rgba, smoothness = 0.3f };
                Assert.Equal(TerrainLayerSmoothnessSource.DiffuseAlphaChannel, layer.smoothnessSource);
                Assert.Equal(1f, TerrainRenderer.LayerSmoothness(layer));
                // Without alpha in the format, or without a texture, the slider gives it.
                layer.diffuseTexture = rgb;
                Assert.Equal(0.3f, TerrainRenderer.LayerSmoothness(layer));
                layer.diffuseTexture = null;
                Assert.Equal(0.3f, TerrainRenderer.LayerSmoothness(layer));
                // A constant source keeps the slider whatever the texture.
                layer.diffuseTexture = rgba;
                layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;
                Assert.Equal(0.3f, TerrainRenderer.LayerSmoothness(layer));
            }
            finally
            {
                Object.DestroyImmediate(rgba);
                Object.DestroyImmediate(rgb);
            }
        }

        [Fact]
        public void TheMetallicSliderIsAGammaValue()
        {
            var colorSpace = QualitySettings.s_ColorSpace;
            try
            {
                var layer = new TerrainLayer { metallic = 0.5f };
                QualitySettings.s_ColorSpace = ColorSpace.Linear;
                Assert.Equal(Mathf.GammaToLinearSpace(0.5f), TerrainRenderer.LayerMetallic(layer));
                Assert.InRange(TerrainRenderer.LayerMetallic(layer), 0.2139f, 0.2141f);
                layer.metallic = 1f;
                Assert.Equal(1f, TerrainRenderer.LayerMetallic(layer));
                layer.metallic = 0.5f;
                QualitySettings.s_ColorSpace = ColorSpace.Gamma;
                Assert.Equal(0.5f, TerrainRenderer.LayerMetallic(layer));
            }
            finally
            {
                QualitySettings.s_ColorSpace = colorSpace;
            }
        }
    }
}
