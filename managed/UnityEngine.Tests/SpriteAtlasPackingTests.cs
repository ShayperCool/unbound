using System;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A sprite bound to a packed atlas page (UVs mirrored as the packer flipped it, a variant's
    /// scale), late binding through SpriteAtlasManager, and Sprites.DataUtility's rect rules (the
    /// values the 6000.0.71f1 Editor gives in tests/projects/spriteatlas; the sprites here are
    /// stand-ins of their shapes).
    /// </summary>
    public class SpriteAtlasPackingTests
    {
        static Sprite Quad() => Sprite.Create(new Texture2D(8, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);

        [Fact]
        public void BindingMovesTheUvsToThePageAndMirrorsFlippedSprites()
        {
            var page = new Texture2D(16, 8);
            // The quad's left edge is at -2 units; the page rect starts at 10, so the UV transform's
            // offset is 12 (the pivot 2 pixels in).
            var transform = new Vector4(1f, 12f, 1f, 4f);
            var plain = Quad();
            plain.BindAtlas(page, new Rect(10, 2, 4, 4), transform, SpritePackingMode.Rectangle, SpritePackingRotation.None, new Vector2(16, 8), 1f);
            Assert.True(plain.packed);
            Assert.Same(page, plain.texture);
            Assert.Equal(new Rect(10, 2, 4, 4), plain.textureRect);
            Assert.Equal(10f / 16f, plain.uv[0].x);
            Assert.Equal(6f / 8f, plain.uv[0].y);
            var flipped = Quad();
            flipped.BindAtlas(page, new Rect(10, 2, 4, 4), transform, SpritePackingMode.Tight, SpritePackingRotation.Rotate180, new Vector2(16, 8), 1f);
            Assert.Equal(SpritePackingMode.Tight, flipped.packingMode);
            Assert.Equal(SpritePackingRotation.Rotate180, flipped.packingRotation);
            // Mirrored within the page rect on both axes: the top-left corner samples the bottom-right.
            Assert.Equal(14f / 16f, flipped.uv[0].x, 6);
            Assert.Equal(2f / 8f, flipped.uv[0].y, 6);
            // A variant's half-size page keeps the master's rects and UVs.
            var half = Quad();
            half.BindAtlas(new Texture2D(8, 4), new Rect(10, 2, 4, 4), transform, SpritePackingMode.Rectangle, SpritePackingRotation.None, new Vector2(16, 8), 0.5f);
            Assert.Equal(plain.uv, half.uv);
            Assert.Equal(0.5f, half.spriteAtlasTextureScale);
            Assert.Equal(UnityEngine.Sprites.DataUtility.GetOuterUV(plain), UnityEngine.Sprites.DataUtility.GetOuterUV(half));
        }

        [Fact]
        public void LateBindingSpritesHaveNoTextureAndZeroUvs()
        {
            var sprite = Quad();
            sprite.UnbindTexture();
            Assert.Null(sprite.texture);
            Assert.All(sprite.uv, uv => Assert.Equal(Vector2.zero, uv));
            Assert.False(sprite.packed);
            Assert.Equal(new Vector4(0, 0, 1, 1), UnityEngine.Sprites.DataUtility.GetOuterUV(sprite));
            Assert.Equal(new Vector4(0, 0, 1, 1), UnityEngine.Sprites.DataUtility.GetInnerUV(sprite));
        }

        [Fact]
        public void AtlasRequestsReachListenersAndRegisterTheGivenAtlas()
        {
            Assert.False(SpriteAtlasManager.RequestAtlas("Nobody"));
            string requested = null;
            SpriteAtlas registered = null;
            Action<string, Action<SpriteAtlas>> listener = (tag, register) => { requested = tag; register(null); };
            Action<SpriteAtlas> onRegistered = atlas => registered = atlas;
            SpriteAtlasManager.atlasRequested += listener;
            SpriteAtlasManager.atlasRegistered += onRegistered;
            try
            {
                Assert.True(SpriteAtlasManager.RequestAtlas("Late"));
                Assert.Equal("Late", requested);
                Assert.Null(registered);
            }
            finally
            {
                SpriteAtlasManager.atlasRequested -= listener;
                SpriteAtlasManager.atlasRegistered -= onRegistered;
            }
        }

        [Fact]
        public void PaddingAndOuterUvFollowTheTextureRect()
        {
            // A sprite whose texture rect is its rect: no padding, the rect's UVs.
            var sprite = Quad();
            Assert.Equal(Vector4.zero, UnityEngine.Sprites.DataUtility.GetPadding(sprite));
            Assert.Equal(new Vector4(0, 0, 0.5f, 1f), UnityEngine.Sprites.DataUtility.GetOuterUV(sprite));
        }
    }
}
