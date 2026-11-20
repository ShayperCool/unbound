using Unbound.Tests;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Project release")]
    public sealed class FontAtlasHistoryTests
    {
        [Theory]
        [InlineData(TestRelease.Measured, true)]
        [InlineData(TestRelease.Unmeasured, false)]
        public void ReadGlyphSurvivesSameFrameRebuildOnlyInMeasuredFamily(string release, bool readSurvives)
        {
            using var pinned = TestRelease.Pin(release);
            var font = new Font(new BoxGlyphSource(), 10, "Box");
            const int size = 128;
            int frame = Time.s_FrameCount;
            try
            {
                font.RequestCharactersInTexture("ABCD", size);
                var atlas = font.Atlas;
                Assert.Equal(256, atlas.Texture.width);
                Assert.True(font.GetCharacterInfo('C', out _, size));

                Time.s_FrameCount++;
                font.RequestCharactersInTexture("B", size);
                Assert.True(font.GetCharacterInfo('A', out _, size));
                int rebuilds = atlas.RebuildCount;
                font.RequestCharactersInTexture("E", size);

                Assert.Equal(rebuilds + 1, atlas.RebuildCount);
                Assert.Equal(256, atlas.Texture.width);
                Assert.Equal(readSurvives, font.GetCharacterInfo('A', out _, size));
                Assert.True(font.GetCharacterInfo('B', out _, size));
                Assert.False(font.GetCharacterInfo('C', out _, size));
                Assert.True(font.GetCharacterInfo('E', out _, size));
            }
            finally
            {
                Time.s_FrameCount = frame;
                Object.DestroyImmediate(font);
            }
        }
    }
}
