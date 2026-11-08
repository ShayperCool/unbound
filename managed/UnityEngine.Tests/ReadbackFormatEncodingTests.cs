using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using Xunit;
using UnityEngine.Rendering;
using System;

namespace UnityEngine.Tests
{
    public class ReadbackFormatEncodingTests
    {
        [Theory]
        [InlineData(1, new byte[] { 64, 255, 255, 255 })]
        [InlineData(2, new byte[] { 64, 128, 255, 255 })]
        [InlineData(4, new byte[] { 64, 128, 192, 32 })]
        public void ByteColorConversionUsesSourceComponents(int channels, byte[] expected)
        {
            var source = new byte[] { 64, 128, 192, 32 };
            Assert.Equal(expected, ReadbackFormat.Of(UnityEngine.TextureFormat.RGBA32).Encode(source, 1, false, sourceChannels: channels));
        }

        [Fact]
        public void AlphaOnlyConversionKeepsItsAlphaAndZeroRgb()
        {
            Assert.Equal(new byte[] { 0, 0, 0, 64 }, ReadbackFormat.Of(UnityEngine.TextureFormat.RGBA32).Encode(new byte[] { 64 }, 1, false, alpha: true, sourceChannels: 1));
        }

        [Fact]
        public void UnsupportedAlphaReadbackRetainsDimensionsAndCompletesWithNoData()
        {
            var source = new Texture2D(16, 1, TextureFormat.Alpha8, false);
            bool called = false;
            try
            {
                var request = AsyncGPUReadback.Request(source, 0, r =>
                {
                    Assert.True(r.done);
                    Assert.True(r.hasError);
                    Assert.Equal(16, r.width);
                    Assert.Equal(1, r.height);
                    Assert.Equal(1, r.depth);
                    Assert.Equal(0, r.layerCount);
                    Assert.Throws<InvalidOperationException>(() => r.GetData<byte>());
                    called = true;
                });
                Assert.True(called);
                Assert.True(request.done);
                Assert.True(request.hasError);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
