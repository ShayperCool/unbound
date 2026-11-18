using System;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A material's texture property whose texture was destroyed
    /// (tests/projects/defaulttextures destroyed.*): GetTexture reads null, and the property
    /// samples opaque black whatever its default.</summary>
    public sealed class DestroyedMaterialTextureTests : IDisposable
    {
        readonly Shader m_Shader = ShaderUtil.CreateShaderAsset(
            "Shader \"Tests/DestroyedTexture\" { Properties { _T (\"T\", 2D) = \"white\" {} } SubShader { Pass { } } }");
        readonly Material m_Material;

        public DestroyedMaterialTextureTests() => m_Material = new Material(m_Shader);

        public void Dispose()
        {
            Object.DestroyImmediate(m_Material);
            Object.DestroyImmediate(m_Shader);
        }

        /// <summary>A material already sent with a texture is sent again when the texture is
        /// destroyed (the common case: a draw before the destruction).</summary>
        [Fact]
        public void AMaterialSentWithATextureIsSentAgainWhenItIsDestroyed()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            m_Material.SetTexture("_T", texture);
            m_Material.Sync();
            Assert.False(m_Material.m_Dirty);
            var slot = m_Material.m_Textures[Shader.PropertyToID("_T")];
            Assert.False(slot.SentDestroyed);
            Object.DestroyImmediate(texture);
            m_Material.Sync();
            Assert.True(slot.SentDestroyed);
        }

        [Fact]
        public void ADestroyedTextureReadsNullAndSamplesOpaqueBlack()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            m_Material.SetTexture("_T", texture);
            Assert.Same(texture, m_Material.GetTexture("_T"));
            int epoch = Texture.s_DestroyedEpoch;
            Object.DestroyImmediate(texture);
            Assert.Equal(epoch + 1, Texture.s_DestroyedEpoch);
            Assert.True(ReferenceEquals(m_Material.GetTexture("_T"), null));
            Assert.Equal(TextureHandles.Of(TextureElements.DestroyedTexture), Material.SentTexture(texture, RenderTextureSubElement.Default));
            Assert.Equal(new Color32(0, 0, 0, 255), (Color32)TextureElements.DestroyedTexture.GetPixel(0, 0));
            // Controls: a live texture sends its own handle, no texture the default's (0).
            var live = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try { Assert.Equal(TextureHandles.Of(live), Material.SentTexture(live, RenderTextureSubElement.Default)); }
            finally { Object.DestroyImmediate(live); }
            Assert.Equal(0UL, Material.SentTexture(null, RenderTextureSubElement.Default));
        }
    }
}
