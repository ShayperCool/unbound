using System;
using System.Linq;
using Unbound.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Render API members the pipelines use (tests/projects/srpmembers and formatmapping
    /// compare them with the Editor).</summary>
    public class SrpMembersTests
    {
        const string Guarded = "Setrendertarget is not allowed in this context";

        [Fact]
        public void TheRenderTargetGuardStopsEveryOverloadButTheMipOne()
        {
            var rt = new RenderTexture(4, 4, 24, GraphicsFormat.R8G8B8A8_UNorm);
            RenderTargetIdentifier id = rt;
            var colors = new RenderTargetIdentifier[] { rt };
            var binding = new RenderTargetBinding(rt, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, rt, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            var guarded = new Action<CommandBuffer>[]
            {
                cb => cb.SetRenderTarget(id),
                cb => cb.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store),
                cb => cb.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store),
                cb => cb.SetRenderTarget(id, 0, CubemapFace.Unknown),
                cb => cb.SetRenderTarget(id, 0, CubemapFace.Unknown, 0),
                cb => cb.SetRenderTarget(id, id),
                cb => cb.SetRenderTarget(id, id, 0),
                cb => cb.SetRenderTarget(id, id, 0, CubemapFace.Unknown),
                cb => cb.SetRenderTarget(id, id, 0, CubemapFace.Unknown, 0),
                cb => cb.SetRenderTarget(id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, id, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store),
                cb => cb.SetRenderTarget(colors, id),
                cb => cb.SetRenderTarget(colors, id, 0, CubemapFace.Unknown, 0),
                cb => cb.SetRenderTarget(binding),
                cb => cb.SetRenderTarget(binding, 0, CubemapFace.Unknown, 0),
            };
            var cb = new CommandBuffer();
            Assert.False(CommandBuffer.ThrowOnSetRenderTarget);
            try
            {
                CommandBuffer.ThrowOnSetRenderTarget = true;
                foreach (var call in guarded)
                    Assert.Equal(Guarded, Assert.Throws<Exception>(() => call(cb)).Message);
                cb.SetRenderTarget(id, 0);
                cb.ClearRenderTarget(true, true, Color.black);
            }
            finally
            {
                CommandBuffer.ThrowOnSetRenderTarget = false;
            }
            cb.SetRenderTarget(id);
            cb.Release();
        }

        [Fact]
        public void GraphicsBuffersCheckTheirShapeAndOnlyVertexOrIndexBuffersAreLockableByDefault()
        {
            void Shape(GraphicsBuffer.Target target, int count, int stride, string message) =>
                Assert.Equal(message, Assert.Throws<ArgumentException>(() => new GraphicsBuffer(target, count, stride)).Message);
            Shape(GraphicsBuffer.Target.Structured, 0, 4, "Attempting to create a zero length graphics buffer\nParameter name: count");
            Shape(GraphicsBuffer.Target.Structured, 4, 0, "Attempting to create a graphics buffer with a negative or null stride\nParameter name: stride");
            Shape(GraphicsBuffer.Target.Index, 4, 3, "Attempting to create an index buffer with an invalid stride: 3\nParameter name: stride");
            Shape(GraphicsBuffer.Target.Raw, 4, 6, "Stride must be a multiple of 4 unless the buffer is only used as a vertex buffer and/or index buffer \nParameter name: stride");
            new GraphicsBuffer(GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.CopySource, 4, 6).Release();

            foreach (var (target, flags) in new[]
            {
                (GraphicsBuffer.Target.Vertex, GraphicsBuffer.UsageFlags.LockBufferForWrite),
                (GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Index, GraphicsBuffer.UsageFlags.LockBufferForWrite),
                (GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None),
                (GraphicsBuffer.Target.Index | GraphicsBuffer.Target.CopyDestination, GraphicsBuffer.UsageFlags.None),
            })
            {
                var b = new GraphicsBuffer(target, 4, 4);
                Assert.Equal(flags, b.usageFlags);
                b.Release();
            }
            Assert.Throws<ArgumentException>(() => new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination,
                GraphicsBuffer.UsageFlags.LockBufferForWrite, 4, 4));
        }

        [Fact]
        public void LocksCheckTheirRangeAndAFailedUnlockLeavesTheLockOpen()
        {
            var plain = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            Assert.Equal("GraphicsBuffer must be created with usage mode UsageFlage.LockBufferForWrite to use LockBufferForWrite",
                Assert.Throws<InvalidOperationException>(() => plain.LockBufferForWrite<int>(0, 1)).Message);
            plain.Release();

            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, 8, 4);
            Assert.Equal("Specified argument was out of the range of valid values.\nParameter name: Bad indices/count arguments (bufferStartIndex:0 count:5 elementSize:8, this.count:8, this.stride4)",
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.LockBufferForWrite<long>(0, 5)).Message);
            var unmatched = Assert.Throws<InvalidOperationException>(() => buffer.UnlockBufferAfterWrite<int>(1));
            Assert.Equal("GraphicsBuffer.UnlockBufferAfterWrite was called without matching GraphicsBuffer.LockBufferForWrite", unmatched.Message);
            Assert.NotNull(unmatched.InnerException);

            var array = buffer.LockBufferForWrite<int>(2, 3);
            Assert.Equal(3, array.Length);
            Assert.True(buffer.Storage.Locked);
            array[0] = 7;
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.UnlockBufferAfterWrite<int>(-1));
            // The array is released, the lock stays open until the next lock ends.
            Assert.Throws<ObjectDisposedException>(() => array[1] = 8);
            Assert.True(buffer.Storage.Locked);
            buffer.LockBufferForWrite<int>(8, 0);
            buffer.UnlockBufferAfterWrite<int>(0);
            Assert.False(buffer.Storage.Locked);
            buffer.LockBufferForWrite<int>(0, 1);
            buffer.Release();
            Assert.False(buffer.Storage?.Locked ?? false);
        }

        [Theory]
        [InlineData(GraphicsFormat.R8G8B8A8_UNorm, "R G B A")]
        [InlineData(GraphicsFormat.B8G8R8A8_SRGB, "B G R A")]
        [InlineData(GraphicsFormat.B8G8R8_UNorm, "B G R 1")]
        [InlineData(GraphicsFormat.R10G10B10_XRUNormPack32, "B G R 1")]
        [InlineData(GraphicsFormat.A2R10G10B10_UNormPack32, "B G R A")]
        [InlineData(GraphicsFormat.A2B10G10R10_UNormPack32, "R G B A")]
        [InlineData(GraphicsFormat.B5G6R5_UNormPack16, "R G B 1")]
        [InlineData(GraphicsFormat.B10G11R11_UFloatPack32, "R G B 1")]
        [InlineData(GraphicsFormat.RGBA_DXT1_UNorm, "R G B 1")]
        [InlineData(GraphicsFormat.RGB_A1_ETC2_UNorm, "R G B A")]
        [InlineData(GraphicsFormat.RG_BC5_UNorm, "R G 0 1")]
        [InlineData(GraphicsFormat.R16_SFloat, "R 0 0 1")]
        [InlineData(GraphicsFormat.D24_UNorm_S8_UInt, "R G B A")]
        [InlineData(GraphicsFormat.None, "0 0 0 1")]
        [InlineData(GraphicsFormat.YUV2, "0 0 0 1")]
        [InlineData((GraphicsFormat)9999, "0 0 0 1")]
        public void SwizzlesReadTheFormatsChannels(GraphicsFormat format, string expected)
        {
            static string Short(FormatSwizzle s) => s.ToString().Substring("FormatSwizzle".Length);
            Assert.Equal(expected, string.Join(" ", Short(GraphicsFormatUtility.GetSwizzleR(format)), Short(GraphicsFormatUtility.GetSwizzleG(format)),
                Short(GraphicsFormatUtility.GetSwizzleB(format)), Short(GraphicsFormatUtility.GetSwizzleA(format))));
        }

        [Fact]
        public void OnlyTheFourCrunchedFormatsAreCrunched()
        {
            var crunched = Enum.GetValues(typeof(TextureFormat)).Cast<TextureFormat>().Where(GraphicsFormatUtility.IsCrunchFormat).Distinct().OrderBy(f => (int)f);
            Assert.Equal(new[] { TextureFormat.DXT1Crunched, TextureFormat.DXT5Crunched, TextureFormat.ETC_RGB4Crunched, TextureFormat.ETC2_RGBA8Crunched }, crunched);
            Assert.False(GraphicsFormatUtility.IsCrunchFormat((TextureFormat)99));
        }

        [Fact]
        public void PropertyBlockElementsReadBackAsTheirBindings()
        {
            var depthOnly = new RenderTexture(new RenderTextureDescriptor(8, 8, GraphicsFormat.None, GraphicsFormat.D24_UNorm_S8_UInt));
            var block = new MaterialPropertyBlock();
            int id = Shader.PropertyToID("_Src");
            block.SetTexture(id, depthOnly, RenderTextureSubElement.Depth);
            Assert.Same(depthOnly, block.GetTexture(id));
            // A depth-only target has no color: the binding is nothing.
            block.SetTexture(id, depthOnly, RenderTextureSubElement.Color);
            Assert.Null(block.GetTexture(id));
            Assert.Equal(new TextureElement(depthOnly, RenderTextureSubElement.Color), block.m_Values[id]);
            block.SetTexture("_Src", (Texture)depthOnly);
            Assert.Same(depthOnly, block.GetTexture(id));
            Assert.Equal("Value cannot be null.\nParameter name: value",
                Assert.Throws<ArgumentNullException>(() => block.SetTexture(id, (RenderTexture)null, RenderTextureSubElement.Depth)).Message);
        }

        [Fact]
        public void KeywordsAndKeywordSets()
        {
            var made = new ShaderKeyword("SRPM_TEST_" + Guid.NewGuid().ToString("N"));
            Assert.True(made.IsValid());
            Assert.Equal(made.m_Index, new ShaderKeyword(made.name).m_Index);
            Assert.Null(default(ShaderKeyword).name);
            Assert.False(default(ShaderKeyword).IsValid());
            var set = new ShaderKeywordSet();
            set.Enable(made);
            Assert.False(set.IsEnabled(made));
            Assert.Empty(set.GetShaderKeywords());
        }

        [Fact]
        public void TiersAndTiledStorage()
        {
            var tier = Graphics.activeTier;
            try
            {
                Graphics.activeTier = (GraphicsTier)7;
                Assert.Equal(7, (int)Graphics.activeTier);
            }
            finally
            {
                Graphics.activeTier = tier;
            }
            Assert.Equal(OpenGLESVersion.None, Graphics.minOpenGLESVersion);
            var previous = GraphicsDeviceProfile.Current;
            try
            {
                GraphicsDeviceProfile.Select("OpenGLCore");
                Assert.Equal(int.MaxValue, SystemInfo.maxTiledPixelStorageSize);
                Assert.Equal(32, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.R16G16B16A16_SFloat, 4));
                Assert.Equal(0, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.None, 1));
                GraphicsDeviceProfile.Select("Metal");
                Assert.Equal(64, SystemInfo.maxTiledPixelStorageSize);
                GraphicsDeviceProfile.Select("Vulkan");
                Assert.Equal(0, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.R8G8B8A8_UNorm, 1));
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }

        [Fact]
        public void DrawMeshInstancedTakesCountsWithinTheMatrices()
        {
            Immediate.Queued.Clear();
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/SrpMembersInstanced\" { SubShader { Pass {} } }");
            var material = new Material(shader) { enableInstancing = true };
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var matrices = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
            try
            {
                Assert.Equal("Count must be in the range of 0 to 3.\nParameter name: count",
                    Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 5)).Message);
                Assert.Equal("Argument lightProbeProxyVolume must not be null if lightProbeUsage is set to UseProxyVolume.\nParameter name: lightProbeProxyVolume",
                    Assert.Throws<ArgumentException>(() => Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 3, null, ShadowCastingMode.On, true, 0, null,
                        LightProbeUsage.UseProxyVolume, null)).Message);
                Assert.Throws<NullReferenceException>(() => Graphics.DrawMeshInstanced(mesh, 0, material, (Matrix4x4[])null));
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 0);
                Assert.Empty(Immediate.Queued);
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 2, null, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off, null);
                Assert.Equal(2, Immediate.Queued.Count);
            }
            finally
            {
                Immediate.Queued.Clear();
            }
        }
    }
}
