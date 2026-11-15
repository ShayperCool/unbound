using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>A command buffer executed outside a camera's rendering starts from GL's matrices
    /// (identity unless GL loaded others; not a camera's that rendered before, even one left
    /// current), and the stream's matrices come back after it (tests/projects/nativegfx
    /// metal.jsonl outside.*).</summary>
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

        static byte[] Execute()
        {
            GfxStream.Flush();
            using (var cmd = new CommandBuffer())
                Graphics.ExecuteCommandBuffer(cmd);
            var recorded = GfxStream.Recorded.ToArray();
            GfxStream.Flush();
            return recorded;
        }

        /// <summary>The frame's cleanup leaves no render texture active for the next frame
        /// (tests/projects/nativegfx metal.jsonl active.nextframe).</summary>
        [Fact]
        public void TheNextFrameStartsWithNoActiveRenderTexture()
        {
            FrameBuilder.EnsureRegistered();
            var previous = RenderTexture.ActiveTarget;
            var texture = new RenderTexture(4, 4, 0);
            try
            {
                RenderTexture.active = texture;
                Assert.Same(texture, RenderTexture.active);
                typeof(PlayerLoopDriver).GetMethod("CleanupFrame",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
                Assert.Null(RenderTexture.active);
            }
            finally
            {
                RenderTexture.SetActiveQuietly(previous);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void ExecutedCommandsStartFromGlMatrices()
        {
            var saved = GfxStream.Current;
            var previous = Camera.current;
            var view = Matrix4x4.TRS(new Vector3(0, 0, -5), Quaternion.identity, Vector3.one);
            var projection = Matrix4x4.Perspective(60f, 1f, 0.3f, 100f);
            var go = new GameObject("left current");
            try
            {
                GfxStream.Camera(view, projection);
                // A camera left current (as a pipeline leaves it) is not rendering.
                Camera.Internal_SetCurrent(go.AddComponent<Camera>());
                var recorded = Execute();
                Assert.Equal(1, Occurrences(recorded, CameraOf(Matrix4x4.identity, Matrix4x4.identity)));
                Assert.Equal(1, Occurrences(recorded, CameraOf(view, projection)));
                Assert.Equal(view, GfxStream.Current.View);
                Assert.Equal(projection, GfxStream.Current.Projection);
                GL.PushMatrix();
                GL.LoadOrtho();
                try
                {
                    recorded = Execute();
                    Assert.Equal(1, Occurrences(recorded, CameraOf(Matrix4x4.identity, Immediate.Ortho)));
                }
                finally { GL.PopMatrix(); }
                // Within a camera's rendering the commands keep the camera's matrices.
                FrameBuilder.BeginCameraRendering();
                try { recorded = Execute(); }
                finally { FrameBuilder.EndCameraRendering(); }
                Assert.Equal(0, Occurrences(recorded, CameraOf(Matrix4x4.identity, Matrix4x4.identity)));
            }
            finally
            {
                Camera.Internal_SetCurrent(previous);
                GfxStream.Restore(saved);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
