using System.Collections.Generic;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The parts of FontEngine that need no native face: atlas packing and texture resets.
    /// Faces and glyph rendering are checked by the `hello` fixture project.</summary>
    public class FontEngineTests
    {
        static List<GlyphRect> Atlas(int width, int height) =>
            // TextMeshPro starts a dynamic atlas this way.
            new List<GlyphRect> { new GlyphRect(0, 0, width - 1, height - 1) };

        static bool Overlaps(GlyphRect a, GlyphRect b) =>
            a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;

        [Fact]
        public void PackedGlyphsSitInsideTheirPaddedRectsWithoutOverlapping()
        {
            var free = Atlas(256, 256);
            var used = new List<GlyphRect>();
            var placed = new List<Glyph>();
            for (int i = 0; i < 40; i++)
            {
                var glyph = new Glyph((uint)i, new GlyphMetrics(10, 10, 0, 10, 12), new GlyphRect(0, 0, 10 + i % 7, 14 + i % 5));
                Assert.True(FontEngine.TryPackGlyphInAtlas(glyph, 4, GlyphPackingMode.BestShortSideFit, GlyphRenderMode.SDFAA, 256, 256, free, used));
                placed.Add(glyph);
            }
            Assert.Equal(40, used.Count);
            for (int i = 0; i < placed.Count; i++)
            {
                var g = placed[i].glyphRect;
                var u = used[i];
                // Unity's layout: padding plus a one-pixel gutter left and below, padding right and above.
                Assert.Equal(u.x + 5, g.x);
                Assert.Equal(u.y + 5, g.y);
                Assert.Equal(g.width + 9, u.width);
                Assert.Equal(g.height + 9, u.height);
                Assert.True(u.x >= 0 && u.y >= 0 && u.x + u.width <= 255 && u.y + u.height <= 255);
                for (int j = i + 1; j < used.Count; j++) Assert.False(Overlaps(u, used[j]), $"{u} overlaps {used[j]}");
                foreach (var f in free) Assert.False(Overlaps(u, f), $"{u} overlaps free {f}");
            }
        }

        [Fact]
        public void PackingStopsWhenTheAtlasIsFull()
        {
            var free = Atlas(64, 64);
            var used = new List<GlyphRect>();
            var toAdd = new List<Glyph>();
            for (int i = 0; i < 10; i++) toAdd.Add(new Glyph((uint)i, default, new GlyphRect(0, 0, 20, 20)));
            var added = new List<Glyph>();
            Assert.False(FontEngine.TryPackGlyphsInAtlas(toAdd, added, 2, GlyphPackingMode.BestAreaFit, GlyphRenderMode.SDFAA, 64, 64, free, used));
            // 25-pixel cells: two by two fit in 63 x 63.
            Assert.Equal(4, added.Count);
            Assert.Equal(6, toAdd.Count);
        }

        [Fact]
        public void EmptyGlyphsTakeNoAtlasSpace()
        {
            var free = Atlas(64, 64);
            var used = new List<GlyphRect>();
            var space = new Glyph(3, new GlyphMetrics(0, 0, 0, 0, 9), GlyphRect.zero);
            Assert.True(FontEngine.TryPackGlyphInAtlas(space, 5, GlyphPackingMode.BestShortSideFit, GlyphRenderMode.SDFAA, 64, 64, free, used));
            Assert.Empty(used);
            Assert.Equal(GlyphRect.zero, space.glyphRect);
        }

        [Theory]
        [InlineData(GlyphPackingMode.BestLongSideFit)]
        [InlineData(GlyphPackingMode.BottomLeftRule)]
        [InlineData(GlyphPackingMode.ContactPointRule)]
        public void EveryPackingModeFillsWithoutOverlap(GlyphPackingMode mode)
        {
            var free = Atlas(128, 128);
            var used = new List<GlyphRect>();
            int count = 0;
            while (FontEngine.TryPackGlyphInAtlas(new Glyph(0, default, new GlyphRect(0, 0, 7 + count % 9, 5 + count % 4)), 1, mode, GlyphRenderMode.SDFAA, 128, 128, free, used))
                count++;
            Assert.True(count > 60, $"{count} glyphs");
            for (int i = 0; i < used.Count; i++)
                for (int j = i + 1; j < used.Count; j++)
                    Assert.False(Overlaps(used[i], used[j]));
        }

        [Fact]
        public void FacesThatCannotLoadReportWhy()
        {
            Assert.Equal(FontEngineError.Invalid_File_Path, FontEngine.LoadFontFace("/nonexistent/font.ttf", 90));
            Assert.Equal(FontEngineError.Invalid_File, FontEngine.LoadFontFace((Font)null));
            Assert.Equal(FontEngineError.Invalid_File, FontEngine.LoadFontFace(new byte[0]));
        }

        [Fact]
        public void ResetClearsAnAtlas()
        {
            var texture = new Texture2D(8, 8, TextureFormat.Alpha8, false);
            FontEngine.ResetAtlasTexture(texture);
            foreach (var c in texture.GetPixels32()) Assert.Equal(0, c.a);
        }
    }
}
