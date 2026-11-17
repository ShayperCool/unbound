using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The argument errors of the layered textures' pixel APIs and of Graphics.CopyTexture
    /// (texts measured in tests/projects/apierrors `texture.*`), and what they leave untouched.</summary>
    public sealed class TextureArgumentErrorsTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;

        public TextureArgumentErrorsTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
        }

        public void Dispose() => m_Logger.logHandler = m_PreviousHandler;

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;

            internal CaptureHandler(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                m_Logs.Add(type + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        static Color[] Fill(int count, Color color)
        {
            var colors = new Color[count];
            for (int i = 0; i < count; i++) colors[i] = color;
            return colors;
        }

        static string MessageOf(Action action) => Assert.Throws<ArgumentException>(action).Message;

        [Fact]
        public void LayeredSetPixelsRefuseMissingAndShortArraysAndWriteTheStartOfLongerOnes()
        {
            var array = new Texture2DArray(4, 4, 2, TextureFormat.RGBA32, false) { name = "Layers" };
            Assert.Equal("Texture2DArray.SetPixels: one of the arguments is not valid. (Texture 'Layers')", MessageOf(() => array.SetPixels(null, 0)));
            Assert.Equal("Texture2DArray.SetPixels: size of data to be filled was larger than the size of data available in the source array. (Texture 'Layers')",
                MessageOf(() => array.SetPixels(new Color[15], 0)));
            var longer = Fill(20, Color.red);
            longer[16] = Color.green;
            array.SetPixels(longer, 1);
            Assert.All(array.GetPixels(1), c => Assert.Equal(Color.red, c));
            Assert.All(array.GetPixels(0), c => Assert.Equal(new Color(0, 0, 0, 0), c));
            Assert.Equal("Texture2DArray.SetPixels32: the size of data to be written is outside the target buffer bounds. (Texture 'Layers')",
                MessageOf(() => array.SetPixels32(new Color32[17], 0)));
            Assert.Equal("Texture2DArray.GetPixels: invalid array element. (Texture 'Layers')", MessageOf(() => array.GetPixels(2)));
            Assert.Equal("Texture2DArray.GetPixels: invalid mipmap level. (Texture 'Layers')", MessageOf(() => array.GetPixels(0, 1)));
            Object.DestroyImmediate(array);

            var volume = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false) { name = "Volume" };
            Assert.Equal("Texture3D.SetPixels32: one of the arguments is not valid. (Texture 'Volume')", MessageOf(() => volume.SetPixels32(null)));
            Assert.Equal("Texture3D.SetPixels32: size of data to be filled was larger than the size of data available in the source array. (Texture 'Volume')",
                MessageOf(() => volume.SetPixels32(new Color32[7])));
            volume.SetPixels(Fill(9, Color.blue));
            Assert.Equal(Color.blue, volume.GetPixel(1, 1, 1));
            Object.DestroyImmediate(volume);
        }

        [Fact]
        public void GetPixelDataNamesTheValidRangesAsTheReleasesManagedCodeDoes()
        {
            var volume = new Texture3D(4, 4, 4, TextureFormat.RGBA32, true);
            Assert.Equal("The passed in miplevel 3 is invalid. The valid range is 0 through  2", MessageOf(() => volume.GetPixelData<byte>(3)));
            var cubes = new CubemapArray(4, 2, TextureFormat.RGBA32, false);
            Assert.Equal("The passed in face 7 is invalid.  The valid range is 0 through 5", MessageOf(() => cubes.GetPixelData<byte>(0, (CubemapFace)7, 0)));
            Assert.Equal("The passed in element 2 is invalid. The valid range is 0 through 1", MessageOf(() => cubes.GetPixelData<byte>(0, CubemapFace.PositiveX, 2)));
            var cube = new Cubemap(4, TextureFormat.RGBA32, false);
            Assert.Equal("The passed in face Unknown is invalid. The valid range is 0 through 5.", MessageOf(() => cube.GetPixelData<byte>(0, CubemapFace.Unknown)));
            Object.DestroyImmediate(volume);
            Object.DestroyImmediate(cubes);
            Object.DestroyImmediate(cube);
        }

        [Fact]
        public void CubemapTexelAccessLogsAnUndefinedFaceOrLevel()
        {
            var cube = new Cubemap(4, TextureFormat.RGBA32, true);
            cube.SetPixel(CubemapFace.NegativeY, 0, 0, Color.red, 0);
            Assert.Equal(Color.white, cube.GetPixel((CubemapFace)6, 0, 0));
            cube.SetPixel(CubemapFace.NegativeY, 0, 0, Color.green, 5);
            Assert.Equal(Color.red, cube.GetPixel(CubemapFace.NegativeY, 0, 0));
            Assert.Equal(new[]
            {
                "Error: GetPixel called on an undefined image (valid values are 0 - 5",
                "Error: SetPixel called on an undefined mip (valid values are 0 - 2",
            }, m_Logs);
            Object.DestroyImmediate(cube);
        }

        /// <summary>SetPixelData writes a face's level from the given start of the array, and the
        /// pixel readers see it; other faces keep theirs.</summary>
        [Fact]
        public void CubemapSetPixelDataWritesAFaceLevel()
        {
            var cube = new Cubemap(2, TextureFormat.RGBA32, false) { name = "Cube" };
            cube.SetPixels(Fill(4, Color.blue), CubemapFace.PositiveX);
            var bytes = new byte[2 + 16];
            for (int i = 0; i < 4; i++)
            {
                bytes[2 + i * 4] = 255;
                bytes[2 + i * 4 + 3] = 255;
            }
            bytes[2 + 12 + 1] = 255;
            cube.SetPixelData(bytes, 0, CubemapFace.PositiveY, 2);
            Assert.Equal(new Color(1, 0, 0, 1), cube.GetPixel(CubemapFace.PositiveY, 0, 0));
            Assert.Equal(new Color(1, 1, 0, 1), cube.GetPixels(CubemapFace.PositiveY)[3]);
            Assert.Equal(Color.blue, cube.GetPixel(CubemapFace.PositiveX, 1, 1));
            Assert.Equal("Cubemap.SetPixelData: one of the arguments is not valid. (Texture 'Cube')", MessageOf(() => cube.SetPixelData(bytes, 0, (CubemapFace)6)));
            Assert.Equal("Cubemap.SetPixelData: invalid mipmap level. (Texture 'Cube')", MessageOf(() => cube.SetPixelData(bytes, 1, CubemapFace.PositiveY)));
            Assert.Equal("Cubemap.SetPixelData: size of data to be filled was larger than the size of data available in the source array. (Texture 'Cube')",
                MessageOf(() => cube.SetPixelData(bytes, 0, CubemapFace.PositiveY, 3)));
            Object.DestroyImmediate(cube);
        }

        static Texture2D Solid(int width, int height, TextureFormat format, Color color)
        {
            var texture = new Texture2D(width, height, format, false);
            texture.SetPixels(Fill(width * height, color));
            texture.Apply();
            return texture;
        }

        [Fact]
        public void CopyTextureRefusesUndefinedLevelsNegativeRegionsAndOtherDataSizes()
        {
            var red = Solid(4, 4, TextureFormat.RGBA32, Color.red);
            var larger = Solid(8, 8, TextureFormat.RGBA32, Color.red);
            var blue = Solid(4, 4, TextureFormat.RGBA32, Color.blue);
            Graphics.CopyTexture(red, 1, blue, 0);
            Graphics.CopyTexture(red, 0, 0, blue, 0, 1);
            Graphics.CopyTexture(red, 0, 0, 0, 0, -1, 2, blue, 0, 0, 0, 0);
            Graphics.CopyTexture(larger, 0, blue, 0);
            Assert.Equal(Color.blue, blue.GetPixel(0, 0));
            Assert.Equal(new[]
            {
                "Error: Graphics.CopyTexture called with invalid source element index (got 1, have 1 elements/layers)",
                "Error: Graphics.CopyTexture called with invalid destination mip level: got 1, have 1 mips (after mipmap limits if applicable)",
                "Error: Graphics.CopyTexture called with negative region size (width -1, height 2).",
                $"Error: Graphics.CopyTexture called with mismatching data size (src (8x8 with format {(int)larger.graphicsFormat} -> 256 bytes) dst (4x4 with format {(int)blue.graphicsFormat} -> 64 bytes))",
            }, m_Logs);
            // A level of as many bytes copies, also when its sizes differ.
            var wide = Solid(8, 2, TextureFormat.RGBA32, Color.green);
            Graphics.CopyTexture(wide, 0, blue, 0);
            Assert.Equal(Color.green, blue.GetPixel(0, 0));
            Assert.Equal(4, m_Logs.Count);
            foreach (var texture in new[] { red, larger, blue, wide }) Object.DestroyImmediate(texture);
        }

        [Fact]
        public void CompressedLevelsOfOtherByteCountsCopyWithoutTheSizeError()
        {
            var dxt1 = new Texture2D(8, 8, TextureFormat.DXT1, false);
            var dxt5 = new Texture2D(8, 8, TextureFormat.DXT5, false);
            Graphics.CopyTexture(dxt1, 0, 0, dxt5, 0, 0);
            Assert.DoesNotContain(m_Logs, log => log.Contains("mismatching data size"));
            Object.DestroyImmediate(dxt1);
            Object.DestroyImmediate(dxt5);
        }
    }
}
