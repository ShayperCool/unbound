using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Making a Texture3D of each compressed format, as the macOS Editor's Metal device
    /// answers (tests/projects/nativegfx metal.jsonl volume.make.*: made, or the exception and the
    /// logged errors), and the other devices refusing only Crunched volumes.</summary>
    public class Texture3DDeviceFormatTests
    {
        sealed class Capture : ILogHandler
        {
            public readonly List<string> Logs = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Logs.Add(logType + ": " + string.Format(format, args));
            public void LogException(Exception exception, Object context) => Logs.Add("Exception: " + exception.Message);
        }

        static string Reference([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "tests", "projects", "nativegfx",
                "Reference", "unity-6000.0.71f1", "metal.jsonl"));

        static List<string> Make(TextureFormat format)
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture();
            logger.logHandler = capture;
            string result;
            try
            {
                Object.DestroyImmediate(new Texture3D(4, 4, 2, format, false));
                result = "made";
            }
            catch (Exception e)
            {
                result = e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                logger.logHandler = previous;
            }
            return new[] { result }.Concat(capture.Logs).ToList();
        }

        [Fact]
        public void VolumesOfCompressedFormatsAsTheDeviceMakesThem()
        {
            var recorded = new List<(TextureFormat, List<string>)>();
            foreach (var line in File.ReadLines(Reference()))
            {
                using var row = JsonDocument.Parse(line);
                var key = row.RootElement.GetProperty("k").GetString();
                if (!key.StartsWith("volume.make.", StringComparison.Ordinal)) continue;
                recorded.Add((Enum.Parse<TextureFormat>(key.Substring("volume.make.".Length)),
                    row.RootElement.GetProperty("v").EnumerateArray().Select(v => v.GetString()).ToList()));
            }
            Assert.Equal(34, recorded.Count);
            var previous = GraphicsDeviceProfile.Current;
            try
            {
                GraphicsDeviceProfile.Select("Metal");
                foreach (var (format, expected) in recorded)
                    Assert.Equal(expected, Make(format));
                // A control outside the recorded device: only Crunched volumes are refused.
                foreach (var device in new[] { "OpenGLCore", "Vulkan" })
                {
                    GraphicsDeviceProfile.Select(device);
                    foreach (var (format, _) in recorded)
                    {
                        var made = Make(format);
                        if (UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCrunchFormat(format))
                            Assert.Equal(new[] { "ArgumentException: Crunched Texture3D is not supported." }, made);
                        else
                            Assert.Equal(new[] { "made" }, made);
                    }
                }
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }
    }
}
