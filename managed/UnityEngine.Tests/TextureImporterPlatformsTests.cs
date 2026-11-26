using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A texture importer's per-platform settings as the Unity 6000.0.71f1 Editor reads,
    /// stores, clears and saves them (tests/projects/textureplatforms), on an installation whose
    /// platform modules are Android's.</summary>
    public sealed class TextureImporterPlatformsTests : IDisposable
    {
        public TextureImporterPlatformsTests() => ImporterMeta.s_TextureImportPlatformsForTests = new[] { "Standalone", "Android" };

        public void Dispose() => ImporterMeta.s_TextureImportPlatformsForTests = null;

        static string Entry(string target, int max = 2048, int quality = 50, int format = -1, int overridden = 0) =>
            "  - serializedVersion: 4\n    buildTarget: " + target + "\n    maxTextureSize: " + max + "\n    resizeAlgorithm: 0\n    textureFormat: " + format
            + "\n    textureCompression: 1\n    compressionQuality: " + quality + "\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: " + overridden
            + "\n    ignorePlatformSupport: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n";

        static string Meta(params string[] entries) =>
            "fileFormatVersion: 2\nguid: 9123456789abcdef0123456789abcdef\nTextureImporter:\n  serializedVersion: 13\n  isReadable: 0\n  ignorePngGamma: 0\n  platformSettings:\n"
            + string.Concat(entries) + "  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

        static TextureImporter Importer(string meta) => (TextureImporter)AssetImporter.FromMeta("Assets/Platforms.png", "9123456789abcdef0123456789abcdef",
            "TextureImporter", TestYaml.DocumentBody(meta.Substring(meta.IndexOf("TextureImporter:", StringComparison.Ordinal))));

        static string[] Names(TextureImporter importer) =>
            importer.m_Settings.Get("platformSettings").Items.Select(i => i.Get("buildTarget").AsString()).ToArray();

        /// <summary>A platform the importer imports for that the .meta lacks gets an entry when the
        /// importer loads: the default entry as it was then (load.copy: Android keeps 512 and 60
        /// after the default changes to 1024 and 10); Standalone, then the modules (load.order).</summary>
        [Fact]
        public void MissingPlatformsAreTheDefaultAsLoaded()
        {
            var importer = Importer(Meta(Entry("DefaultTexturePlatform", 512, 60), Entry("iOS")));
            Assert.Equal(new[] { "DefaultTexturePlatform", "iOS", "Standalone", "Android" }, Names(importer));
            importer.maxTextureSize = 1024;
            importer.compressionQuality = 10;
            var android = importer.GetPlatformTextureSettings("Android");
            Assert.Equal(("Android", false, 512, 60), (android.name, android.overridden, android.maxTextureSize, android.compressionQuality));
            Assert.Equal(1024, importer.GetDefaultPlatformTextureSettings().maxTextureSize);
            // Without a default entry, the default is added at its defaults (load.nodefault).
            var none = Importer(Meta(Entry("Standalone", 256), Entry("Android"), Entry("iOS")));
            Assert.Equal(new[] { "Standalone", "Android", "iOS", "DefaultTexturePlatform" }, Names(none));
            Assert.Equal(2048, none.maxTextureSize);
        }

        /// <summary>A platform without an entry reads the default's settings under its own name, not
        /// overridden; one the importer does not import for gets no entry (get.tvOS, get.Foo); a stored
        /// one reads its own, overridden or not, whatever the platform (get.WebGL, get.Standalone).</summary>
        [Fact]
        public void APlatformWithoutAnEntryReadsTheDefault()
        {
            var importer = Importer(Meta(Entry("DefaultTexturePlatform", 4096, 70), Entry("Standalone", 1024, 40), Entry("Android", 512, 80, 47, 1),
                Entry("WebGL", 256, 50, 12, 1)));
            var tv = importer.GetPlatformTextureSettings("tvOS");
            Assert.Equal(("tvOS", false, 4096, 70), (tv.name, tv.overridden, tv.maxTextureSize, tv.compressionQuality));
            Assert.Equal(("", 4096), (importer.GetPlatformTextureSettings(null).name, importer.GetPlatformTextureSettings(null).maxTextureSize));
            Assert.Equal(("iOS", false), (importer.GetPlatformTextureSettings("iPhone").name, importer.GetPlatformTextureSettings("iPhone").overridden));
            var web = importer.GetPlatformTextureSettings("WebGL");
            Assert.Equal((true, 256, 12), (web.overridden, web.maxTextureSize, (int)web.format));
            var desktop = importer.GetPlatformTextureSettings("StandaloneWindows64");
            Assert.Equal(("Standalone", false, 1024, 40), (desktop.name, desktop.overridden, desktop.maxTextureSize, desktop.compressionQuality));
            Assert.True(importer.GetPlatformTextureSettings("Android", out int max, out TextureImporterFormat format, out int quality, out bool split));
            Assert.Equal((512, 47, 80, false), (max, (int)format, quality, split));
            Assert.Equal(new[] { "DefaultTexturePlatform", "Standalone", "Android", "WebGL" }, Names(importer));
            Assert.False(EditorUtility.IsDirty(importer));
            // A copy each time (get.copy).
            importer.GetPlatformTextureSettings("Android").maxTextureSize = 32;
            Assert.Equal(512, importer.GetPlatformTextureSettings("Android").maxTextureSize);
        }

        /// <summary>Set stores every field in the entry of its (normalized) name or after the
        /// others, overridden or not and whatever the platform; null throws (set.*, setoff.*).</summary>
        [Fact]
        public void SetStoresTheEntryInItsPlaceOrAfterTheOthers()
        {
            var importer = Importer(Meta(Entry("DefaultTexturePlatform"), Entry("Standalone"), Entry("Android"), Entry("iOS")));
            var phone = new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256, format = TextureImporterFormat.ASTC_6x6, ignorePlatformSupport = true };
            importer.SetPlatformTextureSettings(phone);
            Assert.Equal("iOS", phone.name);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "tvOS", maxTextureSize = 1024 });
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "", overridden = true, maxTextureSize = 32 });
            Assert.True(EditorUtility.IsDirty(importer));
            Assert.Equal(new[] { "DefaultTexturePlatform", "Standalone", "Android", "iOS", "tvOS", "" }, Names(importer));
            var stored = importer.GetPlatformTextureSettings("iOS");
            Assert.Equal((true, true, 256, 50), (stored.overridden, stored.ignorePlatformSupport, stored.maxTextureSize, (int)stored.format));
            Assert.False(importer.GetPlatformTextureSettings("tvOS").overridden);
            Assert.Throws<NullReferenceException>(() => importer.SetPlatformTextureSettings(null));
