using System;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class AstcTextureMemoryTests : IDisposable
    {
        static readonly FieldInfo Workspace = typeof(MemorySizes).GetField("s_AstcFallbackFormats",
            BindingFlags.Static | BindingFlags.NonPublic);

        readonly object m_PreviousWorkspace = Workspace.GetValue(null);
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;
        readonly string m_PreviousThreading = GraphicsDeviceProfile.CurrentThreadingMode.ToString();

        public AstcTextureMemoryTests()
        {
            Workspace.SetValue(null, 0u);
            GraphicsDeviceProfile.Select("OpenGLCore");
        }

        public void Dispose()
        {
            Workspace.SetValue(null, m_PreviousWorkspace);
            GraphicsDeviceProfile.Select(m_PreviousDevice.Type.ToString(), m_PreviousThreading);
        }

        [Fact]
        public void FirstInitializedTextureOwnsTheWorkspaceRegardlessOfReadOrder()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var pending = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true, true);
            var first = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
            var later = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
            try
            {
                Assert.Equal(1256, Profiler.GetRuntimeMemorySizeLong(later));
                Assert.Equal(1000, Profiler.GetRuntimeMemorySizeLong(pending));
                Assert.Equal(4456, Profiler.GetRuntimeMemorySizeLong(first));
                pending.Apply(false, false);
                Assert.Equal(1256, Profiler.GetRuntimeMemorySizeLong(pending));
                Assert.True(first.Reinitialize(16, 24, TextureFormat.RGBA32, false));
                Assert.Equal(5616, Profiler.GetRuntimeMemorySizeLong(first));
            }
            finally
            {
                Object.DestroyImmediate(pending);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(later);
            }
        }

        [Fact]
        public void InitializedArrayStartsTheWorkspaceBeforeItsFirstApply()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var array = new Texture2DArray(8, 8, 2, TextureFormat.ASTC_4x4, false, true);
            var texture = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
            try
            {
                Assert.Equal(1360, Profiler.GetRuntimeMemorySizeLong(array));
                Assert.Equal(1256, Profiler.GetRuntimeMemorySizeLong(texture));
            }
            finally
            {
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void NewFootprintAddsSetupBytesToTheTextureThatUsesItFirst()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var first = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
            var second = new Texture2D(8, 8, TextureFormat.ASTC_5x5, false, true);
            var color = new Texture2D(8, 8, TextureFormat.ASTC_5x5, false, false);
            try
            {
                Assert.Equal(1256, Profiler.GetRuntimeMemorySizeLong(color));
                Assert.Equal(1264, Profiler.GetRuntimeMemorySizeLong(second));
                Assert.Equal(4456, Profiler.GetRuntimeMemorySizeLong(first));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(color);
            }
        }

        [Fact]
        public void UnsupportedExplicitGraphicsFormatsLeaveTheLegacyFallbackCold()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var graphics = GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.ASTC_4x4, false);
            Assert.False(SystemInfo.IsFormatSupported(graphics, GraphicsFormatUsage.Sample));
            var cube = new Cubemap(8, graphics, TextureCreationFlags.None);
            var array = new Texture2DArray(8, 8, 2, graphics, TextureCreationFlags.None);
            Texture2D first = null;
            try
            {
                if (cube != null) cube.Apply(false, false);
                if (array != null) array.Apply(false, false);
                first = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
                Assert.Equal(4456, Profiler.GetRuntimeMemorySizeLong(first));
            }
            finally
            {
                Object.DestroyImmediate(cube);
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(first);
            }
        }

        [Fact]
        public void ImportedCubemapWithoutAnUploadLeavesTheFallbackCold()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var cube = new Cubemap(8, TextureFormat.RGBA32, false);
            // An imported wrapper can know its block format before its source bytes are available.
            cube.IsPersistentAsset = true;
            cube.m_SourceFormat = TextureFormat.ASTC_4x4;
            Texture2D first = null;
            try
            {
                cube.Apply(false, false);
                first = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
                Assert.Equal(4456, Profiler.GetRuntimeMemorySizeLong(first));
            }
            finally
            {
                Object.DestroyImmediate(cube, true);
                Object.DestroyImmediate(first);
            }
        }

        [Fact]
        public void HdrFallbackUsesHalfFloatBytesInPerObjectAccounting()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            long previous = Profiler.GetAllocatedMemoryForGraphicsDriver();
            var texture = new Texture2D(8, 8, TextureFormat.ASTC_HDR_4x4, false, true);
            try
            {
                Assert.Equal(4712, Profiler.GetRuntimeMemorySizeLong(texture));
                Assert.Equal(previous + 256, Profiler.GetAllocatedMemoryForGraphicsDriver());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void InitializedHdrCloneKeepsItsStorageAccountingAndCanOwnANewFootprint()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var source = new Texture2D(8, 8, TextureFormat.ASTC_HDR_4x4, false, true);
            Texture2D copy = null;
            try
            {
                copy = Object.Instantiate(source);
                Assert.Equal(1512, Profiler.GetRuntimeMemorySizeLong(copy));
                Assert.True(copy.Reinitialize(8, 8, TextureFormat.ASTC_5x5, false));
                Assert.Equal(944, Profiler.GetRuntimeMemorySizeLong(copy));
                copy.Apply(false, false);
                Assert.Equal(1264, Profiler.GetRuntimeMemorySizeLong(copy));
                Assert.Equal(4712, Profiler.GetRuntimeMemorySizeLong(source));
            }
            finally
            {
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(source);
            }
        }

        [Fact]
        public void InitializedUncompressedCloneCanOwnTheFirstAstcSetup()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var source = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            Texture2D copy = null;
            try
            {
                copy = Object.Instantiate(source);
                Assert.True(copy.Reinitialize(8, 8, TextureFormat.ASTC_4x4, false));
                Assert.Equal(944, Profiler.GetRuntimeMemorySizeLong(copy));
                copy.Apply(false, false);
                Assert.Equal(4456, Profiler.GetRuntimeMemorySizeLong(copy));
            }
            finally
            {
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(source);
            }
        }

        [Fact]
        public void UnmeasuredReleaseDoesNotClaimTheWorkspace()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var texture = new Texture2D(8, 8, TextureFormat.ASTC_4x4, false, true);
            try
            {
                Assert.Equal(0u, (uint)Workspace.GetValue(null));
                Assert.Equal(1256, Profiler.GetRuntimeMemorySizeLong(texture));
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
