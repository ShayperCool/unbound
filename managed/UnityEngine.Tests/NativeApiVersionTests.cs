using System;
using System.Reflection;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>NativeApi 14 (render protocol 6, wrapW in the texture packets): the managed side
    /// refuses a native side of any other version at startup, older or newer, before anything is
    /// installed.</summary>
    public unsafe class NativeApiVersionTests
    {
        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", BindingFlags.Static | BindingFlags.NonPublic);

        [Fact]
        public void TheVersionIsRenderProtocolSixWithWrapW()
        {
            Assert.Equal(14u, NativeApi.CurrentVersion);
            Assert.Equal(1UL << 62, TextureElements.DepthElement);
        }

        [Theory]
        [InlineData(13u)]
        [InlineData(15u)]
        public void AnotherNativeVersionIsRejectedBeforeInstalling(uint version)
        {
            var before = (NativeApi)Api.GetValue(null);
            var ready = (bool)Ready.GetValue(null);
            var api = new NativeApi { Version = version };
            InvalidOperationException error = null;
            try { Native.Initialize(&api); }
            catch (InvalidOperationException e) { error = e; }
            Assert.NotNull(error);
            Assert.Equal($"native API version {version}, managed expects 14", error.Message);
            Assert.Equal(before.Version, ((NativeApi)Api.GetValue(null)).Version);
            Assert.Equal(ready, (bool)Ready.GetValue(null));
        }
    }
}
