using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Profiling;
using Xunit;
using Object = UnityEngine.Object;
namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class TextAssetMemoryTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        public TextAssetMemoryTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose() => ProjectInfo.UnityVersion = m_Release;

        [Theory]
        [InlineData(0,160L)]
        [InlineData(31,160L)]
        [InlineData(32,193L)]
        [InlineData(33,194L)]
        [InlineData(37,198L)]
        [InlineData(32768,32929L)]
        public void BinaryPayloadUsesInlineStorageUpToTheByteBoundary(int length,long expected)
        {
            var asset = new TextAsset(new byte[length]);
            try { Assert.Equal(expected,Profiler.GetRuntimeMemorySizeLong(asset)); }
            finally { Object.DestroyImmediate(asset); }
        }

        [Theory]
        [InlineData(15,160L)]
        [InlineData(16,193L)]
        [InlineData(1000,2161L)]
        public void TextPayloadCountsUtf8BytesRatherThanCharacters(int length,long expected)
        {
            var asset = new TextAsset(new string('\u03a9',length));
            try { Assert.Equal(expected,Profiler.GetRuntimeMemorySizeLong(asset)); }
            finally { Object.DestroyImmediate(asset); }
        }

        [Fact]
        public void ClonesRetainPayloadAccountingAfterSourceDestructionAndNameChanges()
        {
            var source = new TextAsset(new byte[1000]);
            var copy = Object.Instantiate(source);
            try
            {
                Object.DestroyImmediate(source);
                copy.name = new string('n',1000);
                Assert.Equal(1161,Profiler.GetRuntimeMemorySizeLong(copy));
                copy.name = "short";
                _ = copy.text;
                _ = copy.bytes;
                Assert.Equal(1161,Profiler.GetRuntimeMemorySizeLong(copy));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(copy); }
        }

        [Fact]
        public void OtherReleasesKeepTheUnmeasuredFallback()
        {
            ProjectInfo.UnityVersion = "2022.3.62f1";
            var asset = new TextAsset(new byte[1000]);
            try { Assert.Equal(0,Profiler.GetRuntimeMemorySizeLong(asset)); }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
