using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary><c>CommandBuffer.CopyTexture</c> never copies the screen: an end naming it (the
    /// camera target outside cameras or of a screen camera, the texture standing for the screen)
    /// logs the Editor's error and records no copy (tests/projects/nativegfx metal.jsonl
    /// screencopy.*, screencamera.*); copies between render textures are recorded.</summary>
    public class CommandBufferScreenCopyTests
    {
        sealed class Capture : ILogHandler
        {
            public readonly List<string> Errors = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                if (logType == LogType.Error) Errors.Add(string.Format(format, args));
            }
            public void LogException(Exception exception, Object context) => Errors.Add(exception.Message);
        }

        static byte[] CopyOf(ulong source) =>
            new byte[] { GfxStream.CopyTextureOp }.Concat(BitConverter.GetBytes(source)).ToArray();

        static int Occurrences(byte[] bytes, byte[] part)
        {
            int n = 0;
            for (int at = 0; at + part.Length <= bytes.Length; at++)
                if (bytes.AsSpan(at, part.Length).SequenceEqual(part)) n++;
            return n;
        }

        static (List<string> errors, byte[] recorded) Execute(CommandBuffer cmd)
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture();
            logger.logHandler = capture;
            try
            {
                GfxStream.Flush();
                Graphics.ExecuteCommandBuffer(cmd);
                var recorded = GfxStream.Recorded.ToArray();
                GfxStream.Flush();
                return (capture.Errors, recorded);
            }
            finally
            {
                logger.logHandler = previous;
            }
        }

        [Fact]
        public void TheScreenIsNoTextureToCopy()
        {
            var a = new RenderTexture(8, 8, 0);
            var b = new RenderTexture(8, 8, 0);
            var standIn = new RenderTexture(8, 8, 0);
            var active = RenderTexture.ActiveTarget;
            try
            {
                ulong ha = a.EnsureCreated(), hb = b.EnsureCreated();
                using (var cmd = new CommandBuffer())
                {
                    cmd.CopyTexture(BuiltinRenderTextureType.CameraTarget, 0, 0, 0, 0, 8, 8, a, 0, 0, 0, 0);
                    cmd.CopyTexture(b, 0, 0, 0, 0, 4, 4, BuiltinRenderTextureType.CameraTarget, 0, 0, 2, 2);
                    // A control: render textures are copied.
                    cmd.CopyTexture(b, a);
                    var (errors, recorded) = Execute(cmd);
                    Assert.Equal(new[]
                    {
                        "Graphics.CopyTexture called with null source texture",
                        "Graphics.CopyTexture called with null destination texture",
                    }, errors);
                    Assert.Equal(0, Occurrences(recorded, CopyOf(0)));
                    Assert.Equal(1, Occurrences(recorded, CopyOf(hb)));
                }
                // The texture standing for the screen, the active target: no texture either.
                RenderTexture.ScreenStandIn = standIn;
                RenderTexture.SetActiveQuietly(standIn);
                ulong hs = standIn.EnsureCreated();
                using (var cmd = new CommandBuffer())
                {
                    cmd.CopyTexture(BuiltinRenderTextureType.CurrentActive, a);
                    cmd.CopyTexture(a, BuiltinRenderTextureType.CurrentActive);
                    var (errors, recorded) = Execute(cmd);
                    Assert.Equal(new[]
                    {
                        "Graphics.CopyTexture called with null source texture",
                        "Graphics.CopyTexture called with null destination texture",
                    }, errors);
                    Assert.Equal(0, Occurrences(recorded, CopyOf(hs)));
                    Assert.Equal(0, Occurrences(recorded, CopyOf(ha)));
                }
            }
            finally
            {
                RenderTexture.SetActiveQuietly(active);
                RenderTexture.ScreenStandIn = null;
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(standIn);
            }
        }
    }
}
