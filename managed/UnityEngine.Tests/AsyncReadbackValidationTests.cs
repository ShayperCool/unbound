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

        [Fact]
        public void UnmeasuredReportedFormatKeepsEarlierDiagnosticPolicy() =>
            SourceValidationKeepsFailureOrderAndDimensions("6000.0.71f1", TextureFormat.ARGB32, true);

    }
}
