using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>A command buffer executed outside a camera's rendering draws with identity view and
    /// projection, also after a camera set others; the stream's matrices come back after it
    /// (tests/projects/nativegfx metal.jsonl outside.*).</summary>
    public class OutsideCameraCommandsTests
    {
        static byte[] CameraOf(Matrix4x4 view, Matrix4x4 projection)
        {
            var bytes = new System.Collections.Generic.List<byte> { 6 };
            foreach (var m in new[] { view, projection })
                for (int i = 0; i < 16; i++) bytes.AddRange(BitConverter.GetBytes(m[i]));
            return bytes.ToArray();
        }

        static int Occurrences(byte[] bytes, byte[] part)
        {
            int n = 0;
            for (int at = 0; at + part.Length <= bytes.Length; at++)
                if (bytes.AsSpan(at, part.Length).SequenceEqual(part)) n++;
            return n;
        }

        [Fact]
        public void ExecutedCommandsStartFromIdentityMatrices()
        {
            var saved = GfxStream.Current;
            var view = Matrix4x4.TRS(new Vector3(0, 0, -5), Quaternion.identity, Vector3.one);
            var projection = Matrix4x4.Perspective(60f, 1f, 0.3f, 100f);
            try
            {
                GfxStream.Camera(view, projection);
                GfxStream.Flush();
                using (var cmd = new CommandBuffer())
                    Graphics.ExecuteCommandBuffer(cmd);
                var recorded = GfxStream.Recorded.ToArray();
                GfxStream.Flush();
                Assert.Equal(1, Occurrences(recorded, CameraOf(Matrix4x4.identity, Matrix4x4.identity)));
                Assert.Equal(1, Occurrences(recorded, CameraOf(view, projection)));
                Assert.Equal(view, GfxStream.Current.View);
                Assert.Equal(projection, GfxStream.Current.Projection);
            }
            finally
            {
                GfxStream.Restore(saved);
            }
        }
    }
}
