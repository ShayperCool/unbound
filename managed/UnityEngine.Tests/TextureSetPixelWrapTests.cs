using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Texture2D.SetPixel outside the selected level. Every result is read back in range
    /// (GetPixels32 cells, raw bytes or the pixel data view), never through an out-of-range
    /// GetPixel that would repeat the same mapping.</summary>
    public class TextureSetPixelWrapTests
    {
        static readonly Color32 Ink = new Color32(250, 241, 232, 223);

        sealed class Scope : IDisposable
        {
            readonly List<Texture2D> m_Textures = new List<Texture2D>();
            internal Texture2D Keep(Texture2D texture)
            {
                m_Textures.Add(texture);
                return texture;
            }
            public void Dispose()
            {
                foreach (var texture in m_Textures)
                    if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        /// <summary>A distinct, known cell in every texel of every level.</summary>
        static Color32 Marker(int mip, int i) => new Color32((byte)(i + 1), (byte)(20 + mip), 7, 255);

        static Texture2D Marked(Scope scope, int width, int height, bool mips)
        {
            var texture = scope.Keep(new Texture2D(width, height, TextureFormat.RGBA32, mips, true) { hideFlags = HideFlags.HideAndDontSave });
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
                var cells = new Color32[w * h];
                for (int i = 0; i < cells.Length; i++) cells[i] = Marker(mip, i);
                texture.SetPixels32(cells, mip);
            }
            return texture;
        }

        /// <summary>Every level holds its markers except <paramref name="index"/> of
        /// <paramref name="mip"/>, which holds the ink (index -1: nothing changed).</summary>
        static void AssertOnly(Texture2D texture, int mip, int index)
        {
            for (int level = 0; level < texture.mipmapCount; level++)
            {
                var cells = texture.GetPixels32(level);
                for (int i = 0; i < cells.Length; i++)
                    Assert.Equal(level == mip && i == index ? Ink : Marker(level, i), cells[i]);
            }
        }

        static List<int> Changed(byte[] before, byte[] after, int texelBytes)
        {
            Assert.Equal(before.Length, after.Length);
            var changed = new List<int>();
            for (int i = 0; i < before.Length; i += texelBytes)
                if (!before.AsSpan(i, texelBytes).SequenceEqual(after.AsSpan(i, texelBytes))) changed.Add(i / texelBytes);
            return changed;
        }

        // ---- Documented contract (6000.0 Texture2D.SetPixel: "If the pixel coordinate is outside
        // the texture's dimensions, Unity clamps or repeats it, depending on the texture's
        // TextureWrapMode"; wrapModeU is the horizontal axis). Expected cells follow from that
        // sentence; no Unity capture measures them.

        [Theory]
        // In-range controls.
        [InlineData(2, 1, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 2, 1)]
        [InlineData(0, 0, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 0, 0)]
        [InlineData(4, 2, TextureWrapMode.Clamp, TextureWrapMode.Repeat, 4, 2)]
        // Repeat: both edges on both axes, several periods.
        [InlineData(-1, 0, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 4, 0)]
        [InlineData(5, 0, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 0, 0)]
        [InlineData(0, -1, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 0, 2)]
        [InlineData(0, 3, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 0, 0)]
        [InlineData(12, 7, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 2, 1)]
        [InlineData(-11, -4, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 4, 2)]
        [InlineData(-5, -3, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 0, 0)]
        // Clamp: the edge texel on both sides of both axes.
        [InlineData(-1, 1, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 0, 1)]
        [InlineData(5, 1, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 4, 1)]
        [InlineData(2, -3, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2, 0)]
        [InlineData(2, 3, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2, 2)]
        [InlineData(-100, 50, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 0, 2)]
        [InlineData(99, -99, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 4, 0)]
        // Mixed axes: x follows U, y follows V.
        [InlineData(-1, 10, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 4, 2)]
        [InlineData(10, -1, TextureWrapMode.Clamp, TextureWrapMode.Repeat, 4, 2)]
        [InlineData(-7, 4, TextureWrapMode.Clamp, TextureWrapMode.Repeat, 0, 1)]
        [InlineData(7, -7, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 2, 0)]
        public void OutOfRangeWriteReachesTheWrappedTexel(int x, int y, TextureWrapMode u, TextureWrapMode v, int expectedX, int expectedY)
        {
            foreach (bool explicitMip in new[] { false, true })
            {
                using var scope = new Scope();
                // 5x3: non-square, neither side a power of two.
                var texture = Marked(scope, 5, 3, false);
                texture.wrapModeU = u;
                texture.wrapModeV = v;
                if (explicitMip) texture.SetPixel(x, y, Ink, 0);
                else texture.SetPixel(x, y, Ink);
                AssertOnly(texture, 0, expectedY * 5 + expectedX);
            }
        }

        [Fact]
        public void WrapModeSetsBothAxes()
        {
            using var scope = new Scope();
            var texture = Marked(scope, 5, 3, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixel(-8, 9, Ink);
            AssertOnly(texture, 0, 2 * 5 + 0);
        }

        [Theory]
        // Level 1 of 5x3 is 2x1: wrapping by level 0's 5x3 would pick x 3 or y 2 instead.
        [InlineData(3, 0, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1, 1)]
        [InlineData(-1, 5, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1, 1)]
        [InlineData(-2, 2, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 1, 0)]
        [InlineData(7, -2, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 1, 1)]
        [InlineData(-3, 9, TextureWrapMode.Clamp, TextureWrapMode.Repeat, 1, 0)]
        [InlineData(1, 0, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 1, 1)] // in-range control
        // Level 2 is 1x1: every coordinate is its only texel.
        [InlineData(-9, 13, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 2, 0)]
        [InlineData(4, -6, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 2, 0)]
        [InlineData(1, 1, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 2, 0)]
        public void OutOfRangeWriteWrapsBySelectedLevel(int x, int y, TextureWrapMode u, TextureWrapMode v, int mip, int expectedIndex)
        {
            using var scope = new Scope();
            var texture = Marked(scope, 5, 3, true);
            Assert.Equal(3, texture.mipmapCount);
            texture.wrapModeU = u;
            texture.wrapModeV = v;
            texture.SetPixel(x, y, Ink, mip);
            AssertOnly(texture, mip, expectedIndex);
        }

        // ---- Arithmetic safety (implementation property, NOT a Unity observation): extreme
        // coordinates on a small non-power-of-two texture neither throw nor leave the level.
        // Which texel Unity writes for them is not measured; Clamp's edge follows the contract.

        [Theory]
        [InlineData(int.MinValue, int.MinValue)]
        [InlineData(int.MaxValue, int.MaxValue)]
        [InlineData(int.MinValue, int.MaxValue)]
        [InlineData(int.MaxValue, int.MinValue)]
        public void ExtremeCoordinatesChangeExactlyOneTexel(int x, int y)
        {
            foreach (var mode in new[] { TextureWrapMode.Repeat, TextureWrapMode.Clamp, TextureWrapMode.Mirror, TextureWrapMode.MirrorOnce })
                for (int mip = 0; mip < 3; mip++)
                {
                    using var scope = new Scope();
                    var texture = Marked(scope, 5, 3, true);
                    texture.wrapMode = mode;
                    var before = texture.GetRawTextureData();
                    texture.SetPixel(x, y, Ink, mip);
                    var after = texture.GetRawTextureData();
                    var changed = Changed(before, after, 4);
                    Assert.Single(changed);
                    int w = Math.Max(1, 5 >> mip), h = Math.Max(1, 3 >> mip);
                    int offset = 0;
                    for (int level = 0; level < mip; level++) offset += Math.Max(1, 5 >> level) * Math.Max(1, 3 >> level);
                    int index = changed[0] - offset;
                    Assert.InRange(index, 0, w * h - 1);
                    if (mode == TextureWrapMode.Clamp)
                        Assert.Equal((y < 0 ? 0 : h - 1) * w + (x < 0 ? 0 : w - 1), index);
                    Assert.Equal(Ink, texture.GetPixels32(mip)[index]);
                }
        }

        // ---- Mirror/MirrorOnce: the documented pages describe sampling only. This implementation
        // reuses GetPixel's mapping; only "one texel inside the selected level" is asserted.

        [Theory]
        [InlineData(TextureWrapMode.Mirror)]
        [InlineData(TextureWrapMode.MirrorOnce)]
        public void MirroredOutOfRangeWriteStaysInsideTheLevel(TextureWrapMode mode)
        {
            foreach (var (x, y) in new[] { (-1, 0), (5, 1), (0, -1), (2, 3), (-6, -4), (11, 8) })
            {
                using var scope = new Scope();
                var texture = Marked(scope, 5, 3, false);
                texture.wrapMode = mode;
                var before = texture.GetRawTextureData();
                texture.SetPixel(x, y, Ink);
                var changed = Changed(before, texture.GetRawTextureData(), 4);
                Assert.Single(changed);
                Assert.InRange(changed[0], 0, 14);
            }
        }

        // ---- Preservation of existing behaviour (Unbound regressions, not Unity observations).

        [Theory]
        [InlineData(TextureFormat.RGBA32)]
        [InlineData(TextureFormat.RGB24)]
        [InlineData(TextureFormat.R8)]
        [InlineData(TextureFormat.RGBAHalf)]
        [InlineData(TextureFormat.RGBAFloat)]
        [InlineData(TextureFormat.R16)]
        public void WrappedWriteConvertsLikeTheInRangeWrite(TextureFormat format)
        {
            using var scope = new Scope();
            var color = new Color(0.5f, 0.2f, 1.75f, 0.3f);
            var wrapped = scope.Keep(new Texture2D(3, 2, format, false, true) { hideFlags = HideFlags.HideAndDontSave });
            var direct = scope.Keep(new Texture2D(3, 2, format, false, true) { hideFlags = HideFlags.HideAndDontSave });
            wrapped.SetPixel(-1, 3, color); // Repeat: (2, 1)
            direct.SetPixel(2, 1, color);
            Assert.Equal(direct.GetRawTextureData(), wrapped.GetRawTextureData());
            Assert.Equal(direct.GetPixels(), wrapped.GetPixels());
        }

        [Fact]
        public void WrappedWriteGoesThroughThePixelDataView()
        {
            using var scope = new Scope();
            var texture = scope.Keep(new Texture2D(3, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave });
            var data = new byte[24];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(100 + i);
            texture.SetPixelData(data, 0);
            var view = texture.GetPixelData<byte>(0);
            texture.SetPixel(-4, -1, new Color32(10, 20, 30, 40)); // Repeat: (2, 1), texel 5
            var expected = (byte[])data.Clone();
            expected[20] = 10; expected[21] = 20; expected[22] = 30; expected[23] = 40;
            Assert.Equal(expected, view.ToArray());
            Assert.Equal(new Color32(10, 20, 30, 40), texture.GetPixels32()[5]);
        }

        static List<string> Errors(Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(10)]
        public void InvalidMipStillLogsAndWritesNothing(int mip)
        {
            using var scope = new Scope();
            var texture = Marked(scope, 5, 3, true);
            texture.wrapMode = TextureWrapMode.Repeat;
            var errors = Errors(() => texture.SetPixel(-1, 7, Ink, mip));
            // Unprefixed Unity message: Application.logMessageReceived sees it.
            Assert.Equal("Texture mip level is out of range.", Assert.Single(errors));
            AssertOnly(texture, -1, -1);
        }

        [Fact]
        public void UnreadableTextureStillThrows()
        {
            using var scope = new Scope();
            var texture = Marked(scope, 5, 3, false);
            texture.Apply(false, true);
            var raw = texture.GetRawTextureData();
            Assert.Throws<UnityException>(() => texture.SetPixel(-1, -1, Ink));
            Assert.Throws<UnityException>(() => texture.SetPixel(9, 9, Ink, 0));
            Assert.Equal(raw, texture.GetRawTextureData());
        }

        [Fact]
        public void BlockWritersStillRejectOutOfRangeRectangles()
        {
            using var scope = new Scope();
            var texture = Marked(scope, 5, 3, false);
            Assert.Throws<ArgumentException>(() => texture.SetPixels(-1, 0, 2, 2, new Color[4]));
            Assert.Throws<ArgumentException>(() => texture.SetPixels(4, 0, 2, 1, new Color[2]));
            Assert.Throws<ArgumentException>(() => texture.SetPixels32(0, 2, 1, 2, new Color32[2]));
            Assert.Throws<ArgumentException>(() => texture.SetPixels32(0, -1, 1, 1, new Color32[1]));
            AssertOnly(texture, -1, -1);
        }

        // ---- GPU storage controls. Base behaviour measured on Unity 6000.0.71f1 for IN-RANGE
        // SetPixel (tests/projects/assetclones texture.dirty) and SetPixels (tests/projects/samplerbias
        // cache/pixels/unapplied): CPU edits stay off established GPU storage until Apply. That an
        // out-of-range write behaves the same is the documented "sets pixel data in CPU memory".

        [Fact]
        public void WrappedWriteAfterApplyStaysOnTheCpuUntilApply()
        {
            using var scope = new Scope();
            var texture = Marked(scope, 2, 2, false);
            texture.Apply();
            uint applied = texture.updateCount;
            ulong handle = TextureHandles.Of(texture);
            Assert.NotEqual(0UL, handle);
            texture.SetPixel(-1, -1, Ink); // Repeat: (1, 1)
            AssertOnly(texture, 0, 3);
            Assert.Equal(handle, TextureHandles.Of(texture));
            Assert.Equal(applied, texture.updateCount);
            Assert.False(texture.m_PixelsDirty);
            texture.Apply(false);
            Assert.Equal(applied + 1, texture.updateCount);
            Assert.Equal(handle, TextureHandles.Of(texture));
            Assert.False(texture.m_PixelsDirty);
            AssertOnly(texture, 0, 3);
        }

        [Fact]
        public void WrappedWriteBeforeFirstUseIsUploadedAtFirstUse()
        {
            using var scope = new Scope();
            var texture = Marked(scope, 2, 2, false);
            texture.SetPixel(5, -1, Ink); // Repeat: (1, 1)
            Assert.Equal(0u, texture.updateCount);
            Assert.NotEqual(0UL, TextureHandles.Of(texture));
            Assert.Equal(1u, texture.updateCount);
            Assert.False(texture.m_PixelsDirty);
            AssertOnly(texture, 0, 3);
        }

        [Fact]
        public void WrappedMipWriteSurvivesApplyWithoutMipUpdate()
        {
            using var scope = new Scope();
            var texture = Marked(scope, 4, 4, true);
            texture.Apply(false);
            uint applied = texture.updateCount;
            texture.wrapModeU = TextureWrapMode.Clamp;
            texture.SetPixel(-1, 9, Ink, 1); // 2x2 level: Clamp x 0, Repeat y 1
            AssertOnly(texture, 1, 2);
            Assert.Equal(applied, texture.updateCount);
            texture.Apply(false);
            Assert.Equal(applied + 1, texture.updateCount);
            AssertOnly(texture, 1, 2);
        }
    }
}
