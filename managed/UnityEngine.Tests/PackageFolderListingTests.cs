using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Package root folders in the asset database's listing, as the Unity 6000.0.71f1
    /// Editor answers for them (tests/projects/packagefolders): folders of their own, found by no
    /// search; Packages is no folder; subfolders in natural order.</summary>
    public sealed class PackageFolderListingTests : System.IDisposable
    {
        public PackageFolderListingTests() => AssetDatabase.List(
            "[{\"guid\":\"00000000000000001000000000000000\",\"path\":\"Assets\",\"kind\":\"folder\",\"importer\":\"DefaultImporter\"},"
            + "{\"guid\":\"2d6ba5cbe47e6ad3c87474c56174d4e0\",\"path\":\"Packages/com.x.audio\",\"kind\":\"folder\",\"importer\":\"DefaultImporter\"},"
            + "{\"guid\":\"0000000000000000000000000000aa01\",\"path\":\"Packages/com.x.audio/Runtime10\",\"kind\":\"folder\",\"importer\":\"DefaultImporter\"},"
            + "{\"guid\":\"0000000000000000000000000000aa02\",\"path\":\"Packages/com.x.audio/runtime9\",\"kind\":\"folder\",\"importer\":\"DefaultImporter\"},"
            + "{\"guid\":\"0000000000000000000000000000aa03\",\"path\":\"Packages/com.x.audio/_Editor\",\"kind\":\"folder\",\"importer\":\"DefaultImporter\"},"
            + "{\"guid\":\"0000000000000000000000000000aa04\",\"path\":\"Packages/com.x.audio/audio.txt\",\"kind\":\"text\",\"importer\":\"TextScriptImporter\"}]");

        public void Dispose() => AssetDatabase.Invalidate();

        [Fact]
        public void APackageRootIsAFolderButPackagesIsNone()
        {
            Assert.True(AssetDatabase.IsValidFolder("Packages/com.x.audio"));
            Assert.True(AssetDatabase.IsValidFolder("Packages/com.x.audio/"));
            Assert.False(AssetDatabase.IsValidFolder("Packages"));
            Assert.Empty(AssetDatabase.GetSubFolders("Packages"));
            Assert.Empty(AssetDatabase.GetSubFolders("Packages/com.x.audio/audio.txt"));
            Assert.Empty(AssetDatabase.GetSubFolders("Packages/com.x.missing"));
        }

        [Fact]
        public void SubFoldersComeInNaturalOrder()
        {
            Assert.Equal(new[] { "Packages/com.x.audio/_Editor", "Packages/com.x.audio/runtime9", "Packages/com.x.audio/Runtime10" },
                AssetDatabase.GetSubFolders("Packages/com.x.audio"));
        }

        [Fact]
        public void NoSearchFindsARootFolder()
        {
            Assert.Contains("Packages/com.x.audio", AssetDatabase.GetAllAssetPaths());
            Assert.DoesNotContain("2d6ba5cbe47e6ad3c87474c56174d4e0", AssetDatabase.FindAssets("audio"));
            Assert.Contains("0000000000000000000000000000aa04", AssetDatabase.FindAssets("audio"));
            Assert.DoesNotContain("2d6ba5cbe47e6ad3c87474c56174d4e0", AssetDatabase.FindAssets("t:DefaultAsset"));
        }
    }
}
