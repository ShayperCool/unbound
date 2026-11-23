using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class SpriteGeometryMemoryTests
    {
        [Fact]
        public void GeneratedPhysicsAndSecondaryTexturesRemainOutsideTheMeasuredLayout()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var texture = new Texture2D(4, 4);
            var rect = new Rect(0, 0, 4, 4);
            var ordinary = Sprite.Create(texture, rect, Vector2.zero);
            var fallback = Sprite.Create(texture, rect, Vector2.zero, 100f, 0, SpriteMeshType.FullRect, Vector4.zero, true);
            var secondary = Sprite.Create(texture, rect, Vector2.zero, 100f, 0, SpriteMeshType.FullRect, Vector4.zero, false,
                new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = texture } });
            try
            {
                Assert.Equal(1460L, MemorySizes.RuntimeSize(ordinary));
                Assert.NotNull(fallback);
                Assert.NotNull(secondary);
                Assert.Null(MemorySizes.RuntimeSize(fallback));
                Assert.Null(MemorySizes.RuntimeSize(secondary));
            }
            finally
            {
                Object.DestroyImmediate(secondary);
                Object.DestroyImmediate(fallback);
                Object.DestroyImmediate(ordinary);
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void ACloneOfAPersistentSpriteKeepsTheUnmeasuredLayout()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var texture = new Texture2D(4, 4);
            var source = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            Sprite clone = null;
            try
            {
                Assert.Equal(1460L, MemorySizes.RuntimeSize(source));
                source.IsPersistentAsset = true;
                clone = Object.Instantiate(source);
                Assert.NotNull(clone);
                Assert.Null(MemorySizes.RuntimeSize(source));
                Assert.Null(MemorySizes.RuntimeSize(clone));
            }
            finally
            {
                source.IsPersistentAsset = false;
                Object.DestroyImmediate(clone);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void AnUnmeasuredFamilyRetainsTheUnknownSpriteSize()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
            try
            {
                Assert.NotNull(sprite);
                Assert.Null(MemorySizes.RuntimeSize(sprite));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
