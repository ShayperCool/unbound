using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The texels a runtime sprite trace reads and Sprite.OverridePhysicsShape's rect
    /// space (tests/projects/spriteoutlines records both in the Unity 6000.0.71f1 Editor).</summary>
    public class SpriteTracingTests
    {
        [Fact]
        public void AWindowReadsTheTexelsGetPixels32Gives()
        {
            var texture = new Texture2D(5, 3, TextureFormat.RGBA32, false);
            var pixels = new Color32[15];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)i, (byte)(i * 3), 7, (byte)(255 - i));
            texture.SetPixels32(pixels);
            var window = texture.GetPixels32Window(1, 1, 3, 2);
            Assert.Equal(6, window.Length);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                    Assert.Equal(pixels[(y + 1) * 5 + x + 1], window[y * 3 + x]);
            Assert.Empty(texture.GetPixels32Window(5, 3, 0, 0));
            Assert.ThrowsAny<System.Exception>(() => texture.GetPixels32Window(3, 0, 3, 1));
        }

        [Fact]
        public void TexturesMadeByScriptsAreNotTheirImport()
        {
            var texture = new Texture2D(4, 4);
            Assert.False(texture.TexelsAsImported);
        }

        [Fact]
        public void OverriddenPhysicsShapesMoveFromRectSpaceToUnitsFromThePivot()
        {
            var sprite = Sprite.Create(new Texture2D(64, 64), new Rect(2, 3, 40, 42), new Vector2(0.25f, 0.75f), 64f, 1, SpriteMeshType.FullRect);
            sprite.OverridePhysicsShape(new List<Vector2[]> { new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(-5, 50) } });
            Assert.Equal(1, sprite.GetPhysicsShapeCount());
            var points = new List<Vector2>();
            sprite.GetPhysicsShape(0, points);
            Assert.Equal(new Vector2(-10f / 64f, -31.5f / 64f), points[0]);
            Assert.Equal(new Vector2(30f / 64f, -31.5f / 64f), points[1]);
            Assert.Equal(new Vector2(-15f / 64f, 18.5f / 64f), points[2]);
            // A short path anywhere throws before any path is set; no paths clear the shape.
            var mixed = new List<Vector2[]> { new[] { Vector2.zero, Vector2.right, Vector2.up }, new[] { Vector2.one } };
            var e = Assert.Throws<System.ArgumentException>(() => sprite.OverridePhysicsShape(mixed));
            Assert.Equal("Physics Shape at 1 has less than 3 vertices (1).", e.Message);
            Assert.Equal(1, sprite.GetPhysicsShapeCount());
            Assert.Throws<System.ArgumentException>(() => sprite.OverridePhysicsShape(new List<Vector2[]> { null }));
            Assert.Equal("physicsShapes", Assert.Throws<System.ArgumentNullException>(() => sprite.OverridePhysicsShape(null)).ParamName);
            sprite.OverridePhysicsShape(new List<Vector2[]>());
            Assert.Equal(0, sprite.GetPhysicsShapeCount());
        }
    }
}
