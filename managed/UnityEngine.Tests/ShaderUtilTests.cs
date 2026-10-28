using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class ShaderUtilTests
    {
        [Fact]
        public void ShadersCanBeMadeFromSource()
        {
            var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Tests/Made From Source""
{
    Properties { _Color (""Color"", Color) = (1,1,1,1) }
    SubShader { Tags { ""Queue"" = ""Transparent"" } Pass { } }
}");
            Assert.Equal("Tests/Made From Source", shader.name);
            Assert.Equal(3000, shader.renderQueue);
            Assert.Same(shader, Shader.Find("Tests/Made From Source"));
            Assert.NotNull(new Material(shader));
        }

        [Fact]
        public void ATexturesScaleAndOffsetAreItsSTVector()
        {
            // Measured on the 6000.0 Editor.
            var shader = ShaderUtil.CreateShaderAsset(@"Shader ""Tests/Textured ST""
{
    Properties { _MainTex (""Tex"", 2D) = ""white"" {} _Color (""Color"", Color) = (1,1,1,1) }
    SubShader { Pass { } }
}");
            var m = new Material(shader);
            Assert.Equal(new Vector4(1, 1, 0, 0), m.GetVector("_MainTex_ST"));
            Assert.True(m.HasProperty("_MainTex_ST"));
            Assert.True(m.HasVector("_MainTex_ST"));
            m.mainTextureScale = new Vector2(2, 3);
            m.mainTextureOffset = new Vector2(0.5f, 0.25f);
            Assert.Equal(new Vector4(2, 3, 0.5f, 0.25f), m.GetVector("_MainTex_ST"));
            m.SetVector("_MainTex_ST", new Vector4(4, 5, 6, 7));
            Assert.Equal(new Vector2(4, 5), m.mainTextureScale);
            Assert.Equal(new Vector2(6, 7), m.mainTextureOffset);
            Assert.Equal(Vector4.zero, m.GetVector("_Nope_ST"));
            Assert.False(m.HasProperty("_Color_ST"));
        }
    }
}
