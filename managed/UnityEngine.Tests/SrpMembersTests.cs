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

        static RenderTargetBinding Binding(RenderTargetIdentifier[] colors, RenderTargetIdentifier depth, int loads = -1, int stores = -1,
            RenderBufferLoadAction load = RenderBufferLoadAction.Load) =>
            new RenderTargetBinding(colors, Enumerable.Repeat(load, loads < 0 ? colors.Length : loads).ToArray(),
                Enumerable.Repeat(RenderBufferStoreAction.Store, stores < 0 ? colors.Length : stores).ToArray(),
                depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

        /// <summary>A binding is checked when recorded, with the release's messages, and takes 152
        /// bytes whatever its count and level, as a color with a depth target does (tests/projects/
        /// cmdbuffers bind.*); one color with actions, a face or a slice takes 36.</summary>
        [Fact]
        public void ARenderTargetBindingIsCheckedWhenRecordedAndTakesItsMeasuredSize()
        {
            var rts = Enumerable.Range(0, 3).Select(_ => new RenderTexture(4, 4, 0, GraphicsFormat.R8G8B8A8_UNorm)).ToArray();
            var depth = new RenderTexture(4, 4, 24, RenderTextureFormat.Depth);
            try
            {
                var ids = rts.Select(r => new RenderTargetIdentifier(r)).ToArray();
                using var cb = new CommandBuffer();
                ArgumentException Refused(RenderTargetBinding b) => Assert.Throws<ArgumentException>(() => cb.SetRenderTarget(b));
                Assert.Equal("The number of color render targets must be at least 1, but was 0",
                    Refused(Binding(new RenderTargetIdentifier[0], depth)).Message);
                Assert.Equal("The number of color render targets (9) and exceeds the maximum supported number of render targets (8)",
                    Refused(Binding(Enumerable.Repeat(ids[0], 9).ToArray(), depth)).Message);
                Assert.Equal("The number of color load actions provided (1) does not match the number of color render targets (2)",
                    Refused(Binding(ids.Take(2).ToArray(), depth, loads: 1)).Message);
                Assert.Equal("The number of color store actions provided (2) does not match the number of color render targets (2)",
                    Refused(Binding(ids.Take(2).ToArray(), depth, stores: 1)).Message);
                Assert.Equal("RenderBufferLoadAction.Clear is not supported",
                    Refused(Binding(ids.Take(2).ToArray(), depth, load: RenderBufferLoadAction.Clear)).Message);
                var nothing = Assert.Throws<NullReferenceException>(() => cb.SetRenderTarget(default(RenderTargetBinding)));
                Assert.Equal("Object reference not set to an instance of an object", nothing.Message);
                Assert.Equal(0, cb.sizeInBytes);

                cb.SetRenderTarget(Binding(ids.Take(1).ToArray(), depth));
                Assert.Equal(152, cb.sizeInBytes);
                cb.SetRenderTarget(Binding(ids, depth), 1, CubemapFace.PositiveX, 2);
                Assert.Equal(304, cb.sizeInBytes);
                cb.SetRenderTarget(ids[0], depth);
                Assert.Equal(456, cb.sizeInBytes);
                cb.SetRenderTarget(ids[0], RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, depth, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
                Assert.Equal(608, cb.sizeInBytes);
                cb.SetRenderTarget(ids[0], 1, CubemapFace.Unknown, 2);
                Assert.Equal(644, cb.sizeInBytes);
            }
            finally
            {
                foreach (var r in rts) UnityEngine.Object.DestroyImmediate(r);
                UnityEngine.Object.DestroyImmediate(depth);
            }
        }

        /// <summary>A binding draws into every color at the call's level (slice -1 is 0); a None
        /// depth or color target given with the other binds nothing, through the binding, array,
        /// color-and-depth and actions overloads (tests/projects/cmdbuffers bind.colors.*,
        /// bind.slice.all, bind.noneDepth.*, bind.noneColor).</summary>
        [Fact]
        public void ABindingBindsEveryColorAndANoneTargetBindsNothing()
        {
            var rts = Enumerable.Range(0, 3).Select(_ => new RenderTexture(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)
                { dimension = TextureDimension.Tex2DArray, volumeDepth = 3, useMipMap = true, autoGenerateMips = false })).ToArray();
            try
            {
                foreach (var r in rts) r.Create();
                var ids = rts.Select(r => new RenderTargetIdentifier(r, 1, CubemapFace.Unknown, 2)).ToArray();
                var none = new RenderTargetIdentifier(BuiltinRenderTextureType.None);
                void Run(Action<CommandBuffer> record)
                {
                    using var cb = new CommandBuffer();
                    record(cb);
                    Graphics.ExecuteCommandBuffer(cb);
                }
                Run(cb => cb.SetRenderTarget(Binding(ids, ids[0])));
                var state = GfxStream.Current;
                Assert.Equal(rts.Select(r => TextureHandles.Of(r)).ToArray(), state.Colors);
                // The identifiers' own mip and slice give way to the call's.
                Assert.Equal((0, 0), (state.Mip, state.Slice));
                Run(cb => cb.SetRenderTarget(Binding(ids, ids[0]), 1, CubemapFace.Unknown, -1));
                Assert.Equal((1, 0), (GfxStream.Current.Mip, GfxStream.Current.Slice));
                // Control: the array overload keeps slice -1 in the target state (a clear clears
                // every slice; draws go into the first).
                Run(cb => cb.SetRenderTarget(ids, ids[0], 1, CubemapFace.Unknown, -1));
                Assert.Equal(-1, GfxStream.Current.Slice);

                foreach (var record in new Action<CommandBuffer>[]
                {
                    cb => cb.SetRenderTarget(Binding(ids, none)),
                    cb => cb.SetRenderTarget(ids, none),
                    cb => cb.SetRenderTarget(ids[0], none),
                    cb => cb.SetRenderTarget(none, ids[0]),
                    cb => cb.SetRenderTarget(ids[0], RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, none, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store),
                })
                {
                    Run(cb => cb.SetRenderTarget(ids[1]));
                    Run(record);
                    Assert.Equal(GfxStream.NoTexture, GfxStream.Current.Color);
                    Assert.Null(RenderTexture.active);
                }
            }
            finally
            {
                foreach (var r in rts) UnityEngine.Object.DestroyImmediate(r);
                RenderTexture.active = null;
            }
        }

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
                Assert.Equal(message, Unbound.Runtime.MonoExceptions.MessageOf(Assert.Throws<ArgumentException>(() => new GraphicsBuffer(target, count, stride))));
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
                Unbound.Runtime.MonoExceptions.MessageOf(Assert.Throws<ArgumentOutOfRangeException>(() => buffer.LockBufferForWrite<long>(0, 5))));
            // A count whose bytes wrap 32 bits (0x20000000 * 8 = 2^32) is refused too.
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.LockBufferForWrite<long>(0, 0x20000000));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.LockBufferForWrite<int>(int.MaxValue, 1));
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

        /// <summary>Red: the array of an earlier lock still wrote into memory the next lock or a
        /// Release freed.</summary>
        [Fact]
        public void AnArrayDiesWithItsLockAndUnwrittenElementsKeepTheirContents()
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, 4, 4);
            // The first lock makes the CPU copy (from the GPU's contents, which tests have none
            // of); every later write keeps it current.
            buffer.LockBufferForWrite<int>(0, 0);
            buffer.UnlockBufferAfterWrite<int>(0);
            buffer.SetData(new[] { 10, 11, 12, 13 });
            var first = buffer.LockBufferForWrite<int>(0, 4);
            first[0] = 1;
            var second = buffer.LockBufferForWrite<int>(0, 2);
            Assert.Throws<ObjectDisposedException>(() => first[1] = 2);
            // The lock reads the buffer's contents: an element it does not write keeps its value.
            // (The earlier open lock's writes are kept: Unbound's choice, unmeasured.)
            Assert.Equal(1, second[0]);
            Assert.Equal(11, second[1]);
            second[1] = 21;
            buffer.UnlockBufferAfterWrite<int>(1);
            unsafe
            {
                var mirror = (int*)buffer.Storage.Mirror;
                Assert.Equal(new[] { 1, 21, 12, 13 }, new[] { mirror[0], mirror[1], mirror[2], mirror[3] });
            }
            // SetData keeps the copy current for the next lock.
            buffer.SetData(new[] { 30 }, 0, 3, 1);
            var third = buffer.LockBufferForWrite<int>(3, 1);
            Assert.Equal(30, third[0]);
            buffer.Release();
            Assert.Throws<ObjectDisposedException>(() => third[0] = 31);
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
                Unbound.Runtime.MonoExceptions.MessageOf(Assert.Throws<ArgumentNullException>(() => block.SetTexture(id, (RenderTexture)null, RenderTextureSubElement.Depth))));
        }

        [Fact]
        public void KeywordsAndKeywordSets()
        {
            var made = new ShaderKeyword("SRPM_TEST_" + Guid.NewGuid().ToString("N"));
            Assert.True(made.IsValid());
            Assert.Equal(made.m_Index, new ShaderKeyword(made.name).m_Index);
            Assert.Null(default(ShaderKeyword).name);
            Assert.False(default(ShaderKeyword).IsValid());
            var errors = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try
            {
                var set = new ShaderKeywordSet();
                set.Enable(made);
                Assert.False(set.IsEnabled(made));
                Assert.Empty(set.GetShaderKeywords());
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
            // Every operation logs the Editor's two errors.
            Assert.Equal(6, errors.Count);
            Assert.Equal("ShaderKeywordSet cannot be used outside of IPreprocessShaders or IPreprocessComputeShaders.", errors[0]);
            Assert.Equal("ShaderKeywordSet has been created manually. This is not supported. Operations on this object will have no effect.", errors[1]);
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
                    Unbound.Runtime.MonoExceptions.MessageOf(Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 5))));
                Assert.Equal("Argument lightProbeProxyVolume must not be null if lightProbeUsage is set to UseProxyVolume.\nParameter name: lightProbeProxyVolume",
                    Unbound.Runtime.MonoExceptions.MessageOf(Assert.Throws<ArgumentException>(() => Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 3, null,
                        ShadowCastingMode.On, true, 0, null, LightProbeUsage.UseProxyVolume, null))));
                Assert.Throws<NullReferenceException>(() => Graphics.DrawMeshInstanced(mesh, 0, material, (Matrix4x4[])null));
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 0);
                Assert.Empty(Immediate.Queued);
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 2, null, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off, null);
                Assert.Equal(2, Immediate.Queued.Count);
                // A proxy volume lights the instances as Blend Probes does.
                Immediate.Queued.Clear();
                var volume = new GameObject("Volume").AddComponent<LightProbeProxyVolume>();
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 1, null, ShadowCastingMode.On, true, 0, null, LightProbeUsage.UseProxyVolume, volume);
                Assert.Equal(LightProbeUsage.BlendProbes, Assert.Single(Immediate.Queued).Lighting.Probes);
            }
            finally
            {
                Immediate.Queued.Clear();
            }
        }
    }
}
