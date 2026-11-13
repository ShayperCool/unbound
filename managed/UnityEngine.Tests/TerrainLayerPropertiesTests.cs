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
            var dxt1 = new Texture2D(4, 4, TextureFormat.DXT1, false);
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
                // DXT1 has no alpha for this, though its graphics format is an RGBA one.
                layer.diffuseTexture = dxt1;
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
                Object.DestroyImmediate(dxt1);
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

        [Fact]
        public void AGammaFlaggedMetallicIsMadeLinearOnce()
        {
            var colorSpace = QualitySettings.s_ColorSpace;
            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Tests/TerrainGammaMetallic\" { Properties { [Gamma] _Metallic0 (\"Metallic\", Float) = 0 } SubShader { Pass {} } }");
            var material = new Material(shader);
            var data = new TerrainData();
            try
            {
                QualitySettings.s_ColorSpace = ColorSpace.Linear;
                int id = Shader.PropertyToID("_Metallic0");
                // The shader asks for the conversion of a value a script sets (URP's terrain
                // shaders flag their layers' metallic so).
                material.SetFloat(id, 0.5f);
                Assert.True(material.ConvertsProperty(id));
                // The terrain gives the layer's value made linear already: it goes as it is.
                TerrainRenderer.SetLayers(material, data, new[] { new TerrainLayer { metallic = 0.5f } }, 0, null);
                Assert.Equal(Mathf.GammaToLinearSpace(0.5f), material.GetFloat(id));
                Assert.False(material.ConvertsProperty(id));
            }
            finally
            {
                QualitySettings.s_ColorSpace = colorSpace;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
