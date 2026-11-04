using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Texture2D made or reinitialized with an explicit GraphicsFormat. Sources, all Unity 6000.0.71f1:
    /// <list type="bullet">
    /// <item>Unity's C# reference source (Texture.cs): the constructor checks
    /// <c>SystemInfo.IsFormatSupported(format, Sample)</c> with no fallback and logs "Texture creation
    /// failed. ..." without creating the texture; its data colour space is sRGB for an sRGB format;
    /// <c>Reinitialize(int, int, GraphicsFormat, bool)</c> throws the non-readable exception first.</item>
    /// <item>tests/projects/urp caps capture: which named formats support sampling.</item>
    /// <item>tests/projects/texturepixels: the graphicsFormat each TextureFormat's Texture2D reports
    /// (Alpha8 54, ARGB32 88 / sRGB 87, R8 R8_UNorm / R8_SRGB) and how R8 and Alpha8 read.</item>
    /// <item>tests/projects/assetclones ColorLinear/ColorGamma: four GraphicsFormat-made textures.</item>
    /// </list>
    /// Which texture format holds any other graphics format is Unbound's identity (the texture
    /// format reporting it), not a measured Unity answer; the "Unbound:" error text is Unbound's.
    /// </summary>
    public class TextureGraphicsFormatContractTests
    {
        sealed class Profile : IDisposable
        {
            readonly ColorSpace m_Saved = QualitySettings.s_ColorSpace;
            public Profile(ColorSpace space) => QualitySettings.s_ColorSpace = space;
            public void Dispose() => QualitySettings.s_ColorSpace = m_Saved;
        }

        static List<string> Errors(Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception) errors.Add(message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        static string CreationFailed(GraphicsFormat format) =>
            $"Texture creation failed. '{format}' is not supported for Sample usage on this platform. Use 'SystemInfo.IsFormatSupported' C# API to check format support.";

        static void AssertUnboundError(List<string> errors, GraphicsFormat format)
        {
            Assert.Single(errors);
            Assert.StartsWith("Unbound: ", errors[0]);
            Assert.Contains($"({(int)format})", errors[0]);
        }

        static void Destroy(params Texture2D[] textures)
        {
            foreach (var t in textures)
                if (t != null) Object.DestroyImmediate(t);
        }

        [Fact]
        public void R8UNormMakesAnR8TextureNotAlpha8()
        {
            using var profile = new Profile(ColorSpace.Gamma);
            var r8 = new Texture2D(2, 1, GraphicsFormat.R8_UNorm, TextureCreationFlags.None);
            var alpha8 = new Texture2D(2, 1, TextureFormat.Alpha8, false);
            try
            {
                Assert.Equal(TextureFormat.R8, r8.format);
                Assert.Equal(GraphicsFormat.R8_UNorm, r8.graphicsFormat);
                Assert.False(r8.isDataSRGB);
                Assert.NotEqual(alpha8.graphicsFormat, r8.graphicsFormat);
                var cells = new[] { new Color32(10, 20, 30, 40), new Color32(50, 60, 70, 80) };
                r8.SetPixels32(cells);
                alpha8.SetPixels32(cells);
                Assert.Equal(new Color32(10, 255, 255, 255), r8.GetPixels32()[0]);
                Assert.Equal(new Color32(0, 0, 0, 40), alpha8.GetPixels32()[0]);
                Assert.Equal(new byte[] { 10, 50 }, r8.GetRawTextureData());
                Assert.Equal(new byte[] { 40, 80 }, alpha8.GetRawTextureData());
            }
            finally { Destroy(r8, alpha8); }
        }

        [Fact]
        public void R8SrgbMakesAnSrgbR8Texture()
        {
            using var profile = new Profile(ColorSpace.Linear);
            var texture = new Texture2D(2, 2, GraphicsFormat.R8_SRGB, TextureCreationFlags.None);
            try
            {
                Assert.Equal(TextureFormat.R8, texture.format);
                Assert.True(texture.isDataSRGB);
                Assert.Equal(GraphicsFormat.R8_SRGB, texture.graphicsFormat);
                Assert.Equal(4, texture.GetRawTextureData().Length);
            }
            finally { Destroy(texture); }
        }

        // A Texture2D's own reported graphicsFormat (Linear profile) makes the same kind of texture.
        // Excluded: ARGB4444 and sRGB RG16, whose reports (B4G4R4A4_UNormPack16, R8G8_SRGB) do not
        // support sampling on the reference device, and the data colour space of formats with one
        // report for both (Alpha8, R16, the float formats), which only linear data round-trips.
        [Theory]
        [InlineData(TextureFormat.RGBA32, false)]
        [InlineData(TextureFormat.RGBA32, true)]
        [InlineData(TextureFormat.ARGB32, false)]
        [InlineData(TextureFormat.ARGB32, true)]
        [InlineData(TextureFormat.BGRA32, false)]
        [InlineData(TextureFormat.BGRA32, true)]
        [InlineData(TextureFormat.RGB24, false)]
        [InlineData(TextureFormat.RGB24, true)]
        [InlineData(TextureFormat.Alpha8, true)]
        [InlineData(TextureFormat.R8, false)]
        [InlineData(TextureFormat.R8, true)]
        [InlineData(TextureFormat.RG16, true)]
        [InlineData(TextureFormat.R16, true)]
        [InlineData(TextureFormat.RGBA4444, true)]
        [InlineData(TextureFormat.RGB565, true)]
        [InlineData(TextureFormat.RHalf, true)]
        [InlineData(TextureFormat.RGHalf, true)]
        [InlineData(TextureFormat.RGBAHalf, true)]
        [InlineData(TextureFormat.RFloat, true)]
        [InlineData(TextureFormat.RGFloat, true)]
        [InlineData(TextureFormat.RGBAFloat, true)]
        public void ReportedGraphicsFormatMakesTheSameTexture(TextureFormat format, bool linear)
        {
            using var profile = new Profile(ColorSpace.Linear);
            var source = new Texture2D(4, 4, format, false, linear);
            Texture2D copy = null;
            try
            {
                var sourceBytes = source.GetRawTextureData();
                var errors = Errors(() => copy = new Texture2D(4, 4, source.graphicsFormat, TextureCreationFlags.None));
                Assert.Empty(errors);
                Assert.Equal(format, copy.format);
                Assert.Equal(source.isDataSRGB, copy.isDataSRGB);
                Assert.Equal(source.graphicsFormat, copy.graphicsFormat);
                Assert.Equal(sourceBytes.Length, copy.GetRawTextureData().Length);
                Assert.Equal(format, source.format);
                Assert.Equal(sourceBytes, source.GetRawTextureData());
            }
            finally { Destroy(source, copy); }
        }

        // tests/projects/assetclones Color{Linear,Gamma}: format, isDataSRGB and graphicsFormat of
        // GraphicsFormat-made textures in each profile.
        [Theory]
        [InlineData(GraphicsFormat.R8G8B8A8_SRGB, ColorSpace.Linear, TextureFormat.RGBA32, true, GraphicsFormat.R8G8B8A8_SRGB)]
        [InlineData(GraphicsFormat.R8G8B8A8_SRGB, ColorSpace.Gamma, TextureFormat.RGBA32, true, GraphicsFormat.R8G8B8A8_UNorm)]
        [InlineData(GraphicsFormat.R8G8B8A8_UNorm, ColorSpace.Linear, TextureFormat.RGBA32, false, GraphicsFormat.R8G8B8A8_UNorm)]
        [InlineData(GraphicsFormat.R8G8B8A8_UNorm, ColorSpace.Gamma, TextureFormat.RGBA32, false, GraphicsFormat.R8G8B8A8_UNorm)]
        [InlineData(GraphicsFormat.R16G16B16A16_SFloat, ColorSpace.Linear, TextureFormat.RGBAHalf, false, GraphicsFormat.R16G16B16A16_SFloat)]
        [InlineData(GraphicsFormat.R16G16B16A16_SFloat, ColorSpace.Gamma, TextureFormat.RGBAHalf, false, GraphicsFormat.R16G16B16A16_SFloat)]
        [InlineData(GraphicsFormat.R32_SFloat, ColorSpace.Linear, TextureFormat.RFloat, false, GraphicsFormat.R32_SFloat)]
        [InlineData(GraphicsFormat.R32_SFloat, ColorSpace.Gamma, TextureFormat.RFloat, false, GraphicsFormat.R32_SFloat)]
        public void MeasuredGraphicsFormatTextures(GraphicsFormat format, ColorSpace space, TextureFormat expected, bool srgbData, GraphicsFormat reported)
        {
            using var profile = new Profile(space);
            var texture = new Texture2D(2, 2, format, TextureCreationFlags.None);
            try
            {
                Assert.Equal(expected, texture.format);
                Assert.Equal(srgbData, texture.isDataSRGB);
                Assert.Equal(reported, texture.graphicsFormat);
            }
            finally { Destroy(texture); }
        }

        // Formats the reference device cannot sample (tests/projects/urp caps): Unity's constructor
        // logs its error and creates no texture, with no fallback to another format.
        [Theory]
        [InlineData(GraphicsFormat.R8G8_SRGB)]
        [InlineData(GraphicsFormat.B4G4R4A4_UNormPack16)]
        [InlineData(GraphicsFormat.RGBA_ASTC4X4_UNorm)]
        [InlineData(GraphicsFormat.R8_UInt)]
        [InlineData(GraphicsFormat.D16_UNorm)]
        public void FormatsWithoutSamplingFailAsInUnity(GraphicsFormat format)
        {
            Assert.False(SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample));
            Texture2D texture = null;
            var errors = Errors(() => texture = new Texture2D(4, 4, format, TextureCreationFlags.None));
            Assert.Equal(new[] { CreationFailed(format) }, errors);
            Assert.True(texture == null);
        }

        // Formats no texture format Unbound stores reports: an Unbound error and no texture, never
        // an RGBA32 stand-in.
        [Theory]
        [InlineData(GraphicsFormat.R8_SNorm)]
        [InlineData(GraphicsFormat.R32G32B32_SFloat)]
        [InlineData(GraphicsFormat.B8G8R8_UNorm)]
        [InlineData(GraphicsFormat.RGBA_DXT3_UNorm)]
        [InlineData(GraphicsFormat.R_EAC_UNorm)]
        [InlineData(GraphicsFormat.None)]
        [InlineData((GraphicsFormat)55)]
        public void FormatsUnboundCannotHoldFailLoudly(GraphicsFormat format)
        {
            if (format != GraphicsFormat.None && Enum.IsDefined(typeof(GraphicsFormat), format))
                Assert.True(SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample));
            Texture2D texture = null;
            var errors = Errors(() => texture = new Texture2D(4, 4, format, TextureCreationFlags.None));
            AssertUnboundError(errors, format);
            Assert.True(texture == null);
        }

        [Fact]
        public void EveryCreatedNamedFormatReportsItselfAndItsColourSpace()
        {
            using var profile = new Profile(ColorSpace.Linear);
            var seen = new HashSet<GraphicsFormat>();
            foreach (GraphicsFormat format in Enum.GetValues(typeof(GraphicsFormat)))
            {
                if (format == GraphicsFormat.None || !seen.Add(format)) continue;
                Texture2D texture = null;
                var errors = Errors(() => texture = new Texture2D(4, 4, format, TextureCreationFlags.None));
                if (texture == null)
                {
                    Assert.Single(errors);
                    continue;
                }
                try
                {
                    Assert.Empty(errors);
                    Assert.True(SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample));
                    Assert.Equal(format, texture.graphicsFormat);
                    Assert.Equal(GraphicsFormatUtility.IsSRGBFormat(format), texture.isDataSRGB);
                }
                finally { Destroy(texture); }
            }
        }

        [Theory]
        [InlineData(GraphicsFormat.R8_UNorm, 1)]
        [InlineData(GraphicsFormat.R8G8_UNorm, 2)]
        [InlineData(GraphicsFormat.R16_UNorm, 2)]
        [InlineData(GraphicsFormat.R8G8B8A8_UNorm, 4)]
        [InlineData(GraphicsFormat.R32_SFloat, 4)]
        [InlineData(GraphicsFormat.R16G16B16A16_SFloat, 8)]
        [InlineData((GraphicsFormat)54, 1)]
        [InlineData((GraphicsFormat)88, 4)]
        public void MipsAndRawLengthFollowTheResolvedFormat(GraphicsFormat format, int texelBytes)
        {
            var chain = new Texture2D(8, 8, format, TextureCreationFlags.MipChain);
            var two = new Texture2D(8, 8, format, 2, TextureCreationFlags.None);
            var one = new Texture2D(8, 8, format, TextureCreationFlags.None);
            try
            {
                Assert.Equal(4, chain.mipmapCount);
                Assert.Equal(texelBytes * (64 + 16 + 4 + 1), chain.GetRawTextureData().Length);
                Assert.Equal(2, two.mipmapCount);
                Assert.Equal(texelBytes * (64 + 16), two.GetRawTextureData().Length);
                Assert.Equal(1, one.mipmapCount);
                Assert.Equal(texelBytes * 64, one.GetRawTextureData().Length);
                Assert.Equal(chain.format, one.format);
            }
            finally { Destroy(chain, two, one); }
        }

        [Fact]
        public void ReinitializeResolvesAsTheConstructor()
        {
            using var profile = new Profile(ColorSpace.Linear);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.True(texture.Reinitialize(4, 4, GraphicsFormat.R8_SRGB, true));
                Assert.Equal(TextureFormat.R8, texture.format);
                Assert.True(texture.isDataSRGB);
                Assert.Equal(GraphicsFormat.R8_SRGB, texture.graphicsFormat);
                Assert.Equal(3, texture.mipmapCount);
                Assert.Equal(16 + 4 + 1, texture.GetRawTextureData().Length);
                Assert.True(texture.Reinitialize(2, 2, (GraphicsFormat)54, false));
                Assert.Equal(TextureFormat.Alpha8, texture.format);
                Assert.Equal(4, texture.GetRawTextureData().Length);
            }
            finally { Destroy(texture); }
        }

        [Theory]
        [InlineData(TextureFormat.R8, false)]
        [InlineData(TextureFormat.ARGB32, false)]
        [InlineData(TextureFormat.Alpha8, true)]
        [InlineData(TextureFormat.BGRA32, false)]
        [InlineData(TextureFormat.RGBAHalf, true)]
        public void ReinitializeWithTheReportedFormatKeepsTheFormat(TextureFormat format, bool linear)
        {
            using var profile = new Profile(ColorSpace.Linear);
            var texture = new Texture2D(4, 4, format, true, linear);
            try
            {
                var reported = texture.graphicsFormat;
                bool srgb = texture.isDataSRGB;
                Assert.True(texture.Reinitialize(2, 2, reported, false));
                Assert.Equal(format, texture.format);
                Assert.Equal(srgb, texture.isDataSRGB);
                Assert.Equal(reported, texture.graphicsFormat);
                Assert.Equal(1, texture.mipmapCount);
                Assert.Equal(2, texture.width);
            }
            finally { Destroy(texture); }
        }

        [Theory]
        [InlineData(GraphicsFormat.R8_SNorm)]
        [InlineData(GraphicsFormat.R32G32B32_SFloat)]
        [InlineData(GraphicsFormat.None)]
        [InlineData((GraphicsFormat)55)]
        public void RejectedReinitializeLeavesTheTextureAsItWas(GraphicsFormat format)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, true, false);
            try
            {
                var cells = new Color32[16];
                for (int i = 0; i < cells.Length; i++) cells[i] = new Color32((byte)i, (byte)(2 * i), (byte)(3 * i), 255);
                texture.SetPixels32(cells);
                var bytes = texture.GetRawTextureData();
                var reported = texture.graphicsFormat;
                uint updates = texture.updateCount;
                bool result = true;
                var errors = Errors(() => result = texture.Reinitialize(8, 8, format, false));
                Assert.False(result);
                AssertUnboundError(errors, format);
                Assert.Equal(4, texture.width);
                Assert.Equal(4, texture.height);
                Assert.Equal(TextureFormat.RGBA32, texture.format);
                Assert.Equal(reported, texture.graphicsFormat);
                Assert.Equal(3, texture.mipmapCount);
                Assert.True(texture.isReadable);
                Assert.True(texture.isDataSRGB);
                Assert.Equal(updates, texture.updateCount);
                Assert.Equal(bytes, texture.GetRawTextureData());
                Assert.Equal(cells, texture.GetPixels32());
            }
            finally { Destroy(texture); }
        }

        [Fact]
        public void ReinitializeWithAGraphicsFormatRequiresAReadableTexture()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.Apply(false, true);
                var error = Assert.Throws<UnityException>(() => texture.Reinitialize(4, 4, GraphicsFormat.R8_UNorm, false));
                Assert.Equal($"Texture '{texture.name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.", error.Message);
                Assert.False(texture.isReadable);
                Assert.Equal(2, texture.width);
                Assert.Equal(TextureFormat.RGBA32, texture.format);
            }
            finally { Destroy(texture); }
        }

        // The texture keeps the format and data colour space it was made with; later textures made
        // from the (colour-space dependent) default format do not change it.
        [Fact]
        public void CreatedTextureIsIndependentOfLaterDefaultFormats()
        {
            Texture2D first = null, second = null;
            try
            {
                GraphicsFormat firstFormat;
                using (new Profile(ColorSpace.Linear))
                {
                    firstFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
                    first = new Texture2D(2, 2, DefaultFormat.LDR, TextureCreationFlags.None);
                }
                var format = first.format;
                bool srgb = first.isDataSRGB;
                Assert.Equal(GraphicsFormatUtility.IsSRGBFormat(firstFormat), srgb);
                using (new Profile(ColorSpace.Gamma))
                {
                    var secondFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
                    second = new Texture2D(2, 2, DefaultFormat.LDR, TextureCreationFlags.None);
                    Assert.Equal(GraphicsFormatUtility.IsSRGBFormat(secondFormat), second.isDataSRGB);
                    Assert.Equal(format, first.format);
                    Assert.Equal(srgb, first.isDataSRGB);
                }
                Assert.Equal(format, first.format);
                Assert.Equal(srgb, first.isDataSRGB);
            }
            finally { Destroy(first, second); }
        }

        // Preserved measured reports (tests/projects/texturepixels): Alpha8 and ARGB32 report unnamed
        // values; GetTextureFormat maps both B8G8R8A8 formats to BGRA32.
        [Fact]
        public void MeasuredReportsAndConversionsArePreserved()
        {
            using var profile = new Profile(ColorSpace.Linear);
            var alpha8 = new Texture2D(2, 2, TextureFormat.Alpha8, false);
            var argb = new Texture2D(2, 2, TextureFormat.ARGB32, false, false);
            var argbLinear = new Texture2D(2, 2, TextureFormat.ARGB32, false, true);
            try
            {
                Assert.Equal(54, (int)alpha8.graphicsFormat);
                Assert.Equal(87, (int)argb.graphicsFormat);
                Assert.Equal(88, (int)argbLinear.graphicsFormat);
                Assert.Equal(TextureFormat.BGRA32, GraphicsFormatUtility.GetTextureFormat(GraphicsFormat.B8G8R8A8_UNorm));
                Assert.Equal(TextureFormat.BGRA32, GraphicsFormatUtility.GetTextureFormat(GraphicsFormat.B8G8R8A8_SRGB));
            }
            finally { Destroy(alpha8, argb, argbLinear); }
        }
    }
}
