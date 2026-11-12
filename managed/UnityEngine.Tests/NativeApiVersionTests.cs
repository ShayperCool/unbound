using System;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The managed/native boundary includes mesh GPU bindings and floating-point
    /// vertex colors, texture metadata, cached input versions and audio descriptors. Another
    /// version is rejected before installing any function pointers.</summary>
    public unsafe class NativeApiVersionTests
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);

        [Fact]
        public void TheVersionIncludesCachedDisplayAndImeFrameVersions()
        {
            Assert.Equal(64u, NativeApi.CurrentVersion);
            Assert.Equal(5, AudioCallbackKind.PrepareRenderBuffer);
            Assert.Equal(6, AudioCallbackKind.RenderBufferScope);
            Assert.Equal(7, AudioCallbackKind.ReleaseRenderBuffers);
            Assert.Equal(56, System.Runtime.InteropServices.Marshal.SizeOf<ImportedTextureInfo>());
            Assert.Equal(52, (int)System.Runtime.InteropServices.Marshal.OffsetOf<ImportedTextureInfo>("TextureFormat"));
            Assert.Equal(112, System.Runtime.InteropServices.Marshal.SizeOf<FrameInput>());
            Assert.Equal(96, (int)System.Runtime.InteropServices.Marshal.OffsetOf<FrameInput>("DisplayVersion"));
            Assert.Equal(104, (int)System.Runtime.InteropServices.Marshal.OffsetOf<FrameInput>("ImeVersion"));
            Assert.Equal(1UL << 62, TextureElements.DepthElement);
        }

        [Theory]
        [InlineData(14u)]
        [InlineData(15u)]
        [InlineData(16u)]
        [InlineData(17u)]
        [InlineData(18u)]
        [InlineData(19u)]
        [InlineData(20u)]
        [InlineData(21u)]
        [InlineData(22u)]
        [InlineData(23u)]
        [InlineData(24u)]
        [InlineData(25u)]
        [InlineData(26u)]
        [InlineData(27u)]
        [InlineData(28u)]
        [InlineData(29u)]
        [InlineData(30u)]
        [InlineData(31u)]
        [InlineData(32u)]
        [InlineData(33u)]
        [InlineData(34u)]
        [InlineData(35u)]
        [InlineData(36u)]
        [InlineData(37u)]
        [InlineData(38u)]
        [InlineData(39u)]
        [InlineData(40u)]
        [InlineData(41u)]
        [InlineData(42u)]
        [InlineData(43u)]
        [InlineData(44u)]
        [InlineData(45u)]
        [InlineData(46u)]
        [InlineData(47u)]
        [InlineData(48u)]
        [InlineData(49u)]
        [InlineData(50u)]
        [InlineData(51u)]
        [InlineData(52u)]
        [InlineData(53u)]
        [InlineData(54u)]
        [InlineData(55u)]
        [InlineData(NativeApi.CurrentVersion - 1)]
        [InlineData(NativeApi.CurrentVersion + 1)]
        public void AnotherNativeVersionIsRejectedBeforeInstalling(uint version)
        {
            var before = (NativeApi)Api.GetValue(null);
            var ready = (bool)Ready.GetValue(null);
            var api = new NativeApi { Version = version };
            InvalidOperationException error = null;
            try { Native.Initialize(&api); }
            catch (InvalidOperationException e) { error = e; }
            Assert.NotNull(error);
            Assert.Equal($"native API version {version}, managed expects {NativeApi.CurrentVersion}", error.Message);
            Assert.Equal(before.Version, ((NativeApi)Api.GetValue(null)).Version);
            Assert.Equal(ready, (bool)Ready.GetValue(null));
        }
    }
}
