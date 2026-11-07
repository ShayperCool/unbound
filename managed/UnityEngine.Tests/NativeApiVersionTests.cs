using System;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NativeApi 25 (filter component descriptors at the end of AudioSourcePacket, with
    /// AudioReverbFilter's since 20, the reverb zone of AudioFramePacket since 21, Distortion
    /// kind 5 since 23, Chorus kind 6 since 24, mixer Flange since 25; render protocol 8: external 16-bit indices, protocol 9: depth copies,
    /// protocol 10: 32-bit indices, and protocol 11: logical instance IDs. The managed side
    /// refuses a native side of any other version at startup, older or newer, before anything is
    /// installed.</summary>
    public unsafe class NativeApiVersionTests
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);

        [Fact]
        public void TheVersionIsAudioFilterDescriptorsAndRenderProtocolEleven()
        {
            Assert.Equal(25u, NativeApi.CurrentVersion);
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
        [InlineData(26u)]
        public void AnotherNativeVersionIsRejectedBeforeInstalling(uint version)
        {
            var before = (NativeApi)Api.GetValue(null);
            var ready = (bool)Ready.GetValue(null);
            var api = new NativeApi { Version = version };
            InvalidOperationException error = null;
            try { Native.Initialize(&api); }
            catch (InvalidOperationException e) { error = e; }
            Assert.NotNull(error);
            Assert.Equal($"native API version {version}, managed expects 25", error.Message);
            Assert.Equal(before.Version, ((NativeApi)Api.GetValue(null)).Version);
            Assert.Equal(ready, (bool)Ready.GetValue(null));
        }
    }
}
