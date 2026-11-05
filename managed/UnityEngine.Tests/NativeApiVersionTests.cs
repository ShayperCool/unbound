using System;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NativeApi 18 (filter component descriptors at the end of AudioSourcePacket, and
    /// render protocol 8: draws that read a GraphicsBuffer's 16-bit indices): the managed side
    /// refuses a native side of any other version at startup, older or newer, before anything is
    /// installed.</summary>
    public unsafe class NativeApiVersionTests
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);

        [Fact]
        public void TheVersionIsAudioFilterDescriptorsAndRenderProtocolEight()
        {
            Assert.Equal(18u, NativeApi.CurrentVersion);
            Assert.Equal(1UL << 62, TextureElements.DepthElement);
        }

        [Theory]
        [InlineData(14u)]
        [InlineData(15u)]
        [InlineData(16u)]
        [InlineData(17u)]
        [InlineData(19u)]
        public void AnotherNativeVersionIsRejectedBeforeInstalling(uint version)
        {
            var before = (NativeApi)Api.GetValue(null);
            var ready = (bool)Ready.GetValue(null);
            var api = new NativeApi { Version = version };
            InvalidOperationException error = null;
            try { Native.Initialize(&api); }
            catch (InvalidOperationException e) { error = e; }
            Assert.NotNull(error);
            Assert.Equal($"native API version {version}, managed expects 18", error.Message);
            Assert.Equal(before.Version, ((NativeApi)Api.GetValue(null)).Version);
            Assert.Equal(ready, (bool)Ready.GetValue(null));
        }
    }
}
