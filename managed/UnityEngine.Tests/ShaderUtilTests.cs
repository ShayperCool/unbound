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
    }
}