#pragma warning disable 618
            importer.SetPlatformTextureSettings("Android", 128, TextureImporterFormat.ETC2_RGBA8, 70, true);
#pragma warning restore 618
            var android = importer.GetPlatformTextureSettings("Android");
            Assert.Equal((true, 128, 47, 70, true), (android.overridden, android.maxTextureSize, (int)android.format, android.compressionQuality, android.allowsAlphaSplitting));
        }

        /// <summary>Clear removes the entry and dirties the importer, an entry or none (clear.missing);
        /// a read of a platform the importer imports for then adds it again as the default
        /// (clear.Android, clear.StandaloneWindows64); another platform stays without (clear.WebGL).</summary>
        [Fact]
        public void ClearRemovesTheEntryAndAReadAddsItAgain()
        {
            var importer = Importer(Meta(Entry("DefaultTexturePlatform", 1024), Entry("Standalone", 512, 50, -1, 1), Entry("Android", 256, 50, 47, 1),
                Entry("iOS", 128, 50, 50, 1), Entry("WebGL", 64, 50, -1, 1)));
            importer.ClearPlatformTextureSettings("tvOS");
            Assert.True(EditorUtility.IsDirty(importer));
            importer.ClearPlatformTextureSettings("Android");
            importer.ClearPlatformTextureSettings("iPhone");
            importer.ClearPlatformTextureSettings("StandaloneWindows64");
            importer.ClearPlatformTextureSettings("WebGL");
            Assert.Equal(new[] { "DefaultTexturePlatform" }, Names(importer));
            Assert.Equal((false, 1024), (importer.GetPlatformTextureSettings("Android").overridden, importer.GetPlatformTextureSettings("Android").maxTextureSize));
            importer.GetPlatformTextureSettings("WebGL");
            importer.GetPlatformTextureSettings("Standalone");
            Assert.Equal(new[] { "DefaultTexturePlatform", "Android", "Standalone" }, Names(importer));
            importer.ClearPlatformTextureSettings("DefaultTexturePlatform");
            Assert.Equal(2048, importer.GetDefaultPlatformTextureSettings().maxTextureSize);
            Assert.Equal(new[] { "Android", "Standalone", "DefaultTexturePlatform" }, Names(importer));
        }

        /// <summary>What the importer added on loading is no change: the .meta is not written for it
        /// (importermeta same.*); a change writes the list as the importer holds it, entries it
        /// added, set and cleared included (textureplatforms load.copy, clear.meta).</summary>
        [Fact]
        public void TheSavedListIsTheImportersAndTheAddedEntriesAloneAreNoChange()
        {
            var text = Meta(Entry("DefaultTexturePlatform", 1024), Entry("Standalone", 512, 50, -1, 1));
            var importer = Importer(text);
            Assert.Null(ImporterMeta.Rewritten(text, "TextureImporter", importer.m_Settings, importer.SavedSettings(), ("", "", ""), "", new List<string>()));
            importer.ClearPlatformTextureSettings("Standalone");
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "iOS", overridden = true, maxTextureSize = 128 });
            var unwritten = new List<string>();
            var written = ImporterMeta.Rewritten(text, "TextureImporter", importer.m_Settings, importer.SavedSettings(), ("", "", ""), "", unwritten);
            Assert.Empty(unwritten);
            Assert.Equal(Meta(Entry("DefaultTexturePlatform", 1024), Entry("Android", 1024), Entry("iOS", 128, 50, -1, 1)), written);
        }

        /// <summary>An entry the importer added that changed since is a change: the default it added
        /// edited through a property, another one edited in its settings tree (as a SerializedObject
        /// applies an edit).</summary>
        [Fact]
        public void AnAddedEntryChangedSinceIsAChange()
        {
            var text = Meta(Entry("Standalone", 256), Entry("Android"), Entry("iOS"));
            var importer = Importer(text);
            importer.maxTextureSize = 512;
            var written = ImporterMeta.Rewritten(text, "TextureImporter", importer.m_Settings, importer.SavedSettings(), ("", "", ""), "", new List<string>());
            Assert.NotNull(written);
            Assert.EndsWith("    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 512\n", written.Substring(0, written.IndexOf("    resizeAlgorithm", written.IndexOf("buildTarget: DefaultTexturePlatform", StringComparison.Ordinal), StringComparison.Ordinal)));

            var other = Meta(Entry("DefaultTexturePlatform"), Entry("Android"), Entry("iOS"));
            var edited = Importer(other);
            Assert.Null(ImporterMeta.Rewritten(other, "TextureImporter", edited.m_Settings, edited.SavedSettings(), ("", "", ""), "", new List<string>()));
            MetaSettings.SetInt(edited.m_Settings, "platformSettings.[buildTarget=Standalone].maxTextureSize", 64);
            var saved = ImporterMeta.Rewritten(other, "TextureImporter", edited.m_Settings, edited.SavedSettings(), ("", "", ""), "", new List<string>());
            Assert.Equal(Meta(Entry("DefaultTexturePlatform"), Entry("Android"), Entry("iOS"), Entry("Standalone", 64)), saved);
        }

        /// <summary>ignorePngGamma is the .meta's (gamma.Gamma100000Ignored), set into it
        /// (gamma.Gamma100000Toggle), and carried by TextureImporterSettings both ways
        /// (gamma.Gamma100000Toggle.settings).</summary>
        [Fact]
        public void IgnorePngGammaIsTheMetas()
        {
            var importer = Importer(Meta(Entry("DefaultTexturePlatform"), Entry("Standalone"), Entry("Android")).Replace("ignorePngGamma: 0", "ignorePngGamma: 1"));
            Assert.True(importer.ignorePngGamma);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.True(settings.ignorePngGamma);
            settings.ignorePngGamma = false;
            importer.SetTextureSettings(settings);
            Assert.False(importer.ignorePngGamma);
            Assert.Equal("0", MetaSettings.Find(importer.m_Settings, "ignorePngGamma").AsString());
            importer.ignorePngGamma = true;
            Assert.Equal("1", MetaSettings.Find(importer.m_Settings, "ignorePngGamma").AsString());
        }
    }
}
