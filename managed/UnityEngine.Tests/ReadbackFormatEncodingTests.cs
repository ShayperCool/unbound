using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class ReadbackFormatEncodingTests
    {
        [Fact]
        public void DeclaredAlpha8ReadbackPreservesEveryStoredAlphaByte()
        {
            var source = new byte[256];
            for (int i = 0; i < source.Length; i++) source[i] = (byte)i;
            var own = ReadbackFormat.Of(GraphicsFormats.Texture2DAlpha8);
            Assert.NotNull(own);
            Assert.Equal(source, own.Encode(source, source.Length, floats: false, alpha: true));
            // Explicit red-channel conversion is a different destination from the stored alpha.
            Assert.Equal(new byte[256], ReadbackFormat.Of(GraphicsFormat.R8_UNorm).Encode(source, source.Length, floats: false, alpha: true));
        }
    }
}
