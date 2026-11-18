using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>What an Append or Counter buffer's hidden counter does that the device decides
    /// (tests/projects/gpubuffers, the OpenGL Core Editor and the macOS Metal Editor): SetData sets
    /// it to 0 on Metal and keeps it on OpenGL Core; CopyCount past the destination's end copies
    /// nothing, asserting on OpenGL Core only.</summary>
    public sealed class BufferCounterDeviceTests
    {
        static T On<T>(string device, Func<T> body)
        {
            var previous = GraphicsDeviceProfile.Current;
            try
            {
                GraphicsDeviceProfile.Select(device);
                return body();
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }

        sealed class Collect : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(logType + ": " + string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        /// <summary>The SetCounter commands (op 25: buffer handle, value) recorded while
        /// <paramref name="body"/> runs.</summary>
        static List<(ulong Buffer, uint Value)> CounterSets(Action body)
        {
            GfxStream.Flush();
            body();
            var recorded = GfxStream.Recorded.ToArray();
            GfxStream.Flush();
            var sets = new List<(ulong, uint)>();
            for (int at = 0; at + 13 <= recorded.Length; at++)
                if (recorded[at] == 25)
                    sets.Add((BitConverter.ToUInt64(recorded, at + 1), BitConverter.ToUInt32(recorded, at + 9)));
            return sets;
        }

        [Theory]
        [InlineData("Metal", ComputeBufferType.Append, true)]
        [InlineData("Metal", ComputeBufferType.Counter, true)]
        [InlineData("Metal", ComputeBufferType.Default, false)]
        [InlineData("OpenGLCore", ComputeBufferType.Append, false)]
        [InlineData("Vulkan", ComputeBufferType.Counter, false)]
        public void SetDataSetsTheCounterToZeroWhereTheDeviceDoes(string device, ComputeBufferType type, bool resets)
        {
            On(device, () =>
            {
                using var buffer = new ComputeBuffer(8, 4, type);
                ulong handle = buffer.Handle;
                var sets = CounterSets(() =>
                {
                    buffer.SetCounterValue(14);
                    buffer.SetData(new uint[8]);
                });
                var expected = new List<(ulong, uint)>();
                if (type != ComputeBufferType.Default) expected.Add((handle, 14u));
                if (resets) expected.Add((handle, 0u));
                // Only this buffer's commands count: other tests' buffers are not recorded here.
                Assert.Equal(expected, sets.FindAll(s => s.Buffer == handle));
                return 0;
            });
        }

        [Theory]
        [InlineData("OpenGLCore", true)]
        [InlineData("Metal", false)]
        // Unmeasured on Vulkan, which keeps OpenGL Core's assertion.
        [InlineData("Vulkan", true)]
        public void CopyCountPastTheEndAssertsWhereTheDeviceDoes(string device, bool asserts)
        {
            var handler = Debug.unityLogger.logHandler;
            var collect = new Collect();
            try
            {
                Debug.unityLogger.logHandler = collect;
                On(device, () =>
                {
                    using var append = new ComputeBuffer(8, 4, ComputeBufferType.Append);
                    using var args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
                    ComputeBuffer.CopyCount(append, args, 16);
                    // In range: no assertion on any device (the control).
                    ComputeBuffer.CopyCount(append, args, 12);
                    return 0;
                });
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
            Assert.Equal(asserts ? new[] { "Assert: Assertion failed on expression: 'dstOffset + size <= m_size'" } : Array.Empty<string>(),
                collect.Lines.ToArray());
        }
    }
}
