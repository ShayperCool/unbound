using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>
    /// A box font: units per em 1000, ascender 800, descender -200, no line gap, so at size S a line is
    /// S pixels high with the baseline 0.8 S below its top. Every glyph advances S / 2 and is a solid
    /// (S / 2) x (0.7 S) box on the baseline; 'I' has a left bearing of 2; spaces have no pixels.
    /// </summary>
    internal sealed class BoxGlyphSource : GlyphSource
    {
        internal int Renders;

        internal BoxGlyphSource()
        {
            UnitsPerEm = 1000;
            Ascender = 800;
            Descender = -200;
            LineGap = 0;
            YMax = 900;
            YMin = -250;
            Family = "Box";
        }

        internal override bool HasCharacter(int codepoint) => codepoint != 0x2603;

        internal override bool Render(int codepoint, int size, FontStyle style, out RenderedGlyph glyph)
        {
            glyph = default;
            if (!HasCharacter(codepoint)) return false;
            Renders++;
            int width = size / 2, height = (int)(size * 0.7f + 0.5f);
            if (codepoint == ' ')
            {
                glyph = new RenderedGlyph { Advance = size / 2f };
                return true;
            }
            int left = codepoint == 'I' ? 2 : 0;
            if (codepoint == 'I') width = 1;
            glyph = new RenderedGlyph
            {
                Advance = size / 2f,
                Left = left,
                Top = height,
                Width = width,
                Height = height,
                Coverage = Enumerable.Repeat((byte)255, width * height).ToArray(),
            };
            return true;
        }
    }

    public class TextTests
    {
        static Font BoxFont(int fontSize = 10) => new Font(new BoxGlyphSource(), fontSize, "Box");

        static TextGenerationSettings Settings(Font font, Vector2 extents, TextAnchor anchor = TextAnchor.UpperLeft, int size = 10) => new TextGenerationSettings
        {
            font = font,
            color = Color.white,
            fontSize = size,
            lineSpacing = 1f,
            richText = true,
            scaleFactor = 1f,
            fontStyle = FontStyle.Normal,
            textAnchor = anchor,
            resizeTextMinSize = 1,
            resizeTextMaxSize = 40,
            verticalOverflow = VerticalWrapMode.Overflow,
            horizontalOverflow = HorizontalWrapMode.Wrap,
            generationExtents = extents,
            pivot = new Vector2(0.5f, 0.5f),
        };

        [Fact]
        public void FontMetricsScaleTheFaceAtItsSize()
        {
            var font = BoxFont(10);
            Assert.True(font.dynamic);
            Assert.Equal(10, font.fontSize);
            Assert.Equal(8, font.ascent);
            Assert.Equal(10, font.lineHeight);
            Assert.Equal(new[] { "Box" }, font.fontNames);
            Assert.Equal("Font Material", font.material.name);
            Assert.Same(font.material.mainTexture, font.material.mainTexture);
            Assert.Equal(TextureFormat.Alpha8, ((Texture2D)font.material.mainTexture).format);
            Assert.True(font.HasCharacter('A'));
            Assert.False(font.HasCharacter('☃'));
        }

        [Fact]
        public void EmitsFourVerticesPerVisibleCharacterInUnityOrder()
        {
            var gen = new TextGenerator();
            Assert.True(gen.Populate("AB", Settings(BoxFont(), new Vector2(100, 50))));
            // Upper left of a 100x50 rect around the pivot: x from -50, top at 25, baseline at 17.
            var v = gen.verts;
            Assert.Equal(8, v.Count);
            Assert.Equal(new Vector3(-50, 24, 0), v[0].position); // top left
            Assert.Equal(new Vector3(-45, 24, 0), v[1].position); // top right
            Assert.Equal(new Vector3(-45, 17, 0), v[2].position); // bottom right
            Assert.Equal(new Vector3(-50, 17, 0), v[3].position); // bottom left
            Assert.Equal(new Vector3(-45, 24, 0), v[4].position);
            Assert.True(v[0].uv0.y > v[3].uv0.y && v[1].uv0.x > v[0].uv0.x);
            Assert.Equal(new Color32(255, 255, 255, 255), v[0].color);
            Assert.Equal(3, gen.characterCount);
            Assert.Equal(2, gen.characterCountVisible);
            Assert.Equal(new Vector2(-50, 25), gen.characters[0].cursorPos);
            Assert.Equal(5f, gen.characters[0].charWidth);
            Assert.Equal(new Vector2(-40, 25), gen.characters[2].cursorPos);
            Assert.Single(gen.lines);
            Assert.Equal(0, gen.lines[0].startCharIdx);
            Assert.Equal(10, gen.lines[0].height);
            Assert.Equal(25f, gen.lines[0].topY);
        }

        [Fact]
        public void WhitespaceAdvancesWithoutVertices()
        {
            var gen = new TextGenerator();
            gen.Populate("A B", Settings(BoxFont(), new Vector2(100, 50)));
            Assert.Equal(8, gen.vertexCount);
            Assert.Equal(4, gen.characterCount);
            Assert.Equal(-40f, gen.verts[4].position.x);
        }

        [Fact]
        public void WrapsAfterTheLastSpaceThatFits()
        {
            var gen = new TextGenerator();
            // 10 characters of 5 pixels fit in 50.
            gen.Populate("AAAA BBBB CCCC", Settings(BoxFont(), new Vector2(50, 100)));
            Assert.Equal(new[] { 0, 10 }, gen.lines.Select(l => l.startCharIdx));
            Assert.Equal(gen.lines[0].topY - 10, gen.lines[1].topY);
            // The second line starts at the left again.
            int firstOfLine2 = 4 * 8;
            Assert.Equal(-25f, gen.verts[firstOfLine2].position.x);
        }

        [Fact]
        public void BreaksWordsLongerThanTheLine()
        {
            var gen = new TextGenerator();
            gen.Populate("AAAAAAAAAAAA", Settings(BoxFont(), new Vector2(30, 100)));
            Assert.Equal(new[] { 0, 6 }, gen.lines.Select(l => l.startCharIdx));
        }

        [Fact]
        public void NewlinesBreakLinesAndOverflowDoesNotWrap()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(10, 100));
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            gen.Populate("AAAA BBBB\nCC\n", s);
            Assert.Equal(new[] { 0, 10, 13 }, gen.lines.Select(l => l.startCharIdx));
            Assert.Equal(14, gen.characterCount);
        }

        [Fact]
        public void AlignsLinesAndTheBlock()
        {
            var gen = new TextGenerator();
            gen.Populate("AB", Settings(BoxFont(), new Vector2(100, 50), TextAnchor.MiddleCenter));
            // Block of 10x10 centered: x from -5, top at 5.
            Assert.Equal(new Vector3(-5, 4, 0), gen.verts[0].position);
            Assert.Equal(5f, gen.lines[0].topY);

            gen.Populate("AB", Settings(BoxFont(), new Vector2(100, 50), TextAnchor.LowerRight));
            Assert.Equal(new Vector3(40, -16, 0), gen.verts[0].position);
            Assert.Equal(new Vector3(50, -23, 0), gen.verts[6].position);
        }

        [Fact]
        public void AlignsByGeometryWhenAsked()
        {
            var font = BoxFont();
            var gen = new TextGenerator();
            var s = Settings(font, new Vector2(100, 50));
            gen.Populate("IA", s);
            Assert.Equal(-48f, gen.verts[0].position.x);
            s.alignByGeometry = true;
            gen.Populate("IA", s);
            Assert.Equal(-50f, gen.verts[0].position.x);
        }

        [Fact]
        public void TruncatesLinesThatDoNotFit()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(10, 25));
            s.verticalOverflow = VerticalWrapMode.Truncate;
            gen.Populate("AA\nBB\nCC", s);
            Assert.Equal(2, gen.lineCount);
            Assert.Equal(16, gen.vertexCount);
            Assert.Equal(7, gen.characterCount);
            s.verticalOverflow = VerticalWrapMode.Overflow;
            gen.Populate("AA\nBB\nCC", s);
            Assert.Equal(3, gen.lineCount);

            // Not even one line fits: nothing is drawn.
            s.verticalOverflow = VerticalWrapMode.Truncate;
            s.generationExtents = new Vector2(10, 9);
            gen.Populate("AA", s);
            Assert.Equal(0, gen.vertexCount);
            Assert.Equal(0, gen.characterCountVisible);
        }

        [Fact]
        public void LineSpacingSpreadsLinesButNotTheLastLine()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(100, 100));
            s.lineSpacing = 2f;
            gen.Populate("A\nB", s);
            Assert.Equal(30f, gen.GetPreferredHeight("A\nB", s));
            gen.Populate("A\nB", s);
            Assert.Equal(gen.lines[0].topY - 20, gen.lines[1].topY);
            Assert.Equal(20, gen.lines[0].height);
            Assert.Equal(10f, gen.lines[0].leading);
        }

        [Fact]
        public void PreferredSizeIsTheTextBlock()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(100, 0));
            Assert.Equal(15f, gen.GetPreferredWidth("AB\nABC", s));
            Assert.Equal(20f, gen.GetPreferredHeight("AB\nABC", s));
            // The preferred height wraps at the given width.
            s.generationExtents = new Vector2(10, 0);
            Assert.Equal(30f, gen.GetPreferredHeight("AB\nABC", s));
            s.generationExtents = new Vector2(100, 0);
            // Scale factor: pixels, which Text divides by its pixelsPerUnit.
            s.scaleFactor = 2f;
            Assert.Equal(30f, gen.GetPreferredWidth("AB\nABC", s));
            Assert.Equal(10f, gen.GetPreferredHeight("", Settings(BoxFont(), Vector2.zero)));
        }

        [Fact]
        public void BestFitPicksTheLargestSizeThatFits()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(50, 100));
            s.resizeTextForBestFit = true;
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            gen.Populate("AAAAAAAAAA", s);
            Assert.Equal(10, gen.fontSizeUsedForBestFit);
            Assert.Equal(new Vector3(-20, 50 - 1, 0), gen.verts[4].position);

            // Wrapped: the height decides.
            s.horizontalOverflow = HorizontalWrapMode.Wrap;
            s.generationExtents = new Vector2(100, 30);
            gen.Populate("AAAA AAAA AAAA AAAA", s);
            Assert.True(gen.fontSizeUsedForBestFit <= 15);
            Assert.True(gen.rectExtents.height <= 30);
        }

        [Fact]
        public void RichTextTagsStyleAndAreHidden()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(200, 50));
            gen.Populate("<b>A</b>B<color=red>C</color><color=#00ff0080>D</color>", s);
            Assert.Equal(16, gen.vertexCount);
            Assert.Equal("<b>A</b>B<color=red>C</color><color=#00ff0080>D</color>".Length + 1, gen.characterCount);
            Assert.Equal(new Color32(255, 0, 0, 255), gen.verts[8].color);
            Assert.Equal(new Color32(0, 255, 0, 128), gen.verts[12].color);
            // Tags take no room: B follows A.
            Assert.Equal(gen.verts[0].position.x + 5, gen.verts[4].position.x);

            // Unclosed or unknown tags are text.
            gen.Populate("<b>A", s);
            Assert.Equal(16, gen.vertexCount);
            gen.Populate("<x>A</x>", s);
            Assert.Equal(32, gen.vertexCount);
            s.richText = false;
            gen.Populate("<b>A</b>", s);
            Assert.Equal(32, gen.vertexCount);
        }

        [Fact]
        public void SizeTagsMakeTheLineTaller()
        {
            var gen = new TextGenerator();
            var s = Settings(BoxFont(), new Vector2(200, 100));
            Assert.Equal(20f, gen.GetPreferredHeight("<size=20>A</size>B", s));
            gen.Populate("<size=20>A</size>B", s);
            // Both sit on one baseline, 16 below the top (the larger ascent).
            Assert.Equal(gen.verts[3].position.y, gen.verts[7].position.y);
            Assert.Equal(gen.lines[0].topY - 16, gen.verts[3].position.y);
            Assert.Equal(14f, gen.verts[0].position.y - gen.verts[3].position.y);
        }

        [Fact]
        public void CachesUntilTheInputChangesOrIsInvalidated()
        {
            var source = new BoxGlyphSource();
            var font = new Font(source, 10, "Box");
            var gen = new TextGenerator();
            var s = Settings(font, new Vector2(100, 50));
            gen.Populate("AB", s);
            var verts = gen.GetVerticesArray();
            Assert.True(gen.Populate("AB", s));
            Assert.Equal(verts, gen.GetVerticesArray());
            s.color = Color.red;
            gen.Populate("AB", s);
            Assert.Equal(new Color32(255, 0, 0, 255), gen.verts[0].color);
            Assert.True(new TextGenerationSettings { pivot = Vector2.one }.Equals(new TextGenerationSettings { pivot = Vector2.one }));
            Assert.False(new TextGenerationSettings { fontSize = 1 }.Equals(new TextGenerationSettings { fontSize = 2 }));
        }

        [Fact]
        public void RequestedCharactersHaveCharacterInfo()
        {
            var font = BoxFont();
            Assert.False(font.GetCharacterInfo('A', out _, 10));
            font.RequestCharactersInTexture("A ", 10);
            Assert.True(font.GetCharacterInfo('A', out var info, 10));
            Assert.Equal('A', info.index);
            Assert.Equal(10, info.size);
            Assert.Equal(5, info.advance);
            Assert.Equal(5, info.glyphWidth);
            Assert.Equal(7, info.glyphHeight);
            Assert.Equal(0, info.bearing);
            Assert.Equal(0, info.minY);
            Assert.Equal(7, info.maxY);
            Assert.Equal(0, info.minX);
            Assert.Equal(5, info.maxX);
            Assert.True(info.uvTopRight.x > info.uvBottomLeft.x && info.uvTopRight.y > info.uvBottomLeft.y);
            Assert.True(font.GetCharacterInfo(' ', out var space, 10));
            Assert.Equal(5, space.advance);
            Assert.False(font.GetCharacterInfo('A', out _, 10, FontStyle.Bold));
            Assert.Contains(font.characterInfo, c => c.index == 'A');
        }

        [Fact]
        public void CharacterInfoPropertiesEditTheRects()
        {
            var info = new CharacterInfo();
            info.minX = 1;
            info.maxX = 6;
            info.maxY = 8;
            info.minY = -2;
            Assert.Equal(1, info.bearing);
            Assert.Equal(5, info.glyphWidth);
            Assert.Equal(10, info.glyphHeight);
            info.advance = 7;
            Assert.Equal(7, info.advance);
            info.uvBottomLeft = new Vector2(0.1f, 0.2f);
            info.uvTopRight = new Vector2(0.3f, 0.5f);
            Assert.Equal(new Vector2(0.1f, 0.5f), info.uvTopLeft);
            Assert.Equal(new Vector2(0.3f, 0.2f), info.uvBottomRight);
        }

        [Fact]
        public void AFullTextureGrowsAndRaisesTextureRebuilt()
        {
            var font = BoxFont();
            var rebuilt = new List<Font>();
            void OnRebuilt(Font f) => rebuilt.Add(f);
            Font.textureRebuilt += OnRebuilt;
            try
            {
                // 50x70 glyphs (+1 padding): 12 fit in 256x256.
                font.RequestCharactersInTexture("ABCDEFGHIJKL", 100);
                Assert.Empty(rebuilt);
                font.RequestCharactersInTexture("MNOPQRSTUVWX", 100);
                Assert.Equal(new[] { font }, rebuilt);
                var texture = (Texture2D)font.material.mainTexture;
                Assert.Equal(512, texture.width);
                Assert.Equal(256, texture.height);
                // Everything requested so far is still there.
                Assert.True(font.GetCharacterInfo('A', out _, 100));
                Assert.True(font.GetCharacterInfo('X', out _, 100));
            }
            finally
            {
                Font.textureRebuilt -= OnRebuilt;
            }
        }

        [Fact]
        public void MissingCharactersTakeNoRoom()
        {
            var gen = new TextGenerator();
            gen.Populate("A☃B", Settings(BoxFont(), new Vector2(100, 50)));
            Assert.Equal(8, gen.vertexCount);
            Assert.Equal(-45f, gen.verts[4].position.x);
        }

        [Fact]
        public void NoFontIsAnError()
        {
            var gen = new TextGenerator();
            Assert.False(gen.Populate("A", new TextGenerationSettings()));
            Assert.Equal(0, gen.vertexCount);
        }

        [Fact]
        public void SurrogatePairsAreOneCharacter()
        {
            var gen = new TextGenerator();
            gen.Populate("A\U0001F600B", Settings(BoxFont(), new Vector2(100, 50)));
            Assert.Equal(12, gen.vertexCount);
            Assert.Equal(5, gen.characterCount);
            Assert.Equal(0f, gen.characters[2].charWidth);
        }
    }
}
