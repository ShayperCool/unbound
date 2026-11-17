using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class AsyncReadbackValidationTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<(LogType Type, string Text)> Logs = new List<(LogType, string)>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Logs.Add((type, string.Format(format, args)));
            public void LogException(Exception exception, Object context) => Logs.Add((LogType.Exception, exception.Message));
        }

        [Theory]
        [InlineData("6000.0.71f1", TextureFormat.BGRA32, true)]
        [InlineData("6000.0.71f1", TextureFormat.BGRA32, false)]
        [InlineData("6000.0.71f1", TextureFormat.RGBA32, true)]
        [InlineData("6000.0.71f1", TextureFormat.RGBA32, false)]
        [InlineData("6000.0.70f1", TextureFormat.BGRA32, true)]
        [InlineData("6000.0.70f1", TextureFormat.BGRA32, false)]
        [InlineData("6000.0.71f1", TextureFormat.RGBA32, true, true)]
        [InlineData("6000.0.71f1", TextureFormat.RGBA32, false, true)]
        [InlineData("6000.0.71f1", TextureFormat.Alpha8, true, false, false, "OpenGLCore")]
        [InlineData("6000.0.71f1", TextureFormat.Alpha8, false, false, false, "OpenGLCore")]
        [InlineData("6000.0.71f1", TextureFormat.BGRA32, true, false, true)]
        public void SourceValidationKeepsFailureOrderAndDimensions(string release, TextureFormat format, bool explicitFormat, bool depthOnly = false, bool commandBuffer = false, string device = "Vulkan")
        {
            string previousRelease = ProjectInfo.UnityVersion;
            var previousDevice = GraphicsDeviceProfile.Current;
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            var capture = new Capture();
            Texture source = null;
            try
            {
                ProjectInfo.UnityVersion = release;
                GraphicsDeviceProfile.Select(device);
                source = depthOnly
                    ? new RenderTexture(3, 2, 24, RenderTextureFormat.Depth)
                    : new Texture2D(3, 2, format, false, true);
                logger.logHandler = capture;
                logger.logEnabled = true;
                logger.filterLogType = LogType.Log;
                AsyncGPUReadbackRequest request = default;
                bool alpha = format == TextureFormat.Alpha8;
                if (commandBuffer)
                {
                    bool called = false;
                    var command = new CommandBuffer();
                    try
                    {
                        command.RequestAsyncReadback(source, 2, GraphicsFormat.R8G8B8A8_UNorm, value => { called = true; request = value; });
                        Assert.Empty(capture.Logs);
                        Assert.False(called);
                        command.WaitAllAsyncReadbackRequests();
                        Graphics.ExecuteCommandBuffer(command);
                        Assert.True(called);
                    }
                    finally { command.Release(); }
                }
                else if (alpha)
                    request = explicitFormat
                        ? AsyncGPUReadback.Request(source, 0, TextureFormat.Alpha8)
                        : AsyncGPUReadback.Request(source, 0);
                else
                    request = explicitFormat
                        ? AsyncGPUReadback.Request(source, 2, GraphicsFormat.R8G8B8A8_UNorm)
                        : AsyncGPUReadback.Request(source, 2);
                Assert.True(request.done);
                Assert.True(request.hasError);
                Assert.Equal(alpha ? 3 : 0, request.width);
                Assert.Equal(alpha ? 2 : 0, request.height);
                Assert.Equal(alpha ? 1 : 0, request.depth);
                var expected = new List<(LogType, string)>();
                if (release == "6000.0.71f1" && (depthOnly || format == TextureFormat.BGRA32 || alpha) && explicitFormat && !commandBuffer)
                    expected.Add((LogType.Error, $"'{source.graphicsFormat}' doesn't support ReadPixels usage on this platform. Async GPU readback failed."));
                if (!alpha)
                    expected.Add((LogType.Error, depthOnly
                        ? "AsyncGPUReadback - None (0) graphics format is not supported as source for async read back"
                        : "AsyncGPUReadback - Invalid mipIndex: 2"));
                Assert.Equal(expected, capture.Logs);
            }
            finally
            {
                logger.logHandler = previousHandler;
                logger.logEnabled = previousEnabled;
                logger.filterLogType = previousFilter;
                if (source != null) Object.DestroyImmediate(source);
                GraphicsDeviceProfile.Select(previousDevice.Type.ToString());
                ProjectInfo.UnityVersion = previousRelease;
            }
        }

        [Theory]
        [InlineData("6000.0.71f1", "Vulkan", true)]
        [InlineData("6000.0.71f1", "OpenGLCore", false)]
        [InlineData("6000.0.70f1", "Vulkan", false)]
        public void AlphaReadPixelsSupportFollowsMeasuredDeviceProfile(string release, string device, bool supported)
        {
            string previousRelease = ProjectInfo.UnityVersion;
            var previousDevice = GraphicsDeviceProfile.Current;
            try
            {
                ProjectInfo.UnityVersion = release;
                GraphicsDeviceProfile.Select(device);
                Assert.Equal(supported, SystemInfo.IsFormatSupported(GraphicsFormatUtility.Texture2DAlpha8, GraphicsFormatUsage.ReadPixels));
            }
            finally
            {
                ProjectInfo.UnityVersion = previousRelease;
                GraphicsDeviceProfile.Select(previousDevice.Type.ToString());
            }
        }

        const string BlitRefusal = "Image blit conversion between formats 75 -> 52 is not supported";

        /// <summary>Metal reads targets whose format answers no ReadPixels usage, refusing only its
        /// unreadable formats (tests/projects/textureformats metal.jsonl readback.*); Vulkan
        /// refuses every such target.</summary>
        [Theory]
        [InlineData("Metal", GraphicsFormat.B8G8R8A8_UNorm, TextureFormat.RGBAFloat, null, false)]
        [InlineData("Metal", GraphicsFormat.B8G8R8A8_SRGB, TextureFormat.RGBA32, null, false)]
        [InlineData("Metal", GraphicsFormat.E5B9G9R9_UFloatPack32, TextureFormat.RGBAFloat, null, false)]
        [InlineData("Metal", GraphicsFormat.A2B10G10R10_UNormPack32, TextureFormat.RGBA32, null, false)]
        [InlineData("Metal", GraphicsFormat.A2B10G10R10_UNormPack32, TextureFormat.RGBAFloat, BlitRefusal, false)]
        [InlineData("Metal", GraphicsFormat.A1R5G5B5_UNormPack16, TextureFormat.RGBA32,
            "The texture format is not supported for Image operations (id=72)", true)]
        [InlineData("Vulkan", GraphicsFormat.B8G8R8A8_UNorm, TextureFormat.RGBAFloat,
            "ReadPixels is unsupported for B8G8R8A8_UNorm; the texture is left unchanged.", false)]
        [InlineData("Vulkan", GraphicsFormat.R8G8B8A8_UNorm, TextureFormat.RGBAFloat, null, false)]
        public void TargetReadsFollowTheDevicesRule(string device, GraphicsFormat target, TextureFormat destination, string refusal, bool asserted)
        {
            var previousDevice = GraphicsDeviceProfile.Current;
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            var capture = new Capture();
            RenderTexture rt = null;
            Texture2D texture = null;
            try
            {
                GraphicsDeviceProfile.Select(device);
                rt = new RenderTexture(2, 1, target, GraphicsFormat.None);
                texture = new Texture2D(2, 1, destination, false, true);
                var sentinel = new Color(0.1f, 0.2f, 0.3f, 0.4f);
                texture.SetPixels(new[] { sentinel, sentinel });
                var before = texture.GetPixel(0, 0);
                logger.logHandler = capture;
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
                RenderTexture.active = null;
                var refusals = capture.Logs.FindAll(l => l.Type is LogType.Error or LogType.Assert);
                if (refusal == null)
                    Assert.Empty(refusals);
                else
                {
                    var type = asserted ? LogType.Assert : LogType.Error;
                    Assert.Equal(asserted ? new[] { (type, refusal), (type, refusal) } : new[] { (type, refusal) }, refusals);
                    Assert.Equal(before, texture.GetPixel(0, 0));
                }
            }
            finally
            {
                RenderTexture.active = null;
                logger.logHandler = previousHandler;
                if (texture != null) Object.DestroyImmediate(texture);
                if (rt != null) Object.DestroyImmediate(rt);
                GraphicsDeviceProfile.Select(previousDevice.Type.ToString());
            }
        }

        /// <summary>AsyncGPUReadback of a target: Metal refuses only its unreadable formats, with
        /// their own error; elsewhere a target without ReadPixels usage fails.</summary>
        [Theory]
        [InlineData("Metal", GraphicsFormat.A1R5G5B5_UNormPack16,
            "AsyncGPUReadback - A1RGB5 UNorm (72) graphics format is not supported as source for async read back")]
        [InlineData("Metal", GraphicsFormat.B8G8R8A8_UNorm, null)]
        [InlineData("Metal", GraphicsFormat.R32G32B32A32_SInt,
            "'R32G32B32A32_SInt' doesn't support ReadPixels usage on this platform. Async GPU readback failed.")]
        [InlineData("Vulkan", GraphicsFormat.B8G8R8A8_UNorm,
            "'B8G8R8A8_UNorm' doesn't support ReadPixels usage on this platform. Async GPU readback failed.")]
        public void TargetReadbacksFollowTheDevicesRule(string device, GraphicsFormat target, string refusal)
        {
            var previousDevice = GraphicsDeviceProfile.Current;
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            var capture = new Capture();
            RenderTexture rt = null;
            try
            {
                GraphicsDeviceProfile.Select(device);
                rt = new RenderTexture(2, 1, target, GraphicsFormat.None);
                logger.logHandler = capture;
                var request = AsyncGPUReadback.Request(rt, 0);
                var refused = capture.Logs.Exists(l => l.Type == LogType.Error && l.Text == refusal);
                if (refusal == null)
                    Assert.DoesNotContain(capture.Logs, l => l.Text.Contains("graphics format is not supported as source")
                        || l.Text.Contains("doesn't support ReadPixels usage"));
                else
                {
                    Assert.True(refused, string.Join("\n", capture.Logs));
                    Assert.True(request.hasError);
                }
            }
            finally
            {
                logger.logHandler = previousHandler;
                if (rt != null) Object.DestroyImmediate(rt);
                GraphicsDeviceProfile.Select(previousDevice.Type.ToString());
            }
        }

        [Fact]
        public void UnmeasuredReportedFormatKeepsEarlierDiagnosticPolicy() =>
            SourceValidationKeepsFailureOrderAndDimensions("6000.0.71f1", TextureFormat.ARGB32, true);

    }
}
