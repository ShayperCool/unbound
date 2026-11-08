using System;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NativeApi 31 (the reported format of imported textures, the decoded-resampling flag (30), cached display/IME frame versions and filter component descriptors at the end of AudioSourcePacket, with
    /// AudioReverbFilter's since 20, the reverb zone of AudioFramePacket since 21, Distortion
    /// kind 5 since 23, Chorus kind 6 since 24, mixer Flange since 25, Normalize since 26, Pitch Shifter since 27; render protocol 8: external 16-bit indices, protocol 9: depth copies,
    /// protocol 10: 32-bit indices, protocol 11: logical instance IDs, and protocol 12: shared
    /// property arrays and mesh-instance elements; 29 adds the decoded audio decoded-resampling flag. The managed side
    /// refuses a native side of any other version at startup, older or newer, before anything is
    /// installed.</summary>
    public unsafe class NativeApiVersionTests
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);

        [Fact]
        public void TheVersionIncludesCachedDisplayAndImeFrameVersions()
        {
            Assert.Equal(31u, NativeApi.CurrentVersion);
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
        [InlineData(32u)]
        public void AnotherNativeVersionIsRejectedBeforeInstalling(uint version)
        {
            var before = (NativeApi)Api.GetValue(null);
            var ready = (bool)Ready.GetValue(null);
            var api = new NativeApi { Version = version };
            InvalidOperationException error = null;
            try { Native.Initialize(&api); }
            catch (InvalidOperationException e) { error = e; }
            Assert.NotNull(error);
            Assert.Equal($"native API version {version}, managed expects 31", error.Message);
            Assert.Equal(before.Version, ((NativeApi)Api.GetValue(null)).Version);
            Assert.Equal(ready, (bool)Ready.GetValue(null));
        }
    }
}
