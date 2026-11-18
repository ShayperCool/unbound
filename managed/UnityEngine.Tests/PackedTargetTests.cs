using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Packed render targets and legacy formats a device cannot render (tests/projects/
    /// samplerstates Packed.*, the Linux OpenGL Core and Vulkan Editors).</summary>
    public class PackedTargetTests
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
            internal readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(logType + ": " + string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        /// <summary>The texture packet says which targets keep their channels' bits: 5-6-5 and
        /// 1-5-5-5 on the Linux devices, not 4444 or RGBA8; 4444 too on Metal (samplerstates
        /// metal.jsonl Packed.ARGB4444.*), not RGBA8 or float there.</summary>
        [Theory]
        [InlineData("OpenGLCore", RenderTextureFormat.RGB565, 1 << 17)]
        [InlineData("Vulkan", RenderTextureFormat.RGB565, 1 << 17)]
        [InlineData("OpenGLCore", RenderTextureFormat.ARGB1555, 2 << 17)]
        [InlineData("OpenGLCore", RenderTextureFormat.ARGB4444, 0)]
        [InlineData("OpenGLCore", RenderTextureFormat.ARGB32, 0)]
        [InlineData("Metal", RenderTextureFormat.RGB565, 1 << 17)]
        [InlineData("Metal", RenderTextureFormat.ARGB1555, 2 << 17)]
        [InlineData("Metal", RenderTextureFormat.ARGB4444, 3 << 17)]
        [InlineData("Metal", RenderTextureFormat.ARGB32, 0)]
        [InlineData("Metal", RenderTextureFormat.ARGBFloat, 0)]
        public void PackedTargetsSayWhichChannelBitsTheyKeep(string device, RenderTextureFormat format, int bits)
        {
            Assert.Equal(bits, On(device, () =>
            {
                var rt = new RenderTexture(new RenderTextureDescriptor(2, 1, GraphicsFormatUtility.GetGraphicsFormat(format, RenderTextureReadWrite.Linear), 0));
                try { return rt.PackedChannelBits(); }
                finally { Object.DestroyImmediate(rt); }
            }));
        }

        /// <summary>The Vulkan Editor makes an R8G8B8A8_UNorm target of `new RenderTexture` with
        /// ARGB4444 and warns; OpenGL Core renders 4444 (control), and a descriptor's GraphicsFormat
        /// the device cannot render still throws.</summary>
        [Fact]
        public void ALegacyFormatTheDeviceCannotRenderFallsBackWithTheEditorsWarning()
        {
            var handler = Debug.unityLogger.logHandler;
            var collect = new Collect();
            try
            {
                Debug.unityLogger.logHandler = collect;
                var (vulkan, gl) = (On("Vulkan", () => Format(RenderTextureFormat.ARGB4444)), On("OpenGLCore", () => Format(RenderTextureFormat.ARGB4444)));
                Assert.Equal(GraphicsFormat.R8G8B8A8_UNorm, vulkan);
                Assert.Equal(GraphicsFormat.B4G4R4A4_UNormPack16, gl);
                Assert.Equal(new[] { "Warning: 'B4G4R4A4_UNormPack16' is not supported. RenderTexture::GetTemporary fallbacks to R8G8B8A8_UNorm format on this platform. Use 'SystemInfo.IsFormatSupported' C# API to check format support." },
                    collect.Lines);
                On("Vulkan", () => Assert.Throws<ArgumentException>(() => new RenderTexture(new RenderTextureDescriptor(2, 1, GraphicsFormat.B4G4R4A4_UNormPack16, 0))));
                // A descriptor of the RenderTextureFormat falls back alike, warned once
                // (tests/projects/samplerstates Packed.ARGB4444.mip.* on Vulkan).
                collect.Lines.Clear();
                var described = On("Vulkan", () => new RenderTextureDescriptor(2, 2, RenderTextureFormat.ARGB4444, 0).graphicsFormat);
                Assert.Equal(GraphicsFormat.R8G8B8A8_UNorm, described);
                Assert.Single(collect.Lines);
                Assert.Equal(GraphicsFormat.B4G4R4A4_UNormPack16,
                    On("OpenGLCore", () => new RenderTextureDescriptor(2, 2, RenderTextureFormat.ARGB4444, 0).graphicsFormat));
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }

            static GraphicsFormat Format(RenderTextureFormat format)
            {
                var rt = new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear);
                try { return rt.graphicsFormat; }
                finally { Object.DestroyImmediate(rt); }
            }
        }

        /// <summary>CommandBuffer.GetTemporaryRT with a legacy format the device cannot render
        /// falls back as RenderTexture.GetTemporary does, warned when the command is recorded
        /// (unmeasured for the command buffer); OpenGL Core keeps 4444 (control).</summary>
        [Fact]
        public void ACommandBufferTemporaryOfALegacyFormatFallsBackWhenRecorded()
        {
            var handler = Debug.unityLogger.logHandler;
            var collect = new Collect();
            int id = Shader.PropertyToID("_PackedTemporary");
            GraphicsFormat Temporary(string device) => On(device, () =>
            {
                using var cb = new UnityEngine.Rendering.CommandBuffer();
                cb.GetTemporaryRT(id, 2, 1, 0, FilterMode.Point, RenderTextureFormat.ARGB4444);
                int recorded = collect.Lines.Count;
                Graphics.ExecuteCommandBuffer(cb);
                Assert.Equal(recorded, collect.Lines.Count);
                var rt = (RenderTexture)Shader.GetGlobalTexture(id);
                var format = rt.graphicsFormat;
                using var release = new UnityEngine.Rendering.CommandBuffer();
                release.ReleaseTemporaryRT(id);
                Graphics.ExecuteCommandBuffer(release);
                return format;
            });
            try
            {
                Debug.unityLogger.logHandler = collect;
                Assert.Equal(GraphicsFormat.R8G8B8A8_UNorm, Temporary("Vulkan"));
                Assert.Single(collect.Lines, l => l.Contains("'B4G4R4A4_UNormPack16' is not supported"));
                Assert.Equal(GraphicsFormat.B4G4R4A4_UNormPack16, Temporary("OpenGLCore"));
                Assert.Single(collect.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
        }
    }
}
