using System;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    public class TextAssetEncodingTests
    {
        // Identical bytes in imported .txt/.bytes, byte constructors, and their clones:
        // two agreeing Native assetclones captures. Stored bytes remain unchanged.
        [Theory]
        [InlineData("YWxwaGEAzqkK", "alpha\0Ω\n")]
        [InlineData("77u/YWxwaGEAzqkK", "alpha\0Ω\n")]
        [InlineData("//5hAGwAcABoAGEAAACpAwoA", "alpha\0Ω\n")]
        [InlineData("/v8AYQBsAHAAaABhAAADqQAK", "alpha\0Ω\n")]
        [InlineData("//4AAGEAAABsAAAAcAAAAGgAAABhAAAAAAAAAKkDAAAKAAAA", "alpha\0Ω\n")]
        [InlineData("AAD+/wAAAGEAAABsAAAAcAAAAGgAAABhAAAAAAAAA6kAAAAK", "alpha\0Ω\n")]
        [InlineData("YmFk/8CvCg==", "bad���\n")]
        [InlineData("77u/Y2xvbmUAYmluYXJ5/wo=", "\uFEFFclone\0binary�\n")]
        public void TextViewsDetectEncodingWithoutChangingStorage(string base64, string text)
        {
            var bytes = Convert.FromBase64String(base64);
            var source = new TextAsset(new ReadOnlySpan<byte>(bytes));
            var clone = Object.Instantiate(source);
            Assert.Equal(text, source.text);
            Assert.Equal(text, clone.text);
            Assert.Equal(bytes, source.bytes);
            Assert.Equal(bytes, clone.bytes);
            Object.DestroyImmediate(source);
            Assert.Equal(text, clone.text);
            Object.DestroyImmediate(clone);
        }
    }
}
