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
    /// <summary>A render texture's creation flags and the resolve of a multisampled texture into
    /// another (Unity 6000.0.71f1: tests/projects/particletrailwitness `target.flags`,
    /// tests/projects/cameramsaa resolves).</summary>
    public class RenderTextureStateTests
    {
        const RenderTextureCreationFlags New = RenderTextureCreationFlags.AutoGenerateMips | RenderTextureCreationFlags.AllowVerticalFlip;

        /// <summary>Unity's own descriptor takes an sRGB format from <c>sRGB = true</c> only in a
        /// Linear project; the flag follows the format.</summary>
        [Theory]
        [InlineData(ColorSpace.Linear, true)]
        [InlineData(ColorSpace.Gamma, false)]
        public void TheSrgbSwitchFollowsTheColorSpace(ColorSpace space, bool srgb)
        {
            var previous = QualitySettings.s_ColorSpace;
            QualitySettings.s_ColorSpace = space;
            try
            {
                var d = new RenderTextureDescriptor(4, 2, GraphicsFormat.R8G8B8A8_UNorm, 0) { sRGB = true };
                Assert.Equal(srgb, d.sRGB);
                Assert.Equal(srgb ? New | RenderTextureCreationFlags.SRGB : New, d.flags);
                // A format given outright is kept in both.
                d.graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB;
                Assert.Equal(New | RenderTextureCreationFlags.SRGB, d.flags);
            }
            finally { QualitySettings.s_ColorSpace = previous; }
        }

        [Fact]
        public void ADescriptorsSrgbFlagFollowsItsColorFormat()
        {
            var previous = QualitySettings.s_ColorSpace;
            QualitySettings.s_ColorSpace = ColorSpace.Linear;
            try { FlagFollowsFormat(); }
            finally { QualitySettings.s_ColorSpace = previous; }
        }

        static void FlagFollowsFormat()
        {
            var d = new RenderTextureDescriptor(4, 2, GraphicsFormat.R8G8B8A8_SRGB, 0);
            Assert.Equal(New | RenderTextureCreationFlags.SRGB, d.flags);
            d.sRGB = false;
            Assert.Equal(GraphicsFormat.R8G8B8A8_UNorm, d.graphicsFormat);
            Assert.Equal(New, d.flags);
            d.graphicsFormat = GraphicsFormat.B8G8R8A8_SRGB;
            Assert.Equal(New | RenderTextureCreationFlags.SRGB, d.flags);
            d.colorFormat = RenderTextureFormat.ARGBHalf;
            Assert.Equal(New, d.flags);
            // The other flags are their own switches.
            d.useMipMap = true;
            d.autoGenerateMips = false;
            d.enableRandomWrite = true;
            d.sRGB = true;
            Assert.Equal(RenderTextureCreationFlags.MipMap | RenderTextureCreationFlags.EnableRandomWrite | RenderTextureCreationFlags.AllowVerticalFlip, d.flags);
            Assert.Equal(New, new RenderTextureDescriptor(4, 2).flags);
        }

        [Fact]
        public void ATextureMadeByAScriptSaysSoInItsDescriptor()
        {
            var srgb = new RenderTexture(4, 2, 0, GraphicsFormat.R8G8B8A8_SRGB);
            var linear = new RenderTexture(4, 2, 0, GraphicsFormat.R8G8B8A8_UNorm);
            var copy = new RenderTexture(srgb);
            var custom = new CustomRenderTexture(4, 2, GraphicsFormat.R8G8B8A8_UNorm);
            var temporary = RenderTexture.GetTemporary(4, 2, 0, GraphicsFormat.R8G8B8A8_UNorm);
            try
            {
                // 166, the canonical value of a default `new RenderTexture` in a Linear project.
                Assert.Equal(New | RenderTextureCreationFlags.SRGB | RenderTextureCreationFlags.CreatedFromScript, srgb.descriptor.flags);
                Assert.Equal(166, (int)srgb.descriptor.flags);
                Assert.Equal(New | RenderTextureCreationFlags.CreatedFromScript, linear.descriptor.flags);
                Assert.Equal(srgb.descriptor.flags, copy.descriptor.flags);
                Assert.Equal(New | RenderTextureCreationFlags.CreatedFromScript, custom.descriptor.flags);
                // Unity's own GetTemporary marks its descriptor before asking for the texture.
                Assert.Equal(New | RenderTextureCreationFlags.CreatedFromScript, temporary.descriptor.flags);
                // A format change moves the sRGB flag only.
                linear.graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB;
                Assert.Equal(srgb.descriptor.flags, linear.descriptor.flags);
                // A clone is marked as its source is.
                var clone = Object.Instantiate(linear);
                Assert.Equal(linear.descriptor.flags, clone.descriptor.flags);
                Object.DestroyImmediate(clone);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(temporary);
                foreach (var t in new[] { srgb, linear, copy, custom }) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void ATextureLoadedFromAnAssetIsNotMadeByAScript()
        {
            var body = UnityEngine.Tests.TestYaml.DocumentBody("RenderTexture:\n  m_Width: 8\n  m_Height: 4\n  m_ColorFormat: 8\n  m_SRGB: 1\n  m_MipMap: 1\n  m_GenerateMips: 1\n");
            var texture = (RenderTexture)Activator.CreateInstance(typeof(RenderTexture), nonPublic: true);
            try
            {
                ((ISerializedAsset)texture).ReadSerialized(body, null);
                var flags = texture.descriptor.flags;
                Assert.Equal((RenderTextureCreationFlags)0, flags & RenderTextureCreationFlags.CreatedFromScript);
                Assert.Equal(texture.sRGB, (flags & RenderTextureCreationFlags.SRGB) != 0);
                Assert.NotEqual((RenderTextureCreationFlags)0, flags & RenderTextureCreationFlags.MipMap);
                var clone = Object.Instantiate(texture);
                Assert.Equal(flags, clone.descriptor.flags);
                Object.DestroyImmediate(clone);
                // A descriptor given by a script marks the texture (Unity's own setter).
                texture.descriptor = new RenderTextureDescriptor(4, 2, GraphicsFormat.R8G8B8A8_UNorm, 0);
                Assert.Equal(New | RenderTextureCreationFlags.CreatedFromScript, texture.descriptor.flags);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<(LogType Type, string Text)> Logs = new List<(LogType, string)>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Logs.Add((type, string.Format(format, args)));
            public void LogException(Exception exception, Object context) => Logs.Add((LogType.Exception, exception.Message));
        }

        const string Refusal = "Assertion failed on expression: 'srcAttach < m_CurrentFramebuffer.colorCount && \"We should always resolve only current RT\"'";

        /// <summary>Whether a resolve of <paramref name="source"/> into
        /// <paramref name="target"/> reaches the command stream, the bytes it writes and what
        /// it logs.</summary>
        static (bool Sent, int Bytes, List<(LogType Type, string Text)> Logs) Resolve(RenderTexture source, RenderTexture target)
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                GfxStream.Flush();
                bool sent = source.WriteResolve(target);
                int bytes = GfxStream.Recorded.Length;
                GfxStream.Flush();
                return (sent, bytes, capture.Logs);
            }
            finally { Debug.unityLogger.logHandler = previous; }
        }

        sealed class Scene : IDisposable
        {
            internal readonly RenderTexture Source = new RenderTexture(8, 8, 24, GraphicsFormat.R8G8B8A8_UNorm) { antiAliasing = 4 };
            internal readonly RenderTexture Target = new RenderTexture(8, 8, 0, GraphicsFormat.R8G8B8A8_UNorm);
            internal readonly RenderTexture Other = new RenderTexture(8, 8, 0, GraphicsFormat.R8G8B8A8_UNorm);
            readonly RenderTexture m_Active = RenderTexture.active;
            readonly GraphicsDeviceProfile m_Device = GraphicsDeviceProfile.Current;
            readonly string m_Release = ProjectInfo.UnityVersion;

            internal Scene(string device)
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                GraphicsDeviceProfile.Select(device);
                Source.Create();
                Target.Create();
                Other.Create();
            }

            public void Dispose()
            {
                RenderTexture.active = m_Active;
                GraphicsDeviceProfile.Select(m_Device.Type.ToString());
                ProjectInfo.UnityVersion = m_Release;
                foreach (var t in new[] { Source, Target, Other }) Object.DestroyImmediate(t);
                GfxStream.Flush();
            }
        }

        /// <summary>On the OpenGL Core device a source resolved while no render target is active
        /// logs the Editor's assertion once per call (measured); Unbound's choice is the same
        /// for another render target and for the destination being active. The resolve is sent
        /// either way. The active source resolves silently.</summary>
        [Fact]
        public void OpenGLAssertsOnAResolveOfAnInactiveSource()
        {
            using var scene = new Scene("OpenGLCore");
            RenderTexture.active = scene.Source;
            var active = Resolve(scene.Source, scene.Target);
            Assert.True(active.Sent);
            Assert.True(active.Bytes > 0);
            Assert.Empty(active.Logs);
            foreach (var other in new[] { null, scene.Other, scene.Target })
            {
                RenderTexture.active = other;
                var inactive = Resolve(scene.Source, scene.Target);
                Assert.True(inactive.Sent);
                Assert.Equal(active.Bytes, inactive.Bytes);
                Assert.Equal(new[] { (LogType.Assert, Refusal) }, inactive.Logs);
            }
        }

        /// <summary>The Vulkan device is not measured: no assertion there. An uncreated
        /// destination is left alone on both devices, silently.</summary>
        [Theory]
        [InlineData("Vulkan", 0)]
        [InlineData("OpenGLCore", 1)]
        public void TheAssertionIsTheOpenGLDevices(string device, int assertions)
        {
            using var scene = new Scene(device);
            RenderTexture.active = null;
            var inactive = Resolve(scene.Source, scene.Target);
            Assert.True(inactive.Sent);
            Assert.True(inactive.Bytes > 0);
            Assert.Equal(assertions, inactive.Logs.Count);
            scene.Target.Release();
            var uncreated = Resolve(scene.Source, scene.Target);
            Assert.False(uncreated.Sent);
            Assert.Equal(0, uncreated.Bytes);
            Assert.Empty(uncreated.Logs);
        }
    }
}
