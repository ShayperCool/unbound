using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>RenderTextureDescriptor.colorFormat's setter: the format in the descriptor's own
    /// encoding, as the device renders it; a Shadowmap compares, any other format does not.</summary>
    public class RenderTextureDescriptorColorFormatTests
    {
        static GraphicsFormat Rendered(GraphicsFormat format) => SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render);

        [Fact]
        public void ADescriptorKeepsItsOwnEncodingWhateverTheColorSpace()
        {
            var space = QualitySettings.activeColorSpace;
            try
            {
                foreach (var colorSpace in new[] { ColorSpace.Linear, ColorSpace.Gamma })
                {
                    QualitySettings.s_ColorSpace = colorSpace;
                    var linear = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm, 0);
                    linear.colorFormat = RenderTextureFormat.ARGBHalf;
                    linear.colorFormat = RenderTextureFormat.ARGB32;
                    Assert.Equal(Rendered(GraphicsFormat.R8G8B8A8_UNorm), linear.graphicsFormat);
                    Assert.False(linear.sRGB);
                    var srgb = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_SRGB, 0);
                    srgb.colorFormat = RenderTextureFormat.ARGB32;
                    Assert.Equal(Rendered(GraphicsFormat.R8G8B8A8_SRGB), srgb.graphicsFormat);
                }
            }
            finally { QualitySettings.s_ColorSpace = space; }
        }

        /// <summary>The descriptor takes a format the device cannot render as the one it renders
        /// instead (BGRA32 where B8G8R8A8 is not renderable); a render texture's format keeps it.</summary>
        [Fact]
        public void TheDescriptorTakesTheRenderedFormatAndTheTextureItsOwn()
        {
            var d = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm, 0);
            d.colorFormat = RenderTextureFormat.BGRA32;
            Assert.Equal(Rendered(GraphicsFormat.B8G8R8A8_UNorm), d.graphicsFormat);
            var texture = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                texture.format = RenderTextureFormat.BGRA32;
                Assert.Equal(GraphicsFormat.B8G8R8A8_UNorm, texture.graphicsFormat);
                // A Shadowmap compares, and a color format set after it does not turn that off.
                texture.depth = 24;
                texture.format = RenderTextureFormat.Shadowmap;
                Assert.Equal(ShadowSamplingMode.CompareDepths, texture.descriptor.shadowSamplingMode);
                texture.format = RenderTextureFormat.ARGB32;
                Assert.Equal(ShadowSamplingMode.CompareDepths, texture.descriptor.shadowSamplingMode);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<(LogType Type, string Text)> Logs = new List<(LogType, string)>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Logs.Add((type, string.Format(format, args)));
            public void LogException(Exception exception, Object context) => Logs.Add((LogType.Exception, exception.Message));
        }

        /// <summary>A depth-only format set on a texture without a depth buffer gives it 16 bits,
        /// with Unity's warning; one with a depth buffer keeps it, without a warning.</summary>
        [Theory]
        [InlineData(RenderTextureFormat.Depth)]
        [InlineData(RenderTextureFormat.Shadowmap)]
        public void ADepthFormatWithoutADepthBufferGetsSixteenBits(RenderTextureFormat format)
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            var bare = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
            var deep = new RenderTexture(4, 4, 24, RenderTextureFormat.ARGB32);
            try
            {
                var depthOfDeep = deep.depthStencilFormat;
                Debug.unityLogger.logHandler = capture;
                bare.format = format;
                Assert.Equal(new[] { (LogType.Warning, $"{format} RenderTexture requested without a depth buffer. Changing to a 16 bit depth buffer. To resolve this warning, please specify the desired number of depth bits when creating the render texture.") }, capture.Logs);
                Assert.Equal(GraphicsFormat.D16_UNorm, bare.depthStencilFormat);
                Assert.Equal(format == RenderTextureFormat.Shadowmap ? ShadowSamplingMode.CompareDepths : ShadowSamplingMode.None, bare.descriptor.shadowSamplingMode);
                capture.Logs.Clear();
                deep.format = format;
                Assert.Empty(capture.Logs);
                Assert.Equal(depthOfDeep, deep.depthStencilFormat);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(bare);
                Object.DestroyImmediate(deep);
            }
        }

        [Fact]
        public void OnlyAShadowmapCompares()
        {
            var d = new RenderTextureDescriptor(4, 4, GraphicsFormat.R8G8B8A8_UNorm, 24);
            d.colorFormat = RenderTextureFormat.Shadowmap;
            Assert.Equal(ShadowSamplingMode.CompareDepths, d.shadowSamplingMode);
            d.colorFormat = RenderTextureFormat.ARGB32;
            Assert.Equal(ShadowSamplingMode.None, d.shadowSamplingMode);
            d.colorFormat = RenderTextureFormat.Depth;
            Assert.Equal(ShadowSamplingMode.None, d.shadowSamplingMode);
        }
    }
}
