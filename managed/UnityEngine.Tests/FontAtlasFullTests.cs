using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>A dynamic font texture at its largest size (4096 x 4096). Measured in the Unity
    /// 6000.0.71f1 Editor (tests/projects/textcolor legacy/full, every character asked for in one
    /// frame): the texture keeps what it holds, each request that does not fit logs Unity's error, and
    /// no rebuild is announced. Unbound's choice, unmeasured: glyphs of older frames are still evicted
    /// by a reset (with its rebuild event) to make room.</summary>
    [Collection("Frames")]
    public class FontAtlasFullTests
    {
        /// <summary>Square glyphs as large as the size asked for, for the letters A to Z.</summary>
        sealed class Squares : GlyphSource
        {
            internal Squares()
            {
                UnitsPerEm = 1000;
                Ascender = 800;
                Descender = -200;
                YMax = 900;
                YMin = -250;
                Family = "Squares";
            }

            internal override bool HasCharacter(int codepoint) => codepoint >= 'A' && codepoint <= 'Z';

            internal override bool Render(int codepoint, int size, FontStyle style, int renderingMode, out RenderedGlyph glyph)
            {
                glyph = default;
                if (!HasCharacter(codepoint)) return false;
                glyph = new RenderedGlyph { Advance = size + 1, Top = size, Width = size, Height = size, Coverage = Enumerable.Repeat((byte)200, size * size).ToArray() };
                return true;
            }
        }

        sealed class Logs : ILogHandler
        {
            public readonly List<string> Lines = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        const string Full = "Failed to update dynamic font (Squares) texture; all the needed characters do not fit onto a single texture (max size 4096). Try using less text or a smaller font size.";

        [Fact]
        public void AFullTextureKeepsItsGlyphsInOneFrameAndEvictsOlderOnes()
        {
            var handler = Debug.unityLogger.logHandler;
            int frame = Time.s_FrameCount;
            var logs = new Logs();
            int rebuilt = 0;
            Font font = null;
            Action<Font> counted = f => { if (f == font) rebuilt++; };
            Debug.unityLogger.logHandler = logs;
            Font.textureRebuilt += counted;
            try
            {
                Time.s_FrameCount = 100;
                font = new Font(new Squares(), 10, "Squares");
                // Twenty 1000-pixel squares: more than a 4096 x 4096 texture holds.
                font.RequestCharactersInTexture("ABCDEFGHIJKLMNOPQRST", 1000);
                var texture = font.material.mainTexture;
                Assert.Equal((4096, 4096), (texture.width, texture.height));
                Assert.Equal(new[] { Full }, logs.Lines);
                Assert.True(font.GetCharacterInfo('A', out _, 1000));
                Assert.False(font.GetCharacterInfo('T', out _, 1000));
                int full = rebuilt;
                // Again in the same frame: the error again, nothing rebuilt.
                font.RequestCharactersInTexture("T", 1000);
                Assert.Equal(new[] { Full, Full }, logs.Lines);
                Assert.Equal(full, rebuilt);
                Assert.True(font.GetCharacterInfo('A', out _, 1000));
                // A later frame: the older frame's glyphs make room (a reset and its rebuild event).
                Time.s_FrameCount = 101;
                font.RequestCharactersInTexture("UVWXYZ", 1000);
                Assert.True(rebuilt > full);
                Assert.True(font.GetCharacterInfo('Z', out _, 1000));
                Assert.Equal(2, logs.Lines.Count);
            }
            finally
            {
                Font.textureRebuilt -= counted;
                Debug.unityLogger.logHandler = handler;
                Time.s_FrameCount = frame;
            }
        }
    }
}
