using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Dynamic font glyphs wider than tall are stored a quarter turn round in the font
    /// texture (CharacterInfo.flipped), as the Unity 6000.0.71f1 Editor stores them
    /// (tests/projects/textmesh): the corners CharacterInfo reports and the quads text generation
    /// draws both find the glyph's own pixels there.</summary>
    public class FontAtlasRotationTests
    {
        /// <summary>'W' a 6 x 3 bitmap, 'I' a 2 x 5 one and 'O' a 4 x 4 one, each pixel a distinct
        /// value (10 + its index, top row first).</summary>
        sealed class ShapedGlyphSource : GlyphSource
        {
            internal ShapedGlyphSource()
            {
                UnitsPerEm = 1000;
                Ascender = 800;
                Descender = -200;
                YMax = 900;
                YMin = -250;
                Family = "Shapes";
            }

            internal override bool HasCharacter(int codepoint) => codepoint == 'W' || codepoint == 'I' || codepoint == 'O';

            internal override bool Render(int codepoint, int size, FontStyle style, int renderingMode, out RenderedGlyph glyph)
            {
                glyph = default;
                if (!HasCharacter(codepoint)) return false;
                var (width, height) = codepoint == 'W' ? (6, 3) : codepoint == 'I' ? (2, 5) : (4, 4);
                glyph = new RenderedGlyph
                {
                    Advance = width + 1,
                    Top = height,
                    Width = width,
                    Height = height,
                    Coverage = Enumerable.Range(0, width * height).Select(i => (byte)(10 + i)).ToArray(),
                };
                return true;
            }
        }

        static byte Alpha(Texture2D texture, Vector2 uv, int du, int dv)
        {
            int u = Mathf.RoundToInt(uv.x * texture.width) + du, v = Mathf.RoundToInt(uv.y * texture.height) + dv;
            return texture.GetPixels32()[v * texture.width + u].a;
        }

        [Fact]
        public void OnlyGlyphsWiderThanTallAreFlipped()
        {
            var font = new Font(new ShapedGlyphSource(), 10, "Shapes");
            font.RequestCharactersInTexture("WIO");
#pragma warning disable 618
            Assert.True(font.GetCharacterInfo('W', out var wide));
            Assert.True(wide.flipped);
            Assert.True(font.GetCharacterInfo('I', out var tall));
            Assert.False(tall.flipped);
            Assert.True(font.GetCharacterInfo('O', out var square));
            Assert.False(square.flipped);
            var texture = (Texture2D)font.material.mainTexture;
            // The flipped cell is as tall as the glyph is wide: 3 + 2 padding across, 6 + 2 up.
            Assert.Equal(5, Mathf.RoundToInt(wide.uv.width * texture.width));
            Assert.Equal(8, Mathf.RoundToInt(wide.uv.height * texture.height));
#pragma warning restore 618
            // Its top edge runs up the texture: the corners' u are equal along it.
            Assert.Equal(wide.uvTopLeft.x, wide.uvTopRight.x);
            Assert.Equal(wide.uvBottomLeft.x, wide.uvBottomRight.x);
            // Each corner, one padding pixel in, is that corner's pixel of the glyph.
            Assert.Equal(10, Alpha(texture, wide.uvTopLeft, -2, 1));
            Assert.Equal(10 + 5, Alpha(texture, wide.uvTopRight, -2, -2));
            Assert.Equal(10 + 12, Alpha(texture, wide.uvBottomLeft, 1, 1));
            Assert.Equal(10 + 17, Alpha(texture, wide.uvBottomRight, 1, -2));
            Assert.Equal(10, Alpha(texture, tall.uvTopLeft, 1, -2));
            Assert.Equal(10 + 9, Alpha(texture, tall.uvBottomRight, -2, 1));
        }

        [Fact]
        public void GeneratedQuadsUseTheFlippedCorners()
        {
            var font = new Font(new ShapedGlyphSource(), 10, "Shapes");
            var gen = new TextGenerator();
            gen.Populate("WI", new TextGenerationSettings
            {
                font = font,
                color = Color.white,
                fontSize = 10,
                lineSpacing = 1f,
                scaleFactor = 1f,
                textAnchor = TextAnchor.UpperLeft,
                verticalOverflow = VerticalWrapMode.Overflow,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                generationExtents = new Vector2(100, 50),
                pivot = new Vector2(0.5f, 0.5f),
            });
            var verts = gen.GetVerticesArray();
            foreach (var (quad, c) in new[] { (0, 'W'), (1, 'I') })
            {
                Assert.True(font.GetCharacterInfo(c, out var info));
                // Vertices: top left, top right, bottom right, bottom left.
                Assert.Equal(info.uvTopLeft, (Vector2)verts[quad * 4].uv0);
                Assert.Equal(info.uvTopRight, (Vector2)verts[quad * 4 + 1].uv0);
                Assert.Equal(info.uvBottomRight, (Vector2)verts[quad * 4 + 2].uv0);
                Assert.Equal(info.uvBottomLeft, (Vector2)verts[quad * 4 + 3].uv0);
            }
        }
    }
}
