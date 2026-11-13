using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Native rendering plugin calls as the managed side records them (the renderer runs
    /// them: tests/projects/nativegfx), their argument checks and the native pointers without a
    /// renderer.</summary>
    public class NativePluginTests
    {
        const byte SetTargetOp = 3, ViewportOp = 5, CameraOp = 6, PluginOp = 34;
        static readonly IntPtr Callback = (IntPtr)0x7f00_1234_5678;

        /// <summary>A plugin call as recorded: its kind and fields after the callback.</summary>
        record struct Call(byte Kind, ulong Callback, ulong[] Fields);

        /// <summary>The targets set and plugin calls recorded since the last hand-over (other
        /// state commands skipped).</summary>
        static (List<ulong> Targets, List<Call> Calls) Recorded()
        {
            var bytes = GfxStream.Recorded.ToArray();
            GfxStream.Flush();
            var targets = new List<ulong>();
            var calls = new List<Call>();
            int at = 0;
            ulong U64() { var v = BitConverter.ToUInt64(bytes, at); at += 8; return v; }
            uint U32() { var v = BitConverter.ToUInt32(bytes, at); at += 4; return v; }
            while (at < bytes.Length)
            {
                byte op = bytes[at++];
                switch (op)
                {
                    case SetTargetOp:
                        targets.Add(U64());
                        at += 8 + 4 + 4;
                        break;
                    case ViewportOp:
                        at += 16;
                        break;
                    case CameraOp:
                        at += 64 + 64 + 4;
                        break;
                    case PluginOp:
                        byte kind = bytes[at++];
                        ulong callback = U64();
                        ulong[] fields = kind switch
                        {
                            0 => new ulong[] { U32() },
                            1 => new ulong[] { U32(), U64() },
                            2 or 3 => new ulong[] { U64(), U32(), U32(), U32() },
                            4 => new ulong[] { U32(), U32(), U64(), U32(), U32() },
                            _ => throw new InvalidOperationException($"plugin call kind {kind}"),
                        };
                        calls.Add(new Call(kind, callback, fields));
                        break;
                    default:
                        throw new InvalidOperationException($"unexpected opcode {op}");
                }
            }
            return (targets, calls);
        }

        static RenderTexture Target(int depth = 0)
        {
            var rt = new RenderTexture(4, 4, depth, RenderTextureFormat.ARGB32);
            rt.Create();
            return rt;
        }

        sealed class ErrorCapture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Inner = Debug.unityLogger.logHandler;
            internal readonly List<string> Errors = new List<string>();
            internal readonly List<string> Warnings = new List<string>();

            internal ErrorCapture() => Debug.unityLogger.logHandler = this;

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = args == null || args.Length == 0 ? format : string.Format(format, args);
                if (type == LogType.Error) Errors.Add(message);
                if (type == LogType.Warning) Warnings.Add(message);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);

            public void Dispose() => Debug.unityLogger.logHandler = m_Inner;
        }

        [Fact]
        public void NullCallbacksAreRefusedWhenIssued()
        {
            var gl = Assert.Throws<ArgumentException>(() => GL.IssuePluginEvent(IntPtr.Zero, 1));
            Assert.Equal("callback", gl.ParamName);
            Assert.StartsWith("Null callback specified.", gl.Message);
            using var commands = new CommandBuffer();
            Assert.Equal("Null callback specified.", Assert.Throws<ArgumentException>(() => commands.IssuePluginEvent(IntPtr.Zero, 1)).Message);
            Assert.Equal("Null callback specified.", Assert.Throws<ArgumentException>(() => commands.IssuePluginEventAndData(IntPtr.Zero, 1, IntPtr.Zero)).Message);
            Assert.Equal(0, commands.CommandCount);
        }

        [Fact]
        public void GlEventsFindTheActiveRenderTextureBound()
        {
            var rt = Target();
            var previous = RenderTexture.active;
            try
            {
                GfxStream.Flush();
                RenderTexture.active = rt;
                GfxStream.SetTarget(0);
                GfxStream.Flush();
                GL.IssuePluginEvent(Callback, 12);
                var (targets, calls) = Recorded();
                // The stream's target was the screen: the active texture is bound before the call.
                Assert.Equal(new[] { rt.EnsureCreated() }, targets);
                var call = Assert.Single(calls);
                Assert.Equal((byte)0, call.Kind);
                Assert.Equal((ulong)Callback, call.Callback);
                Assert.Equal(12UL, call.Fields[0]);
                // Already bound: nothing is set again.
                GL.IssuePluginEvent(Callback, 13);
                (targets, calls) = Recorded();
                Assert.Empty(targets);
                Assert.Equal(13UL, Assert.Single(calls).Fields[0]);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(rt);
            }
        }

        [Fact]
        public void ExecutedBuffersCallInOrderWithTheirData()
        {
            var rt = Target();
            var previous = RenderTexture.active;
            try
            {
                using var commands = new CommandBuffer();
                commands.IssuePluginEvent(Callback, 20);
                commands.IssuePluginEventAndData(Callback, 21, (IntPtr)0x1234);
                commands.IssuePluginEventAndDataWithFlags(Callback, 22, CustomMarkerCallbackFlags.CustomMarkerCallbackForceInvalidateStateTracking, (IntPtr)0x5678);
                RenderTexture.active = rt;
                GfxStream.SetTarget(0);
                GfxStream.Flush();
                Graphics.ExecuteCommandBuffer(commands);
                var (targets, calls) = Recorded();
                Assert.Equal(new[] { rt.EnsureCreated() }, targets);
                Assert.Equal(new[] { (byte)0, (byte)1, (byte)1 }, calls.ConvertAll(c => c.Kind));
                Assert.Equal(new[] { 21UL, 0x1234UL }, calls[1].Fields);
                Assert.Equal(new[] { 22UL, 0x5678UL }, calls[2].Fields);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(rt);
            }
        }

        [Fact]
        public void TextureUpdatesPassTheTexturesFormat()
        {
            var linear = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var srgb = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            var rgb = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
            var half = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true);
            var previousSpace = QualitySettings.activeColorSpace;
            try
            {
                using var commands = new CommandBuffer();
#pragma warning disable 618
                commands.IssuePluginCustomTextureUpdate(Callback, linear, 11);
                commands.IssuePluginCustomTextureUpdateV1(Callback, linear, 12);
#pragma warning restore 618
                commands.IssuePluginCustomTextureUpdateV2(Callback, srgb, 13);
                commands.IssuePluginCustomTextureUpdateV2(Callback, rgb, 14);
                // Measured for these formats only (tests/projects/nativegfx update.*).
                Assert.Throws<NotSupportedException>(() => commands.IssuePluginCustomTextureUpdateV2(Callback, half, 15));
                Assert.Throws<NotSupportedException>(() => commands.IssuePluginCustomTextureUpdateV2(Callback, Target(), 16));
                GfxStream.Flush();
                Graphics.ExecuteCommandBuffer(commands);
                var (_, calls) = Recorded();
                // (kind: 2 V1, 3 V2; texture, user data, format, bytes per pixel)
                Assert.Equal(new[] { (byte)2, (byte)2, (byte)3, (byte)3 }, calls.ConvertAll(c => c.Kind));
                Assert.Equal(new[] { linear.EnsureUploaded(), 11UL, 8UL, 4UL }, calls[0].Fields);
                Assert.Equal(new[] { linear.EnsureUploaded(), 12UL, 8UL, 4UL }, calls[1].Fields);
                // sRGB texels are an sRGB format in a Linear project only (as the device stores them).
                Assert.Equal(new[] { srgb.EnsureUploaded(), 13UL, previousSpace == ColorSpace.Linear ? 4UL : 8UL, 4UL }, calls[2].Fields);
                Assert.Equal(new[] { rgb.EnsureUploaded(), 14UL, 7UL, 3UL }, calls[3].Fields);
            }
            finally
            {
                foreach (var t in new Object[] { linear, srgb, rgb, half }) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void CustomBlitsBindTheirDestinationAndRefuseTextureSources()
        {
            var source = Target();
            var destination = Target();
            var texture = new Texture2D(2, 2);
            try
            {
                using var commands = new CommandBuffer();
                commands.IssuePluginCustomBlit(Callback, 3, source, destination, 77, 5);
                commands.IssuePluginCustomBlit(Callback, 4, texture, destination, 78, 6);
                GfxStream.SetTarget(0);
                GfxStream.Flush();
                using var errors = new ErrorCapture();
                Graphics.ExecuteCommandBuffer(commands);
                var (targets, calls) = Recorded();
                ulong buffer = (ulong)destination.colorBuffer.GetNativeRenderBufferPtr();
                // The render texture source: the destination bound for the call, then the target before.
                Assert.Equal(destination.EnsureCreated(), targets[0]);
                Assert.Equal(new[] { 3UL, (ulong)(uint)source.EnsureCreated(), buffer, 77UL, 5UL }, calls[0].Fields);
                // A Texture2D source: an error, source 0, the destination's buffer, nothing bound for it.
                Assert.Equal(new[] { 4UL, 0UL, buffer, 78UL, 6UL }, calls[1].Fields);
                Assert.DoesNotContain(destination.EnsureCreated(), targets.GetRange(1, targets.Count - 1));
                Assert.Equal(new[] { "PPtr cast failed when dereferencing! Casting from Texture2D to RenderTexture!" }, errors.Errors);
            }
            finally
            {
                foreach (var t in new Object[] { source, destination, texture }) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void NativePointersOfTexturesThatAreNotShared()
        {
            var uncreated = new RenderTexture(4, 4, 24);
            var withDepth = Target(24);
            var withoutDepth = Target();
            try
            {
                // Not created: zero, and the call does not create it.
                Assert.Equal(IntPtr.Zero, uncreated.GetNativeTexturePtr());
                Assert.Equal(IntPtr.Zero, uncreated.GetNativeDepthBufferPtr());
                Assert.False(uncreated.IsCreated());
                // Without a renderer to share them: stable, distinct values above any OpenGL name.
                var color = withDepth.GetNativeTexturePtr();
                var depth = withDepth.GetNativeDepthBufferPtr();
                Assert.Equal(color, withDepth.GetNativeTexturePtr());
                Assert.NotEqual(color, depth);
                Assert.True((ulong)color > uint.MaxValue && (ulong)depth > uint.MaxValue);
                // A texture without depth gives its color texture.
                Assert.Equal(withoutDepth.GetNativeTexturePtr(), withoutDepth.GetNativeDepthBufferPtr());
                // Render buffers: per texture and kind, never a texture's pointer.
                var colorBuffer = withDepth.colorBuffer.GetNativeRenderBufferPtr();
                var depthBuffer = withDepth.depthBuffer.GetNativeRenderBufferPtr();
                Assert.Equal(colorBuffer, withDepth.colorBuffer.GetNativeRenderBufferPtr());
                Assert.NotEqual(colorBuffer, depthBuffer);
                Assert.NotEqual(colorBuffer, withoutDepth.colorBuffer.GetNativeRenderBufferPtr());
                Assert.DoesNotContain(colorBuffer, new[] { color, depth });
                Assert.DoesNotContain(depthBuffer, new[] { color, depth });
            }
            finally
            {
                foreach (var t in new Object[] { uncreated, withDepth, withoutDepth }) Object.DestroyImmediate(t);
            }
        }

        [Fact]
        public void MultisampledBindingRequiresSamplesAndDoesNotWarnWhenPointersAreRequested()
        {
            var previousVersion = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var resolved = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var single = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { bindTextureMS = true };
            var bound = new RenderTexture(4, 4, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, bindTextureMS = true };
            try
            {
                Assert.True(resolved.Create());
                Assert.False(single.Create());
                Assert.False(single.IsCreated());
                Assert.Equal(IntPtr.Zero, single.GetNativeTexturePtr());
                Assert.Equal(IntPtr.Zero, single.GetNativeDepthBufferPtr());
                Assert.True(bound.Create());
                using var log = new ErrorCapture();
                resolved.GetNativeTexturePtr(); resolved.GetNativeDepthBufferPtr();
                bound.GetNativeTexturePtr(); bound.GetNativeDepthBufferPtr();
                Assert.DoesNotContain(log.Warnings, warning => warning.Contains("bindTextureMS"));
            }
            finally
            {
                foreach (var t in new Object[] { resolved, single, bound }) Object.DestroyImmediate(t);
                ProjectInfo.UnityVersion = previousVersion;
            }
        }

        [Fact]
        public void ExternalTexturesKeepTheNativeTextureTheyShow()
        {
            Assert.Equal("nativeTex can not be null",
                Assert.Throws<ArgumentException>(() => Texture2D.CreateExternalTexture(4, 4, TextureFormat.RGBA32, false, false, IntPtr.Zero)).Message);
            var external = Texture2D.CreateExternalTexture(4, 2, TextureFormat.RGBA32, false, true, (IntPtr)77);
            try
            {
                Assert.Equal((IntPtr)77, external.GetNativeTexturePtr());
                Assert.Equal((4, 2, TextureFormat.RGBA32, 1), (external.width, external.height, external.format, external.mipmapCount));
                external.UpdateExternalTexture((IntPtr)78);
                Assert.Equal((IntPtr)78, external.GetNativeTexturePtr());
            }
            finally
            {
                Object.DestroyImmediate(external);
            }
        }
    }
}
