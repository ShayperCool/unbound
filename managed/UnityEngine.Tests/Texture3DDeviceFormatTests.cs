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

        /// <summary>Block-compressed volumes and arrays keep their blocks: SetPixelData takes a
        /// level's blocks (a volume level's depth slices one after another) and GetPixelData gives
        /// them back; GetPixels decodes them (the Scripting API: GetPixels decompresses compressed
        /// textures; DXT here, the natively decoded formats in tests/projects/nativegfx
        /// volume.*.cpu); texel writes fail as Texture2D's do.</summary>
        [Fact]
        public void CompressedLayeredTexturesKeepTheirBlocks()
        {
            var red = new byte[] { 0x00, 0xF8, 0x00, 0xF8, 0, 0, 0, 0 };
            var blue = new byte[] { 0x1F, 0x00, 0x1F, 0x00, 0, 0, 0, 0 };
            var volume = new Texture3D(4, 4, 2, TextureFormat.DXT1, true) { name = "dxt1 volume" };
            var array = new Texture2DArray(8, 8, 2, TextureFormat.BC7, false, true);
            // ASTC 6x6 over 7x5 texels: 2x1 blocks of 16 bytes per slice (its decoding is native:
            // tests/projects/nativegfx volume.*.cpu).
            var astc = new Texture3D(7, 5, 1, TextureFormat.ASTC_6x6, false);
            try
            {
                var blocks = red.Concat(blue).ToArray();
                volume.SetPixelData(blocks, 0);
                Assert.Equal(blocks, volume.GetPixelData<byte>(0).ToArray());
                // Level 1 (2x2x1): one block.
                Assert.Equal(8, volume.GetPixelData<byte>(1).Length);
                volume.Apply(true);
                Assert.Equal(blocks, volume.GetPixelData<byte>(0).ToArray());
                var texels = volume.GetPixels32(0);
                Assert.Equal(32, texels.Length);
                Assert.All(texels.Take(16), c => Assert.Equal(new Color32(255, 0, 0, 255), c));
                Assert.All(texels.Skip(16), c => Assert.Equal(new Color32(0, 0, 255, 255), c));
                Assert.Equal(new Color(0f, 0f, 1f, 1f), volume.GetPixel(1, 2, 1, 0));
                var write = Assert.Throws<ArgumentException>(() => volume.SetPixels32(new Color32[32], 0));
                Assert.Equal("Texture3D.SetPixels32: texture uses an unsupported format. (Texture 'dxt1 volume')", write.Message);
                // BC7 8x8: four 16-byte blocks per slice.
                var slice = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
                array.SetPixelData(slice, 0, 1);
                Assert.Equal(slice, array.GetPixelData<byte>(0, 1).ToArray());
                array.Apply(false);
                Assert.Equal(32, astc.GetPixelData<byte>(0).Length);
            }
            finally
            {
                Object.DestroyImmediate(volume);
                Object.DestroyImmediate(array);
                Object.DestroyImmediate(astc);
            }
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
