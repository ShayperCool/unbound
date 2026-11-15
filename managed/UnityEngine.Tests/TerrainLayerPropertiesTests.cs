using UnityEditor;
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
        public void EachPassFlagsTheLayersWithAMaskMap()
        {
            var shader = Shader.Create("Universal Render Pipeline/Terrain/Lit", 2000);
            var rgba = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var mask = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            var data = new TerrainData();
            var first = new Material(shader);
            var second = new Material(shader);
            try
            {
                // Five layers in two passes: masks on the first and third, none in the second pass.
                var layers = new TerrainLayer[5];
                for (int i = 0; i < layers.Length; i++)
                    layers[i] = new TerrainLayer { diffuseTexture = rgba, maskMapTexture = i == 0 || i == 2 ? mask : null };
                layers[1].smoothnessSource = TerrainLayerSmoothnessSource.Constant;
                layers[3].diffuseRemapMin = new Vector4(0.1f, 0.2f, 0.3f, 1f);
                TerrainRenderer.SetLayers(first, data, layers, 0, null);
                TerrainRenderer.SetLayers(second, data, layers, 4, null);

                Assert.True(first.IsKeywordEnabled("_MASKMAP"));
                // Layers without a map get the flat normal and black textures.
                Assert.Same(mask, first.GetTexture("_Mask0"));
                Assert.Same(Texture2D.blackTexture, first.GetTexture("_Mask1"));
                Assert.Same(Texture2D.normalTexture, first.GetTexture("_Normal0"));
                Assert.Equal(new[] { 1f, 0f, 1f, 0f }, new[] { first.GetFloat("_LayerHasMask0"), first.GetFloat("_LayerHasMask1"), first.GetFloat("_LayerHasMask2"), first.GetFloat("_LayerHasMask3") });
                // The smoothness from the diffuse alpha, unless the layer's source is a constant.
                Assert.Equal(new[] { 1f, 0f, 1f, 1f }, new[] { first.GetFloat("_DiffuseHasAlpha0"), first.GetFloat("_DiffuseHasAlpha1"), first.GetFloat("_DiffuseHasAlpha2"), first.GetFloat("_DiffuseHasAlpha3") });
                Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 1f), first.GetVector("_DiffuseRemapOffset3"));
                Assert.Equal(new Vector4(0.9f, 0.8f, 0.7f, 0f), first.GetVector("_DiffuseRemapScale3"));
                // The second pass has no mask; its unused slots count as layers without one.
                Assert.False(second.IsKeywordEnabled("_MASKMAP"));
                Assert.Equal(new[] { 0f, 0f, 0f, 0f }, new[] { second.GetFloat("_LayerHasMask0"), second.GetFloat("_LayerHasMask1"), second.GetFloat("_LayerHasMask2"), second.GetFloat("_LayerHasMask3") });
                // Both passes know the terrain's layer count.
                Assert.Equal(5f, first.GetFloat("_NumLayersCount"));
                Assert.Equal(5f, second.GetFloat("_NumLayersCount"));

                // A pass whose masks are all gone turns the keyword off again.
                layers[0].maskMapTexture = null;
                layers[2].maskMapTexture = null;
                TerrainRenderer.SetLayers(first, data, layers, 0, null);
                Assert.False(first.IsKeywordEnabled("_MASKMAP"));
                Assert.Equal(0f, first.GetFloat("_LayerHasMask0"));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(rgba);
                Object.DestroyImmediate(mask);
            }
        }

        [Fact]
        public void TheFlatNormalAndLinearGreyHoldLinearData()
        {
            // As the Editor reports them (tests/projects/defaulttextures).
            Assert.Equal(UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, Texture2D.normalTexture.graphicsFormat);
            Assert.Equal(UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, Texture2D.linearGrayTexture.graphicsFormat);
            Assert.NotSame(Texture2D.grayTexture, Texture2D.linearGrayTexture);
            Assert.Equal(new Color32(127, 127, 255, 255), Texture2D.normalTexture.GetPixels32()[0]);
            Assert.Equal(new Color32(127, 127, 127, 127), Texture2D.linearGrayTexture.GetPixels32()[0]);
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
        public void TheFormatsWithAlphaAreTheEditors()
        {
            var alpha = new[]
            {
                TextureFormat.Alpha8, TextureFormat.ARGB4444, TextureFormat.RGBA32, TextureFormat.ARGB32, TextureFormat.DXT5,
                TextureFormat.RGBA4444, TextureFormat.BGRA32, TextureFormat.RGBAHalf, TextureFormat.RGBAFloat, TextureFormat.BC7,
                TextureFormat.DXT5Crunched, TextureFormat.PVRTC_RGBA2, TextureFormat.PVRTC_RGBA4, TextureFormat.ETC2_RGBA1,
                TextureFormat.ETC2_RGBA8, TextureFormat.ETC2_RGBA8Crunched, TextureFormat.RGBA64, TextureFormat.RGBA32_SIGNED,
                TextureFormat.RGBA64_SIGNED,
            };
            foreach (TextureFormat format in System.Enum.GetValues(typeof(TextureFormat)))
            {
                bool astc = format is >= TextureFormat.ASTC_4x4 and <= TextureFormat.ASTC_12x12
                    or >= TextureFormat.ASTC_HDR_4x4 and <= TextureFormat.ASTC_HDR_12x12;
                Assert.True(System.Array.IndexOf(alpha, format) >= 0 || astc
                    ? Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(format)
                    : !Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(format), format.ToString());
            }
        }

        [Fact]
        public void AGammaFlaggedMetallicIsMadeLinearAgain()
        {
            var colorSpace = QualitySettings.s_ColorSpace;
            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Tests/TerrainGammaMetallic\" { Properties { [Gamma] _Metallic0 (\"Metallic\", Float) = 0 } SubShader { Pass {} } }");
            var plain = ShaderUtil.CreateShaderAsset(
                "Shader \"Tests/TerrainPlainMetallic\" { Properties { _Metallic0 (\"Metallic\", Float) = 0 } SubShader { Pass {} } }");
            var material = new Material(shader);
            var other = new Material(plain);
            var data = new TerrainData();
            try
            {
                QualitySettings.s_ColorSpace = ColorSpace.Linear;
                int id = Shader.PropertyToID("_Metallic0");
                var layers = new[] { new TerrainLayer { metallic = 0.5f } };
                // The terrain gives the layer's value made linear; a shader that flags it [Gamma]
                // (URP's terrain shaders) has the material make it linear once more, one that does
                // not takes it as it is (tests/projects/urpterrain, defaultreflection).
                TerrainRenderer.SetLayers(material, data, layers, 0, null);
                Assert.Equal(Mathf.GammaToLinearSpace(0.5f), material.GetFloat(id), 6);
                Assert.True(material.ConvertsProperty(id));
                TerrainRenderer.SetLayers(other, data, layers, 0, null);
                Assert.Equal(Mathf.GammaToLinearSpace(0.5f), other.GetFloat(id), 6);
                Assert.False(other.ConvertsProperty(id));
            }
            finally
            {
                QualitySettings.s_ColorSpace = colorSpace;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(shader);
                Object.DestroyImmediate(plain);
            }
        }
    }
}
