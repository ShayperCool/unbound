using Unbound.Tests;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Project release")]
    public sealed class FontRequestSizeTests
    {
        [Fact]
        public void OversizedRequestsShareTheMaximumSizedGlyphAndReportTheirOwnSize()
        {
            using var pinned = TestRelease.Pin(TestRelease.Measured);
            var source = new BoxGlyphSource();
            var font = new Font(source, 10, "Box");
            try
            {
                font.RequestCharactersInTexture("A", 501, FontStyle.Bold);
                Assert.False(font.GetCharacterInfo('A', out _, 499, FontStyle.Bold));
                Assert.False(font.GetCharacterInfo('A', out _, 500, FontStyle.Normal));
                Assert.True(font.GetCharacterInfo('A', out var limit, 500, FontStyle.Bold));

                int renders = source.Renders;
                font.RequestCharactersInTexture("A", int.MaxValue, FontStyle.Bold);
                Assert.Equal(renders, source.Renders);
                Assert.True(font.GetCharacterInfo('A', out var oversized, int.MaxValue, FontStyle.Bold));
                Assert.Equal(int.MaxValue, oversized.size);
                Assert.Equal(limit.advance, oversized.advance);
                Assert.Equal(limit.uvBottomLeft, oversized.uvBottomLeft);
                Assert.Equal(limit.uvTopRight, oversized.uvTopRight);
            }
            finally { Object.DestroyImmediate(font); }
        }

        [Fact]
        public void UnmeasuredFamilyRetainsDistinctAtlasSize()
        {
            using var pinned = TestRelease.Pin(TestRelease.Unmeasured);
            var font = new Font(new BoxGlyphSource(), 10, "Box");
            try
            {
                font.RequestCharactersInTexture("A", 501);
                Assert.False(font.GetCharacterInfo('A', out _, 500));
                Assert.True(font.GetCharacterInfo('A', out _, 501));
            }
            finally { Object.DestroyImmediate(font); }
        }
    }
}
