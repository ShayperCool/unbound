using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using Xunit;
using UnityEngine.Rendering;
using System;

namespace UnityEngine.Tests
{
    public class ReadbackFormatEncodingTests
    {
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
