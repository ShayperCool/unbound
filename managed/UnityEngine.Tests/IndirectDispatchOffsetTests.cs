using System;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The arguments an indirect dispatch reads at a byte offset that is not a multiple of 4,
    /// which the device decides: the Metal Editor reads them at the offset rounded down to one
    /// (tests/projects/cmdbuffers metal.jsonl `metal.dispatch.offset2`/`offset14`, gpubuffers metal
    /// `indirect.unaligned`), the OpenGL Core Editor dispatches nothing (gpubuffers
    /// `indirect.unaligned`).</summary>
    public sealed class IndirectDispatchOffsetTests
    {
        static (bool dispatched, uint offset) Read(string device, uint offset, int words = 6)
        {
            var previous = GraphicsDeviceProfile.Current;
            using var args = new ComputeBuffer(words, 4, ComputeBufferType.IndirectArguments);
            try
            {
                GraphicsDeviceProfile.Select(device);
                bool dispatched = GpuBufferOps.IndirectArguments(args.Storage, ref offset);
                return (dispatched, offset);
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }

        [Fact]
        public void MetalRoundsAnUnalignedOffsetDown()
        {
            Assert.Equal((true, 0u), Read("Metal", 2));
            Assert.Equal((true, 12u), Read("Metal", 14));
            // Rounded down, the arguments of offset 15 end at the buffer's end; aligned offsets keep
            // the end check (16 + 12 > 24).
            Assert.Equal((true, 12u), Read("Metal", 15));
            Assert.False(Read("Metal", 16).dispatched);
        }

        [Fact]
        public void OtherDevicesDispatchNothingAtAnUnalignedOffset()
        {
            Assert.False(Read("OpenGLCore", 2).dispatched);
            Assert.False(Read("OpenGLCore", 14).dispatched);
            Assert.Equal((true, 12u), Read("OpenGLCore", 12));
            Assert.Equal((true, 12u), Read("Metal", 12));
        }

        [Fact]
        public void ANullBufferThrowsBeforeTheOffsetIsRead()
        {
            uint offset = 2;
            Assert.Throws<ArgumentNullException>(() => GpuBufferOps.IndirectArguments(null, ref offset));
        }
    }
}
