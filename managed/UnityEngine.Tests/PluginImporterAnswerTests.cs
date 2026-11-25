using System.IO;
using UnityEditor;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>What the PluginImporter answers from its settings (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/plugindefaults Assets/Controls/*: hand-written sections).</summary>
    public class PluginImporterAnswerTests
    {
        static PluginImporter Importer(string section)
        {
            var importer = (PluginImporter)Unbound.Runtime.ObjectConstruction.CreateWithBacking(typeof(PluginImporter));
            importer.ReadSettings(TestYaml.DocumentBody(section));
            return importer;
        }

        const string AnyExcluding = @"
PluginImporter:
  isOverridable: 0
  platformData:
    Any:
      enabled: 1
      settings:
        Exclude Android: 1
        Exclude Editor: 1
    Editor:
      enabled: 0
      settings:
        DefaultValueInitialized: true
    WindowsStoreApps:
      enabled: 0
      settings:
        CPU: AnyCPU
";

        /// <summary>A platform's own setting answers, "Any Platform" aside (AnyExcluding.dll:
        /// compatible "Any", editor 0; AnyOnly.dll the same without exclusions).</summary>
        [Fact]
        public void CompatibilityIsThePlatformsOwnSetting()
        {
            using var engine = Unbound.Runtime.MainThreadCheck.Engine();
            var importer = Importer(AnyExcluding);
            try
            {
                Assert.True(importer.GetCompatibleWithAnyPlatform());
                Assert.True(importer.GetCompatibleWithPlatform("Any"));
                Assert.False(importer.GetCompatibleWithPlatform("Win64"));
                Assert.False(importer.GetCompatibleWithEditor());
                Assert.False(importer.GetCompatibleWithEditor("OSXUniversal"));
                Assert.True(importer.GetExcludeEditorFromAnyPlatform());
                Assert.True(importer.GetExcludeFromAnyPlatform("Android"));
                Assert.False(importer.GetExcludeFromAnyPlatform("Win64"));
                // GetImporters: "Any Platform" unless excluded.
                Assert.True(importer.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("Win64"));
                Assert.False(importer.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("Editor"));
                Assert.False(importer.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("Android"));
            }
            finally
            {
                Object.DestroyImmediate(importer);
            }
        }

        /// <summary>An enabled platform is compatible without "Any Platform" (EditorOnly.dll:
        /// compatible "Editor"; AnyAndAndroid.dll: compatible "Any,Android", Win64 excluded).</summary>
        [Fact]
        public void AnEnabledPlatformIsCompatibleWithoutAnyPlatform()
        {
            using var engine = Unbound.Runtime.MainThreadCheck.Engine();
            var editorOnly = Importer(@"
PluginImporter:
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 1
      settings:
        DefaultValueInitialized: true
        OS: Windows
");
            var anyAndAndroid = Importer(@"
PluginImporter:
  platformData:
    Android:
      enabled: 1
      settings: {}
    Any:
      enabled: 1
      settings:
        Exclude Win64: 1
");
            try
            {
                Assert.True(editorOnly.GetCompatibleWithEditor());
                Assert.True(editorOnly.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("Editor"));
                Assert.False(editorOnly.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("Android"));
                Assert.True(anyAndAndroid.GetCompatibleWithPlatform("Android"));
                Assert.False(anyAndAndroid.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("Win64"));
                Assert.True(anyAndAndroid.GetCompatibleWithPlatformOrAnyPlatformBuildTarget("WebGL"));
            }
            finally
            {
                Object.DestroyImmediate(editorOnly);
                Object.DestroyImmediate(anyAndAndroid);
            }
        }

        /// <summary>GetPlatformData answers for build targets; "Any" and "Editor" give "" and the
        /// Editor's settings are read with GetEditorData (plugindefaults *.data, *.editorData).</summary>
        [Fact]
        public void PlatformDataIsABuildTargetsAndEditorDataTheEditors()
        {
            using var engine = Unbound.Runtime.MainThreadCheck.Engine();
            var importer = Importer(@"
PluginImporter:
  isOverridable: 1
  platformData:
    Any:
      enabled: 1
      settings:
        Exclude Editor: 1
    Editor:
      enabled: 0
      settings:
        CPU: x86
        DefaultValueInitialized: true
    Win:
      enabled: 1
      settings:
        CPU: x86
");
            try
            {
                Assert.Equal("x86", importer.GetPlatformData("Win", "CPU"));
                Assert.Equal("x86", importer.GetPlatformData(BuildTarget.StandaloneWindows, "CPU"));
                Assert.Equal("", importer.GetPlatformData("Editor", "CPU"));
                Assert.Equal("", importer.GetPlatformData("Any", "Exclude Editor"));
                Assert.Equal("x86", importer.GetEditorData("CPU"));
                Assert.Equal("true", importer.GetEditorData("DefaultValueInitialized"));
                // A package's plug-in (its .meta's isOverridable: 1).
                Assert.True(importer.GetIsOverridable());
            }
            finally
            {
                Object.DestroyImmediate(importer);
            }
        }

        static PluginImporter At(string path, string cpuPlatform = null, string cpu = null, bool overridable = false)
        {
            var settings = "\nPluginImporter:\n  isOverridable: " + (overridable ? 1 : 0) + "\n  platformData:\n    Any:\n      enabled: 1\n      settings: {}\n";
            if (cpuPlatform != null) settings += "    " + cpuPlatform + ":\n      enabled: 1\n      settings:\n        CPU: " + cpu + "\n";
            var importer = Importer(settings);
            importer.m_AssetPath = path;
            return importer;
        }

        /// <summary>Unity's own C# (UnityCsReference 6000.0
        /// Editor/Mono/ImportSettings/DesktopPluginImporterExtension.cs CalculateFinalPluginPath):
        /// where a standalone player puts a plug-in in a build.</summary>
        [Fact]
        public void TheStandalonePlayersPutAPluginByItsKindAndCpu()
        {
            using var engine = Unbound.Runtime.MainThreadCheck.Engine();
            var managed = typeof(object).Assembly.Location;
            var cases = new (PluginImporter Importer, string Platform, BuildTarget Target, string Expected)[]
            {
                (At("Assets/Plugins/libmac.dylib"), "OSXUniversal", BuildTarget.StandaloneOSX, "libmac.dylib"),
                (At("Assets/Plugins/Kit.bundle", "OSXUniversal", "ARM64"), "OSXUniversal", BuildTarget.StandaloneOSX, Path.Combine("ARM64", "Kit.bundle")),
                (At("Assets/Plugins/Kit.bundle", "OSXUniversal", "AnyCPU"), "OSXUniversal", BuildTarget.StandaloneOSX, "Kit.bundle"),
                (At("Assets/Plugins/libstatic.a"), "OSXUniversal", BuildTarget.StandaloneOSX, ""),
                (At("Assets/Plugins/libnative.so.1"), "OSXUniversal", BuildTarget.StandaloneOSX, "libnative.so.1"),
                (At("Assets/Plugins/native.dll", "Win64", "x86_64"), "Win64", BuildTarget.StandaloneWindows64, Path.Combine("x86_64", "native.dll")),
                (At("Assets/Plugins/native.dll", "Win64", "ARM64"), "Win64", BuildTarget.StandaloneWindows64, Path.Combine("ARM64", "native.dll")),
                (At("Assets/Plugins/native.dll", "Win", "x86"), "Win", BuildTarget.StandaloneWindows, Path.Combine("x86", "native.dll")),
                (At("Assets/Plugins/native.dll", "Win64", "None"), "Win64", BuildTarget.StandaloneWindows64, ""),
                (At("Assets/Plugins/libmac.dylib"), "Win64", BuildTarget.StandaloneWindows64, ""),
                (At("Assets/Plugins/Source.cpp"), "Win64", BuildTarget.StandaloneWindows64, Path.Combine("x86_64", "Source.cpp")),
                (At("Assets/Plugins/libnative.so"), "Linux64", BuildTarget.StandaloneLinux64, "libnative.so"),
                (At("Assets/Plugins/libmac.dylib"), "Linux64", BuildTarget.StandaloneLinux64, ""),
                // A managed plug-in is usable on every player.
                (At(managed), "Linux64", BuildTarget.StandaloneLinux64, Path.GetFileName(managed)),
            };
            try
            {
                foreach (var (importer, platform, target, expected) in cases)
                    Assert.True(expected == PluginImporter.DesktopFinalPath(importer, platform, target), importer.assetPath + " " + platform + ": " + PluginImporter.DesktopFinalPath(importer, platform, target));
            }
            finally
            {
                foreach (var c in cases) Object.DestroyImmediate(c.Importer);
            }
        }

        /// <summary>Unity's own loop (PluginImporter.GetImporters): of two plug-ins going to one
        /// place an overridable one is left out, in either order; two that are not overridable both
        /// stay, and so does one without a final path (tests/projects/plugindefaults
        /// `importers.Android`).</summary>
        [Fact]
        public void AnOverridablePluginGoingWhereAnotherGoesIsLeftOut()
        {
            using var engine = Unbound.Runtime.MainThreadCheck.Engine();
            var project = At("Assets/Foo/libstatic.a");
            var package = At("Packages/p/libstatic.a", overridable: true);
            var other = At("Assets/Bar/libstatic.a");
            var unplaced = At("Packages/p/Unplaced.a", overridable: true);
            try
            {
                string Path(PluginImporter i) => i == unplaced ? "" : System.IO.Path.GetFileName(i.assetPath);
                Assert.Equal(new[] { project }, PluginImporter.LeaveOutOverridden(new[] { project, package }, Path));
                Assert.Equal(new[] { project }, PluginImporter.LeaveOutOverridden(new[] { package, project }, Path));
                Assert.Equal(new[] { project, other }, PluginImporter.LeaveOutOverridden(new[] { project, other }, Path));
                Assert.Equal(new[] { unplaced, project }, PluginImporter.LeaveOutOverridden(new[] { unplaced, project }, Path));
            }
            finally
            {
                foreach (var i in new[] { project, package, other, unplaced }) Object.DestroyImmediate(i);
            }
        }

        [Fact]
        public void TheImporterSectionOfAMetaIsReplacedOrAdded()
        {
            const string section = "PluginImporter:\n  platformData:\n";
            // A GUID-only .meta (no final line break) gets it after its GUID.
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n" + section,
                Unbound.Runtime.AssetPaths.WithImporterSection("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef", section));
            // Another importer's section is replaced, labels kept.
            Assert.Equal("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- a\n" + section,
                Unbound.Runtime.AssetPaths.WithImporterSection("fileFormatVersion: 2\r\nguid: 0123456789abcdef0123456789abcdef\r\nlabels:\r\n- a\r\nDefaultImporter:\r\n  externalObjects: {}\r\n  userData: \r\n", section));
            Assert.True(PluginImporter.IsPluginPath("Assets/Plugins/Kit.BUNDLE"));
            Assert.True(PluginImporter.IsPluginPath("Assets/Plugins/Bridge.mm"));
            Assert.False(PluginImporter.IsPluginPath("Assets/Plugins/Managed.pdb"));
        }
    }
}
