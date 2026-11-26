using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>QualitySettings.globalTextureMipmapLimit and the textures it limits (Unity
    /// 6000.0.71f1, tests/projects/mipmaplimit).</summary>
    public class TextureMipmapLimitTests
    {
        [Theory]
        // A texture drops at most its last-but-one mip and keeps a smaller side of 8 texels.
        [InlineData(64, 64, 7, 3)]
        [InlineData(16, 16, 5, 1)]
        [InlineData(8, 8, 4, 0)]
        [InlineData(4, 4, 3, 0)]
        [InlineData(2, 2, 2, 0)]
        [InlineData(64, 64, 3, 2)]
        [InlineData(64, 64, 5, 3)]
        [InlineData(64, 8, 7, 0)]
        [InlineData(8, 64, 7, 0)]
        [InlineData(64, 64, 1, 0)]
        public void ATextureDropsAtMostItsMipsAboveEightTexels(int width, int height, int mips, int expected)
        {
            Assert.Equal(expected, Texture.MaxMipmapLimit(width, height, mips));
        }

        static void WithLimit(int limit, System.Action body)
        {
            int previous = QualitySettings.globalTextureMipmapLimit;
            try
            {
                QualitySettings.globalTextureMipmapLimit = limit;
                body();
            }
            finally
            {
                QualitySettings.globalTextureMipmapLimit = previous;
            }
        }

        [Fact]
        public void TheActiveLimitIsTheGlobalOneWithinTheTexturesOwn()
        {
            var limited = new Texture2D(64, 64, TextureFormat.RGBA32, true) { ignoreMipmapLimit = false };
            var ignoring = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            var plain = new Texture2D(64, 64, TextureFormat.RGBA32, false) { ignoreMipmapLimit = false };
            try
            {
                Assert.True(ignoring.ignoreMipmapLimit);
                WithLimit(2, () =>
                {
                    Assert.Equal(2, limited.activeMipmapLimit);
                    Assert.Equal(0, ignoring.activeMipmapLimit);
                    Assert.Equal(0, plain.activeMipmapLimit);
                    // The size stays the full one.
                    Assert.Equal(64, limited.width);
                });
                WithLimit(7, () => Assert.Equal(3, limited.activeMipmapLimit));
            }
            finally
            {
                Object.DestroyImmediate(limited);
                Object.DestroyImmediate(ignoring);
                Object.DestroyImmediate(plain);
            }
        }

        [Fact]
        public void ANegativeLimitReadsZero()
        {
            WithLimit(-1, () => Assert.Equal(0, QualitySettings.globalTextureMipmapLimit));
        }

        [Fact]
        public void TheIgnoreFlagOfAnUnreadableTextureCannotBeSet()
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, true) { name = "locked" };
            var array = new Texture2DArray(16, 16, 2, TextureFormat.RGBA32, true) { name = "lockedArray" };
            try
            {
                texture.Apply(false, true);
                array.Apply(false, true);
                var e = Assert.Throws<UnityException>(() => texture.ignoreMipmapLimit = false);
                Assert.Equal("Failed to toggle ignoreMipmapLimit, Texture 'locked' is not readable. You can make the texture readable in the Texture Import Settings.", e.Message);
                Assert.True(texture.ignoreMipmapLimit);
                e = Assert.Throws<UnityException>(() => array.ignoreMipmapLimit = false);
                Assert.Equal("Failed to toggle ignoreMipmapLimit, Texture 'lockedArray' is not readable. You can make the texture readable in the Texture Import Settings.", e.Message);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(array);
            }
        }

        [Fact]
        public void AnArrayFollowsTheLimitLikeA2DTexture()
        {
            var array = new Texture2DArray(64, 64, 2, TextureFormat.RGBA32, true);
            try
            {
                Assert.True(array.ignoreMipmapLimit);
                WithLimit(3, () =>
                {
                    Assert.Equal(0, array.activeMipmapLimit);
                    array.ignoreMipmapLimit = false;
                    Assert.Equal(3, array.activeMipmapLimit);
                });
            }
            finally
            {
                Object.DestroyImmediate(array);
            }
        }

        [Fact]
        public void TexturesOwnLimitIsTheActiveOneWithoutChangingTheLevels()
        {
            var limited = new Texture2D(64, 64, TextureFormat.RGBA32, true) { ignoreMipmapLimit = false };
            try
            {
                WithLimit(1, () =>
                {
#pragma warning disable CS0618
                    Assert.Equal(1, Texture.masterTextureLimit);
                    Texture.masterTextureLimit = 3;
                    Assert.Equal(1, QualitySettings.globalTextureMipmapLimit);
                    Assert.Equal(3, Texture.globalMipmapLimit);
                    Assert.Equal(3, limited.activeMipmapLimit);
                    QualitySettings.globalTextureMipmapLimit = 0;
                    Assert.Equal(0, Texture.masterTextureLimit);
#pragma warning restore CS0618
                    Assert.Equal(0, limited.activeMipmapLimit);
                });
            }
            finally
            {
                Object.DestroyImmediate(limited);
            }
        }

        [Fact]
        public void ATextureMadeUnreadableKeepsTheMipsItsLastUploadDropped()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true) { ignoreMipmapLimit = false };
            try
            {
                WithLimit(2, () =>
                {
                    Assert.Equal(-1, texture.FrozenMipmapLimit);
                    texture.Apply(false, true);
                    Assert.Equal(2, texture.FrozenMipmapLimit);
                    QualitySettings.globalTextureMipmapLimit = 0;
                    // The answer follows the limit; what is sampled does not.
                    Assert.Equal(0, texture.activeMipmapLimit);
                    Assert.True(texture.UnderMipmapLimit);
                });
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void AGeneratedTexturesMetaKeepsItsIgnoreFlag()
        {
            var settings = new UnityEditor.AssetImporters.TextureGenerationSettings(UnityEditor.TextureImporterType.Default);
            var meta = typeof(UnityEditor.AssetImporters.TextureGenerator).GetMethod("Meta", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            settings.textureImporterSettings.ignoreMipmapLimit = true;
            Assert.Contains("\n  ignoreMipmapLimit: 1\n", (string)meta.Invoke(null, new object[] { settings }));
            settings.textureImporterSettings.ignoreMipmapLimit = false;
            Assert.Contains("\n  ignoreMipmapLimit: 0\n", (string)meta.Invoke(null, new object[] { settings }));
        }

        [Fact]
        public void AScriptLimitIsKeptByTheActiveLevel()
        {
            var levels = QualitySettings.s_LevelSettings;
            int level = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            int limit = QualitySettings.globalTextureMipmapLimit;
            static Dictionary<string, JsonElement> Level(int limit) =>
                new Dictionary<string, JsonElement> { ["globalTextureMipmapLimit"] = JsonSerializer.SerializeToElement(limit) };
            try
            {
                QualitySettings.Internal_SetNames(new[] { "Full", "Quarter" });
                QualitySettings.s_LevelSettings = new List<Dictionary<string, JsonElement>> { Level(0), Level(2) };
                QualitySettings.SetQualityLevel(1);
                Assert.Equal(2, QualitySettings.globalTextureMipmapLimit);
                QualitySettings.globalTextureMipmapLimit = 1;
                QualitySettings.SetQualityLevel(0);
                Assert.Equal(0, QualitySettings.globalTextureMipmapLimit);
                QualitySettings.SetQualityLevel(1);
                Assert.Equal(1, QualitySettings.globalTextureMipmapLimit);
            }
            finally
            {
                QualitySettings.s_LevelSettings = levels;
                QualitySettings.Internal_SetNames(names);
                QualitySettings.SetQualityLevel(level);
                QualitySettings.globalTextureMipmapLimit = limit;
            }
        }
    }
}
