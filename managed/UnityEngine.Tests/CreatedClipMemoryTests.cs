using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Profiling;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class CreatedClipMemoryTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;
        public CreatedClipMemoryTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose() => ProjectInfo.UnityVersion = m_Release;
        static void Read(float[] data) => Array.Clear(data, 0, data.Length);

        [Theory]
        [InlineData(false, false, 4936L)]
        [InlineData(false, true, 4936L)]
        [InlineData(true, false, 142056L)]
        [InlineData(true, true, 142056L)]
        public void TheCreationOptionControlsStreamBufferAccounting(bool stream, bool callback, long expected)
        {
            var clip = AudioClip.Create("probe", 1000, 1, 44100, stream, callback ? Read : null);
            try { Assert.Equal(expected, Profiler.GetRuntimeMemorySizeLong(clip)); }
            finally { Object.DestroyImmediate(clip); }
        }

        [Theory]
        [InlineData(32002, false, 205768L)]
        [InlineData(32002, true, 205768L)]
        [InlineData(32003, false, 205784L)]
        [InlineData(32003, true, 205784L)]
        public void EachDecodeBufferTruncatesItsOwnFrameCount(int rate, bool callback, long expected)
        {
            var clip = AudioClip.Create("probe", 3217, 2, rate, true, callback ? Read : null);
            try { Assert.Equal(expected, Profiler.GetRuntimeMemorySizeLong(clip)); }
            finally { Object.DestroyImmediate(clip); }
        }

        [Fact]
        public void AnotherRateAndLengthUseTheSameAccounting()
        {
            var clip = AudioClip.Create("probe", 999, 1, 12347, true);
            try { Assert.Equal(40440L, Profiler.GetRuntimeMemorySizeLong(clip)); }
            finally { Object.DestroyImmediate(clip); }
        }

        [Fact]
        public void OtherReleasesRetainTheirUnmeasuredPreviousAccounting()
        {
            ProjectInfo.UnityVersion = "6000.0.70f1";
            var clip = AudioClip.Create("probe", 1000, 1, 44100, true);
            try { Assert.Equal(4936L, Profiler.GetRuntimeMemorySizeLong(clip)); }
            finally { Object.DestroyImmediate(clip); }
        }
    }
}
