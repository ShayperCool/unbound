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
    /// logged errors); on the other devices (unmeasured) the profile refuses none, Crunched
    /// volumes aside.</summary>
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

        /// <summary>The compressed TextureFormats are those the Editor's IsCompressedFormat answers
        /// for every TextureFormat value (the reference's compressed.formats).</summary>
        [Fact]
        public void CompressedTextureFormatsAreTheEditors()
        {
            var recorded = File.ReadLines(Reference())
                .Select(line => JsonDocument.Parse(line).RootElement)
                .Single(row => row.GetProperty("k").GetString() == "compressed.formats")
                .GetProperty("v").EnumerateArray().Select(v => int.Parse(v.GetString())).ToList();
            var compressed = Enum.GetValues<TextureFormat>().Distinct()
                .Where(f => UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(f))
                .Select(f => (int)f).OrderBy(f => f).ToList();
            Assert.Equal(recorded, compressed);
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
                // A control outside the recorded device: a format Metal refuses is made there (the
                // profile refuses none: unmeasured), Crunched volumes are refused alike.
                foreach (var device in new[] { "OpenGLCore", "Vulkan" })
                {
                    GraphicsDeviceProfile.Select(device);
                    Assert.Equal(new[] { "made" }, Make(TextureFormat.ETC2_RGB));
                    Assert.Equal(new[] { "ArgumentException: Crunched Texture3D is not supported." }, Make(TextureFormat.DXT1Crunched));
                }
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
        }
    }
}
