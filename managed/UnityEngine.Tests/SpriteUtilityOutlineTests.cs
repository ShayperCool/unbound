using UnityEditor.Sprites;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Editor sprite utilities (Unity 6000.0.71f1, tests/projects/spriteutility).</summary>
    public class SpriteUtilityOutlineTests
    {
        [Fact]
        public void PolygonCornersStartAtTheTopAndTurnCounterClockwise()
        {
            var square = SpriteUtility.GeneratePolygonOutlineVerticesOfSize(4, 64, 64);
            Assert.Equal(4, square.Length);
            Assert.Equal(new Vector2(0f, 32f), square[0]);
            Assert.Equal(-32f, square[1].x, 4);
            Assert.Equal(-32f, square[2].y, 4);
            Assert.Equal(32f, square[3].x, 4);
            var box = SpriteUtility.GeneratePolygonOutlineVerticesOfSize(0, 100, 50);
            Assert.Equal(new[] { new Vector2(-50f, -25f), new Vector2(50f, -25f), new Vector2(50f, 25f), new Vector2(-50f, 25f) }, box);
            var many = SpriteUtility.GeneratePolygonOutlineVerticesOfSize(128, 100, 50);
            // The angle is summed per corner in single precision: the Editor's corner 127 is at
            // 2.453239 (a product of the step and the index would give 2.453382).
            Assert.Equal(2.453239f, many[127].x, 5);
            Assert.Single(SpriteUtility.GeneratePolygonOutlineVerticesOfSize(1, 10, 10));
        }

        [Fact]
        public void AtlasDataOfASpriteThatIsNotPackedThrows()
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
            try
            {
                Assert.Same(texture, SpriteUtility.GetSpriteTexture(sprite, false));
                Assert.Equal(sprite.uv, SpriteUtility.GetSpriteUVs(sprite, false));
                Assert.Equal("Could not get atlas data - Sprite is not packed.", Assert.Throws<System.ArgumentException>(() => SpriteUtility.GetSpriteTexture(sprite, true)).Message);
                Assert.Throws<System.ArgumentException>(() => SpriteUtility.GetSpriteUVs(sprite, true));
                Assert.Throws<System.ArgumentNullException>(() => SpriteUtility.GetSpriteTexture(null, false));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
