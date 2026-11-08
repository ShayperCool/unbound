using System;
using Unbound.Runtime;
using Unity.Collections;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Which targets a render pass's subpasses bind (tests/projects/urp subpass_mrt): every
    /// color output at once, in the subpass's order, with the depth attachment.</summary>
    public sealed class RenderPassTargetTests : IDisposable
    {
        readonly RenderTexture m_A = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture m_B = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture m_C = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture m_Depth = new RenderTexture(8, 8, 24, RenderTextureFormat.Depth);

        public void Dispose()
        {
            RenderPasses.End();
            foreach (var t in new[] { m_A, m_B, m_C, m_Depth }) Object.DestroyImmediate(t);
        }

        NativeArray<AttachmentDescriptor> Attachments()
        {
            var attachments = new NativeArray<AttachmentDescriptor>(4, Allocator.Temp);
            var textures = new[] { m_A, m_B, m_C, m_Depth };
            for (int i = 0; i < 4; i++)
            {
                var a = new AttachmentDescriptor(i == 3 ? RenderTextureFormat.Depth : RenderTextureFormat.ARGB32);
                a.ConfigureTarget(new RenderTargetIdentifier(textures[i]), false, true);
                a.ConfigureClear(new Color(0.1f * i, 0.2f, 0.3f, 1f));
                attachments[i] = a;
            }
            return attachments;
        }

        [Fact]
        public void ASubpassBindsEveryColorOutputInItsOrder()
        {
            using var attachments = Attachments();
            RenderPasses.Begin(attachments, 3, 8, 8);
            RenderPasses.SubPass(new[] { 2, 0 });
            var state = GfxStream.Current;
            Assert.Equal(new[] { TextureHandles.Of(m_C), TextureHandles.Of(m_A) }, state.Colors);
            Assert.Equal(TextureHandles.Of(m_Depth), state.Depth);
            // One output: a single target (no color list).
            RenderPasses.SubPass(new[] { 1 });
            state = GfxStream.Current;
            Assert.Null(state.Colors);
            Assert.Equal(TextureHandles.Of(m_B), state.Color);
            Assert.Equal(TextureHandles.Of(m_Depth), state.Depth);
        }

        [Fact]
        public void ADepthOnlySubpassDrawsIntoTheDepthAttachment()
        {
            using var attachments = Attachments();
            RenderPasses.Begin(attachments, 3, 8, 8);
            RenderPasses.SubPass(ReadOnlySpan<int>.Empty);
            var state = GfxStream.Current;
            Assert.Null(state.Colors);
            Assert.Equal(TextureHandles.Of(m_Depth), state.Color);
        }

        /// <summary>Outputs outside the pass's attachments are left out (the others still bind).</summary>
        [Fact]
        public void OutputsOutsideTheAttachmentsAreLeftOut()
        {
            using var attachments = Attachments();
            RenderPasses.Begin(attachments, -1, 8, 8);
            RenderPasses.SubPass(new[] { 0, 7, 1 });
            Assert.Equal(new[] { TextureHandles.Of(m_A), TextureHandles.Of(m_B) }, GfxStream.Current.Colors);
        }
    }
}
