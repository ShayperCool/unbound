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
    /// no rebuild is announced. On the next frame, unused old glyphs are evicted on a rebuild
    /// (legacy/full/next).</summary>
    [Collection("Frames")]
    public class FontAtlasFullTests
    {
        /// <summary>Square glyphs as large as the size asked for, for printable ASCII.</summary>
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

            internal override bool HasCharacter(int codepoint) => codepoint >= '!' && codepoint <= '~';

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
                // At 500 pixels a 4096 x 4096 texture fits at most 64 of these squares.
                const int size = 500;
                string initial = new string(Enumerable.Range('!', 80).Select(codepoint => (char)codepoint).ToArray());
                font.RequestCharactersInTexture(initial, size);
                var texture = font.material.mainTexture;
                Assert.Equal((4096, 4096), (texture.width, texture.height));
                Assert.Equal(new[] { Full }, logs.Lines);
                Assert.True(font.GetCharacterInfo(initial[0], out _, size));
                Assert.False(font.GetCharacterInfo(initial[^1], out _, size));
                int full = rebuilt;
                // Again in the same frame: the error again, nothing rebuilt.
                font.RequestCharactersInTexture(initial[^1].ToString(), size);
                Assert.Equal(new[] { Full, Full }, logs.Lines);
                Assert.Equal(full, rebuilt);
                Assert.True(font.GetCharacterInfo(initial[0], out _, size));
                // A later frame: the older frame's glyphs make room (a reset and its rebuild event).
                Time.s_FrameCount = 101;
                font.RequestCharactersInTexture("uvwxyz", size);
                Assert.True(rebuilt > full);
                Assert.True(font.GetCharacterInfo('z', out _, size));
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
